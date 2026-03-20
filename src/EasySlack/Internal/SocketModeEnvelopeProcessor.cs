namespace EasySlack.Internal
{
    using System;
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
        public async Task ProcessAsync(
            string json,
            Func<string, CancellationToken, Task> acknowledgeAsync,
            Func<SlackMessageReceivedEventArgs, CancellationToken, Task> onMessageAsync,
            Func<SlackDisconnectedEventArgs, CancellationToken, Task> onDisconnectedAsync,
            Func<SlackActionRequiredEventArgs, CancellationToken, Task> onActionRequiredAsync,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            using (JsonDocument document = JsonDocument.Parse(json))
            {
                JsonElement root = document.RootElement;
                string? envelopeId = TryGetString(root, "envelope_id");
                string? type = TryGetString(root, "type");

                if (!string.IsNullOrWhiteSpace(envelopeId))
                {
                    await acknowledgeAsync(envelopeId, cancellationToken).ConfigureAwait(false);
                }

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
        }

        private static async Task ProcessEventsApiEnvelopeAsync(
            JsonElement root,
            string json,
            Func<SlackMessageReceivedEventArgs, CancellationToken, Task> onMessageAsync,
            Func<SlackActionRequiredEventArgs, CancellationToken, Task> onActionRequiredAsync,
            CancellationToken cancellationToken)
        {
            if (!root.TryGetProperty("payload", out JsonElement payload)) return;
            if (!payload.TryGetProperty("event", out JsonElement eventElement)) return;

            string? eventType = TryGetString(eventElement, "type");
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
