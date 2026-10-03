namespace EasySlack.Internal
{
    using System;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Parses Socket Mode envelopes and dispatches recognized events.
    /// </summary>
    internal class SocketModeEnvelopeProcessor
    {
        /// <summary>
        /// Processes a raw Socket Mode payload.
        /// </summary>
        /// <param name="json">The raw JSON payload.</param>
        /// <param name="acknowledgeAsync">Acknowledges the envelope when required.</param>
        /// <param name="onMessageAsync">Invoked for recognized message events.</param>
        /// <param name="onDisconnectedAsync">Invoked for disconnect envelopes.</param>
        /// <param name="onActionRequiredAsync">Invoked for operator-attention events.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when processing finishes.</returns>
        public Task ProcessAsync(
            string json,
            Func<string, CancellationToken, Task> acknowledgeAsync,
            Func<SlackMessageReceivedEventArgs, CancellationToken, Task> onMessageAsync,
            Func<SlackDisconnectedEventArgs, CancellationToken, Task> onDisconnectedAsync,
            Func<SlackActionRequiredEventArgs, CancellationToken, Task> onActionRequiredAsync,
            CancellationToken cancellationToken)
        {
            return ProcessAsync(json, default(ActivityContext), acknowledgeAsync, onMessageAsync, onDisconnectedAsync, onActionRequiredAsync, cancellationToken);
        }

        /// <summary>
        /// Processes a raw Socket Mode payload as one traced pipeline job with parse, ack, and dispatch stages.
        /// </summary>
        /// <param name="json">The raw JSON payload.</param>
        /// <param name="connection">The context of the span that established the connection, linked from the envelope span, or default.</param>
        /// <param name="acknowledgeAsync">Acknowledges the envelope when required.</param>
        /// <param name="onMessageAsync">Invoked for recognized message events.</param>
        /// <param name="onDisconnectedAsync">Invoked for disconnect envelopes.</param>
        /// <param name="onActionRequiredAsync">Invoked for operator-attention events.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when processing finishes.</returns>
        public async Task ProcessAsync(
            string json,
            ActivityContext connection,
            Func<string, CancellationToken, Task> acknowledgeAsync,
            Func<SlackMessageReceivedEventArgs, CancellationToken, Task> onMessageAsync,
            Func<SlackDisconnectedEventArgs, CancellationToken, Task> onDisconnectedAsync,
            Func<SlackActionRequiredEventArgs, CancellationToken, Task> onActionRequiredAsync,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            long started = Stopwatch.GetTimestamp();
            string envelopeType = EasySlackTelemetryNames.EnvelopeTypeInvalid;
            using Activity? activity = EasySlackTelemetry.StartEnvelope(json.Length, connection);

            try
            {
                JsonDocument document = ParseStage(json);

                using (document)
                {
                    JsonElement root = document.RootElement;
                    string? envelopeId = TryGetString(root, "envelope_id");
                    string? type = TryGetString(root, "type");
                    envelopeType = EasySlackTelemetry.NormalizeEnvelopeType(type);
                    EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeEnvelopeType, envelopeType);
                    EasySlackTelemetry.SetTag(activity, EasySlackTelemetryNames.AttributeEnvelopeId, envelopeId);

                    await AckStageAsync(envelopeId, acknowledgeAsync, cancellationToken).ConfigureAwait(false);

                    long dispatchStarted = Stopwatch.GetTimestamp();
                    using (Activity? dispatchActivity = EasySlackTelemetry.StartStage(EasySlackTelemetryNames.StageDispatch))
                    {
                        try
                        {
                            await DispatchAsync(root, type, json, onMessageAsync, onDisconnectedAsync, onActionRequiredAsync, cancellationToken).ConfigureAwait(false);
                            EasySlackTelemetry.CompleteStage(dispatchActivity, EasySlackTelemetryNames.StageDispatch, dispatchStarted, EasySlackTelemetryNames.OutcomeSuccess, null);
                        }
                        catch (Exception exception) when (EasySlackTelemetry.FailStage(dispatchActivity, EasySlackTelemetryNames.StageDispatch, dispatchStarted, exception))
                        {
                            throw;
                        }
                    }
                }

                EasySlackTelemetry.CompleteEnvelope(activity, envelopeType, started, EasySlackTelemetryNames.OutcomeSuccess, null);
            }
            catch (Exception exception) when (EasySlackTelemetry.FailEnvelope(activity, envelopeType, started, exception))
            {
                throw;
            }
        }

        private static JsonDocument ParseStage(string json)
        {
            long started = Stopwatch.GetTimestamp();
            using Activity? activity = EasySlackTelemetry.StartStage(EasySlackTelemetryNames.StageParse);

            try
            {
                JsonDocument document = JsonDocument.Parse(json);
                EasySlackTelemetry.CompleteStage(activity, EasySlackTelemetryNames.StageParse, started, EasySlackTelemetryNames.OutcomeSuccess, null);
                return document;
            }
            catch (Exception exception) when (EasySlackTelemetry.FailStage(activity, EasySlackTelemetryNames.StageParse, started, exception))
            {
                throw;
            }
        }

        private static async Task AckStageAsync(string? envelopeId, Func<string, CancellationToken, Task> acknowledgeAsync, CancellationToken cancellationToken)
        {
            long started = Stopwatch.GetTimestamp();

            if (string.IsNullOrWhiteSpace(envelopeId))
            {
                EasySlackTelemetry.CompleteStage(null, EasySlackTelemetryNames.StageAck, started, EasySlackTelemetryNames.OutcomeSkipped, null);
                return;
            }

            using Activity? activity = EasySlackTelemetry.StartStage(EasySlackTelemetryNames.StageAck);

            try
            {
                await acknowledgeAsync(envelopeId, cancellationToken).ConfigureAwait(false);
                EasySlackTelemetry.CompleteStage(activity, EasySlackTelemetryNames.StageAck, started, EasySlackTelemetryNames.OutcomeSuccess, null);
            }
            catch (Exception exception) when (EasySlackTelemetry.FailStage(activity, EasySlackTelemetryNames.StageAck, started, exception))
            {
                throw;
            }
        }

        private static async Task DispatchAsync(
            JsonElement root,
            string? type,
            string json,
            Func<SlackMessageReceivedEventArgs, CancellationToken, Task> onMessageAsync,
            Func<SlackDisconnectedEventArgs, CancellationToken, Task> onDisconnectedAsync,
            Func<SlackActionRequiredEventArgs, CancellationToken, Task> onActionRequiredAsync,
            CancellationToken cancellationToken)
        {
            if (string.Equals(type, "events_api", StringComparison.OrdinalIgnoreCase))
            {
                await ProcessEventsApiEnvelopeAsync(root, json, onMessageAsync, onActionRequiredAsync, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(type, "disconnect", StringComparison.OrdinalIgnoreCase))
            {
                SlackDisconnectedEventArgs disconnected = new SlackDisconnectedEventArgs
                {
                    Reason = ExtractDisconnectReason(root),
                    WillReconnect = true
                };

                await onDisconnectedAsync(disconnected, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!string.Equals(type, "hello", StringComparison.OrdinalIgnoreCase))
            {
                SlackActionRequiredEventArgs actionRequired = new SlackActionRequiredEventArgs
                {
                    Code = "unsupported_socket_envelope",
                    Description = "Received unsupported Socket Mode envelope type: " + type,
                    RawPayload = json
                };

                await onActionRequiredAsync(actionRequired, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task ProcessEventsApiEnvelopeAsync(
            JsonElement root,
            string json,
            Func<SlackMessageReceivedEventArgs, CancellationToken, Task> onMessageAsync,
            Func<SlackActionRequiredEventArgs, CancellationToken, Task> onActionRequiredAsync,
            CancellationToken cancellationToken)
        {
            if (!root.TryGetProperty("payload", out JsonElement payload) || !payload.TryGetProperty("event", out JsonElement eventElement))
            {
                EasySlackTelemetry.RecordEventReceived(EasySlackTelemetryNames.EventTypeMissing);
                return;
            }

            string? eventType = TryGetString(eventElement, "type");
            EasySlackTelemetry.RecordEventReceived(EasySlackTelemetry.NormalizeEventType(eventType));
            if (string.Equals(eventType, "message", StringComparison.OrdinalIgnoreCase))
            {
                SlackMessageReceivedEventArgs message = new SlackMessageReceivedEventArgs
                {
                    ChannelId = TryGetString(eventElement, "channel"),
                    UserId = TryGetString(eventElement, "user"),
                    Text = TryGetString(eventElement, "text"),
                    Timestamp = TryGetString(eventElement, "ts"),
                    ThreadTimestamp = TryGetString(eventElement, "thread_ts"),
                    Subtype = TryGetString(eventElement, "subtype"),
                    RawPayload = json
                };

                await onMessageAsync(message, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (string.Equals(eventType, "app_rate_limited", StringComparison.OrdinalIgnoreCase))
            {
                SlackActionRequiredEventArgs actionRequired = new SlackActionRequiredEventArgs
                {
                    Code = "app_rate_limited",
                    Description = "Slack reported an app_rate_limited event.",
                    RawPayload = json
                };

                await onActionRequiredAsync(actionRequired, cancellationToken).ConfigureAwait(false);
            }
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) return null;
            if (property.ValueKind == JsonValueKind.Null || property.ValueKind == JsonValueKind.Undefined) return null;
            return property.GetString();
        }

        private static string ExtractDisconnectReason(JsonElement root)
        {
            if (root.TryGetProperty("reason", out JsonElement reasonElement))
            {
                string? reason = reasonElement.GetString();
                if (!string.IsNullOrWhiteSpace(reason)) return reason;
            }

            if (root.TryGetProperty("debug_info", out JsonElement debugInfo))
            {
                string? host = TryGetString(debugInfo, "host");
                string? buildNumber = TryGetString(debugInfo, "build_number");
                if (!string.IsNullOrWhiteSpace(host) || !string.IsNullOrWhiteSpace(buildNumber))
                {
                    return "Slack requested disconnect. Host=" + host + ", Build=" + buildNumber;
                }
            }

            return "Slack requested disconnect.";
        }
    }
}
