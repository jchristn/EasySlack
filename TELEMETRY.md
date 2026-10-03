# EasySlack Telemetry

EasySlack emits metrics and traces through the .NET base class library: one `System.Diagnostics.Metrics.Meter` and one `System.Diagnostics.ActivitySource`, both named **`EasySlack`**. Names follow OpenTelemetry conventions, so any OpenTelemetry-compatible collector (Radiant, the OpenTelemetry .NET SDK, `dotnet-counters`, `dotnet-monitor`) can pick them up and export them to Prometheus, Tempo, Loki, or any OTLP backend.

EasySlack is a library. It takes no dependency on an exporter or SDK and never opens a connection to a telemetry backend. It emits; your host collects. With no listener attached, spans are not created and instruments short-circuit, so the cost is a few nanoseconds per call and no allocations. Instrumentation is best-effort: a failure inside telemetry recording is swallowed and never affects the connector.

The goal is operational: an on-call engineer, looking only at dashboards and traces, should be able to tell **where the time went** (Slack Web API, Socket Mode connect, envelope parse/ack/dispatch, or your own event handlers) and **what failed** (Slack error code, HTTP status, exception type, disconnect source, receive loop fault).

## Contents

1. [Subscribing](#subscribing)
2. [Histogram buckets](#histogram-buckets)
3. [Metrics catalog](#metrics-catalog)
4. [Attribute values](#attribute-values)
5. [Spans catalog](#spans-catalog)
6. [Trace topology](#trace-topology)
7. [Recommended PromQL alerts](#recommended-promql-alerts)
8. [Dashboard map](#dashboard-map)
9. [Privacy and cardinality](#privacy-and-cardinality)
10. [Known gaps](#known-gaps)

## Subscribing

There is nothing to enable inside EasySlack. Subscribe your host's collector to the `EasySlack` meter and activity source. The names are exposed as constants on `EasySlack.EasySlackTelemetryNames.MeterName` and `EasySlackTelemetryNames.ActivitySourceName`.

### Radiant

```csharp
using EasySlack;
using Radiant;

RadiantSettings settings = new RadiantSettings("my-slack-bot");
settings.Sources.AddMeter(EasySlackTelemetryNames.MeterName);
settings.Sources.AddActivitySource(EasySlackTelemetryNames.ActivitySourceName);

using (RadiantHost host = RadiantHost.Start(settings))
{
    // create and run your SlackConnector here
}
```

If your host also runs Watson, add `"Watson"` to both lists so HTTP spans and EasySlack spans land in the same traces.

### OpenTelemetry .NET SDK

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(EasySlackTelemetryNames.MeterName)
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(tracing => tracing
        .AddSource(EasySlackTelemetryNames.ActivitySourceName)
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://127.0.0.1:4317")));
```

### Ad hoc inspection

```bash
dotnet-counters monitor --process-id <pid> --counters EasySlack
```

### Runtime metrics

EasySlack does not emit runtime (GC, thread pool, allocation) metrics; that is the host's job. Radiant emits them by default. With the OpenTelemetry SDK, add `AddRuntimeInstrumentation()` as shown above.

### Logs

EasySlack has no logging dependency. The existing `SlackConnector.Logger` callback (`Action<string>`) is unchanged. If you forward it to an `ILogger` that ships to Loki, any line written while an EasySlack span is active is correlated through `Activity.Current`.

## Histogram buckets

All durations are recorded in **seconds** (UCUM `s`) and sizes in **bytes** (`By`). No quantiles are computed in process. Derive p50/p95/p99 in Grafana from the histogram buckets.

On .NET 9 and later (the `net10.0` build), every histogram carries bucket advice that the OpenTelemetry SDK and Radiant honor automatically:

- Durations: `0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10, 30, 60`
- `easyslack.socket.message.size`: `256, 1024, 4096, 16384, 65536, 262144, 1048576`

On .NET 8 the runtime has no bucket advice API, and the OpenTelemetry SDK default boundaries (0 to 10000) are tuned for milliseconds. If your host targets `net8.0`, register explicit boundaries with an SDK `View` or, with Radiant, a `Convention.Histogram(name, "s", buckets, labels...)` passed to `settings.Metrics.DefineAll(...)`.

## Metrics catalog

Instrument names are dotted. A Prometheus exporter rewrites them to snake case, adds the unit suffix, and adds `_total` to counters. Label keys are rewritten the same way (`easyslack.outcome` becomes `easyslack_outcome`, `error.type` becomes `error_type`).

| Instrument | Type | Unit | Labels | Prometheus name | Description |
| --- | --- | --- | --- | --- | --- |
| `easyslack.operation.duration` | Histogram | s | `easyslack.operation`, `easyslack.outcome`, `error.type` | `easyslack_operation_duration_seconds` | Duration of a public connector operation. |
| `easyslack.operations` | Counter | {operation} | `easyslack.operation`, `easyslack.outcome`, `error.type` | `easyslack_operations_total` | Public connector operations by outcome. |
| `easyslack.api.request.duration` | Histogram | s | `slack.api.method`, `easyslack.outcome`, `http.response.status_code`, `error.type` | `easyslack_api_request_duration_seconds` | Duration of one Slack Web API call. |
| `easyslack.api.requests` | Counter | {request} | `slack.api.method`, `easyslack.outcome`, `http.response.status_code`, `error.type` | `easyslack_api_requests_total` | Slack Web API calls by method and outcome. |
| `easyslack.socket.connect.duration` | Histogram | s | `easyslack.reconnect`, `easyslack.outcome`, `error.type` | `easyslack_socket_connect_duration_seconds` | `apps.connections.open` plus the WebSocket handshake. |
| `easyslack.socket.connects` | Counter | {attempt} | `easyslack.reconnect`, `easyslack.outcome`, `error.type` | `easyslack_socket_connects_total` | Socket Mode connection attempts by outcome. |
| `easyslack.socket.connections.active` | UpDownCounter | {connection} | none | `easyslack_socket_connections_active` | Connectors currently in the `Connected` state, process-wide. |
| `easyslack.socket.disconnects` | Counter | {disconnect} | `easyslack.disconnect.source`, `easyslack.will_reconnect` | `easyslack_socket_disconnects_total` | Socket Mode disconnects. |
| `easyslack.socket.reconnect.delay` | Histogram | s | none | `easyslack_socket_reconnect_delay_seconds` | Backoff applied before each reconnect attempt. |
| `easyslack.socket.receive_loop.exits` | Counter | {exit} | `easyslack.loop.exit_reason`, `error.type` | `easyslack_socket_receive_loop_exits_total` | Receive loop terminations. `faulted` means the connector stopped receiving. |
| `easyslack.socket.message.size` | Histogram | By | none | `easyslack_socket_message_size_bytes` | Size of each inbound Socket Mode text message. |
| `easyslack.envelopes` | Counter | {envelope} | `easyslack.envelope.type`, `easyslack.outcome`, `error.type` | `easyslack_envelopes_total` | Envelopes processed (the pipeline job counter). |
| `easyslack.envelope.duration` | Histogram | s | `easyslack.envelope.type`, `easyslack.outcome`, `error.type` | `easyslack_envelope_duration_seconds` | End-to-end envelope processing time. |
| `easyslack.envelope.stage.duration` | Histogram | s | `easyslack.stage`, `easyslack.outcome`, `error.type` | `easyslack_envelope_stage_duration_seconds` | Per-stage duration (`parse`, `ack`, `dispatch`). |
| `easyslack.envelope.stage.events` | Counter | {event} | `easyslack.stage`, `easyslack.outcome`, `error.type` | `easyslack_envelope_stage_events_total` | Per-stage executions by outcome. |
| `easyslack.envelope.last_success.time` | Observable gauge | s | none | `easyslack_envelope_last_success_time_seconds` | Unix time of the most recent successfully processed envelope. Absent until the first success. |
| `easyslack.events.received` | Counter | {event} | `slack.event.type` | `easyslack_events_received_total` | Events API events inside `events_api` envelopes. |
| `easyslack.messages` | Counter | {message} | `easyslack.message.disposition` | `easyslack_messages_total` | Inbound message events: dispatched or skipped. |
| `easyslack.handler.duration` | Histogram | s | `easyslack.event`, `easyslack.outcome`, `error.type` | `easyslack_handler_duration_seconds` | Time spent in your event handlers for one event raise (all subscribers). |
| `easyslack.handler.invocations` | Counter | {invocation} | `easyslack.event`, `easyslack.outcome`, `error.type` | `easyslack_handler_invocations_total` | Event raises by outcome. |
| `easyslack.action_required` | Counter | {event} | `easyslack.action.code` | `easyslack_action_required_total` | `ActionRequired` conditions raised. |
| `easyslack.errors` | Counter | {error} | `easyslack.component`, `error.type` | `easyslack_errors_total` | Failures by component (`api`, `socket`, `envelope`, `handler`, `receive_loop`). Cancellations are not counted. |
| `easyslack.connector.state.transitions` | Counter | {transition} | `easyslack.connection.state` | `easyslack_connector_state_transitions_total` | Connection state transitions, labeled with the state entered. |
| `easyslack.build.info` | Observable gauge | {info} | `easyslack.version` | `easyslack_build_info` | Always 1, labeled with the library version. |

Notes:

- `error.type` is present only when the outcome is not `success`. For `slack_error` it is the Slack error code (for example `invalid_auth`, `channel_not_found`). For `http_error` on an API call it is the HTTP status code. Otherwise it is the exception's full type name.
- `http.response.status_code` is present only when Slack answered.
- `easyslack.operations` and `easyslack.api.requests` are separate on purpose: `send_message_to_user` is one operation that makes two API calls (`conversations.open`, then `chat.postMessage`). The `easyslack.errors` counter only counts leaf components, so one failed API call inside an operation is counted once (component `api`), not twice.
- Socket Mode has no internal queue or concurrency limiter: envelopes are processed one at a time on the receive loop, so there is no `queued` stage. A slow handler shows up directly as `dispatch` stage time and `easyslack.handler.duration`, and it delays every envelope behind it.

## Attribute values

Every metric label takes a bounded set of values.

| Label | Values |
| --- | --- |
| `easyslack.operation` | `start`, `stop`, `validate_connection`, `send_message_to_user`, `send_message_to_channel`, `get_channel_info`, `get_user_info` |
| `easyslack.outcome` | `success`, `slack_error`, `http_error`, `error`, `canceled`, `skipped` (ack stage with no envelope id) |
| `slack.api.method` | `auth.test`, `apps.connections.open`, `chat.postMessage`, `conversations.open`, `conversations.info`, `users.info` (query strings are never included) |
| `easyslack.reconnect` | `true`, `false` |
| `easyslack.disconnect.source` | `transport` (the WebSocket closed or failed), `server_request` (Slack sent a `disconnect` envelope) |
| `easyslack.will_reconnect` | `true`, `false` |
| `easyslack.loop.exit_reason` | `canceled`, `no_reconnect`, `faulted` |
| `easyslack.envelope.type` | `events_api`, `disconnect`, `hello`, `other`, `invalid` (unparseable) |
| `easyslack.stage` | `parse`, `ack`, `dispatch` |
| `slack.event.type` | `message`, `app_rate_limited`, `other`, `missing` |
| `easyslack.message.disposition` | `dispatched`, `skipped_subtype` |
| `easyslack.event` | `MessageReceived`, `Connected`, `Disconnected`, `ActionRequired` |
| `easyslack.action.code` | `unsupported_socket_envelope`, `app_rate_limited`, `invalid_socket_payload` |
| `easyslack.component` | `api`, `socket`, `envelope`, `handler`, `receive_loop` |
| `easyslack.connection.state` | `disconnected`, `connecting`, `connected`, `stopping` |
| `error.type` | Slack error code (lower-case snake case, at most 64 characters, anything else collapses to `other`), HTTP status code, or exception type name |

## Spans catalog

| Span name | Kind | Parent | Attributes | Status |
| --- | --- | --- | --- | --- |
| `easyslack <operation>` (for example `easyslack send_message_to_channel`) | Internal | Caller's `Activity.Current` (for example Watson's request span) | `easyslack.operation`, `easyslack.outcome`, `error.type`, `slack.channel.id`, `slack.message.threaded` | Error on `slack_error`, `http_error`, `error`, `canceled`; exception event attached |
| `slack <api method>` (for example `slack chat.postMessage`) | Client | Operation span or connect span | `slack.api.method`, `http.request.method`, `server.address`, `http.response.status_code`, `slack.retry_after`, `easyslack.outcome`, `error.type` | Error on non-`ok` Slack responses, HTTP errors, and exceptions |
| `slack socket_mode.connect` | Client | `easyslack start` or `easyslack disconnect` | `easyslack.reconnect`, `easyslack.outcome`, `error.type` | Error on failure, exception event attached |
| `slack socket_mode.envelope` | Consumer | New trace root, linked to the `slack socket_mode.connect` span that opened the socket | `slack.envelope.id`, `easyslack.envelope.type`, `slack.event.type`, `easyslack.message.disposition`, `easyslack.action.code`, `slack.channel.id`, `slack.message.threaded`, `easyslack.message.size`, `easyslack.outcome`, `error.type` | Error on failure |
| `stage:parse`, `stage:ack`, `stage:dispatch` | Internal | `slack socket_mode.envelope` | `easyslack.stage`, `easyslack.outcome`, `error.type` | Error on failure |
| `handler:<Event>` (for example `handler:MessageReceived`) | Internal | `stage:dispatch` (MessageReceived, ActionRequired, Disconnected from an envelope), `easyslack start` (Connected), or `easyslack disconnect` (Disconnected, Connected) | `easyslack.event`, `easyslack.handler.count`, `easyslack.outcome`, `error.type` | Error when a handler throws |
| `easyslack disconnect` | Internal | New trace root for transport disconnects; child of `stage:dispatch` for Slack `disconnect` envelopes. Always linked to the connect span of the lost connection | `easyslack.disconnect.source`, `easyslack.disconnect.reason`, `easyslack.will_reconnect`, `easyslack.reconnect.delay`, `error.type` | Unset (a disconnect is an event, not a failure; the nested reconnect span carries its own status) |

Message text, tokens, user names, and raw payloads are never placed on spans or metrics.

## Trace topology

```
Outbound (caller's trace)
easyslack send_message_to_user
├── slack conversations.open           (client)
└── slack chat.postMessage             (client)

Start (caller's trace)
easyslack start
├── slack socket_mode.connect          (client)
│   └── slack apps.connections.open    (client)
└── handler:Connected

Inbound (one new trace per envelope, linked to the connect span)
slack socket_mode.envelope             (consumer)
├── stage:parse
├── stage:ack
└── stage:dispatch
    └── handler:MessageReceived        (your code; your own spans nest here)

Transport loss (new trace, linked to the connect span)
easyslack disconnect
├── handler:Disconnected
├── slack socket_mode.connect          (reconnect = true)
│   └── slack apps.connections.open
└── handler:Connected
```

Context propagation:

- Every outbound operation nests under the caller's `Activity.Current`, so a Watson request that sends a Slack message is one trace from HTTP root to `chat.postMessage`. `HttpClient` also injects a W3C `traceparent` header on the Slack request (Slack ignores it).
- The receive loop is a background hand-off. It explicitly detaches from the span that started it, so each inbound envelope begins its own trace instead of nesting under `easyslack start` for the life of the connection. A span link back to the connect span preserves navigation in Tempo.
- Spans you start inside `MessageReceived`, `Connected`, `Disconnected`, or `ActionRequired` handlers nest under the `handler:<Event>` span automatically.

## Recommended PromQL alerts

Thresholds are starting points; tune them to your traffic.

```yaml
groups:
  - name: easyslack
    rules:
      - alert: EasySlackSocketDisconnected
        # The app expects a live Socket Mode connection but none is up.
        expr: sum(easyslack_socket_connections_active) < 1
        for: 5m
      - alert: EasySlackReceiveLoopFaulted
        # The connector stopped receiving events because of an unhandled exception.
        expr: increase(easyslack_socket_receive_loop_exits_total{easyslack_loop_exit_reason="faulted"}[5m]) > 0
      - alert: EasySlackNoInboundEvents
        # Only meaningful for workspaces that normally send traffic.
        expr: time() - max(easyslack_envelope_last_success_time_seconds) > 1800
        for: 10m
      - alert: EasySlackAuthFailures
        expr: increase(easyslack_api_requests_total{error_type=~"invalid_auth|not_authed|token_revoked|token_expired|account_inactive"}[5m]) > 0
      - alert: EasySlackRateLimited
        expr: increase(easyslack_api_requests_total{http_response_status_code="429"}[10m]) > 0 or increase(easyslack_action_required_total{easyslack_action_code="app_rate_limited"}[10m]) > 0
      - alert: EasySlackApiErrorRatio
        expr: |
          sum(rate(easyslack_api_requests_total{easyslack_outcome!~"success|canceled"}[5m]))
            / clamp_min(sum(rate(easyslack_api_requests_total[5m])), 1e-9) > 0.05
        for: 10m
      - alert: EasySlackApiSlow
        expr: histogram_quantile(0.95, sum by (le, slack_api_method) (rate(easyslack_api_request_duration_seconds_bucket[5m]))) > 2
        for: 10m
      - alert: EasySlackReconnectStorm
        expr: increase(easyslack_socket_disconnects_total[15m]) > 5
      - alert: EasySlackSocketConnectFailing
        expr: increase(easyslack_socket_connects_total{easyslack_outcome!="success"}[10m]) > 2
      - alert: EasySlackHandlerSlow
        # Handlers run on the receive loop; a slow handler delays every envelope behind it.
        expr: histogram_quantile(0.95, sum by (le, easyslack_event) (rate(easyslack_handler_duration_seconds_bucket[5m]))) > 1
        for: 10m
      - alert: EasySlackHandlerErrors
        expr: increase(easyslack_handler_invocations_total{easyslack_outcome="error"}[5m]) > 0
      - alert: EasySlackEnvelopeFailures
        expr: increase(easyslack_envelopes_total{easyslack_outcome="error"}[5m]) > 0
```

## Dashboard map

EasySlack is a library and does not ship an observability stack. The host application that embeds it owns `compose.yaml`, Prometheus, Tempo, Grafana, and the product dashboards. Add an **EasySlack** (or "Slack Integration") dashboard to the host's product folder with these rows:

| Row | Panels (PromQL) | Question it answers |
| --- | --- | --- |
| Health | `sum(easyslack_socket_connections_active)`; `time() - max(easyslack_envelope_last_success_time_seconds)`; `easyslack_build_info` | Is the socket up, is traffic flowing, which version is running? |
| Web API | `sum by (slack_api_method, easyslack_outcome) (rate(easyslack_api_requests_total[5m]))`; p95 by method from `easyslack_api_request_duration_seconds_bucket`; `topk(10, sum by (error_type) (increase(easyslack_api_requests_total{easyslack_outcome!="success"}[1h])))` | Is Slack slow or rejecting us, and why? |
| Operations | rate and p95 by `easyslack_operation` from `easyslack_operation_duration_seconds` | Which public call is slow or failing? |
| Socket Mode | `rate(easyslack_socket_connects_total[5m])` by outcome and reconnect; `increase(easyslack_socket_disconnects_total[1h])` by source; reconnect delay p95; `easyslack_socket_receive_loop_exits_total` by reason | Is the connection flapping, and is it Slack or the network? |
| Inbound pipeline | envelope rate by type and outcome; p95 per stage from `easyslack_envelope_stage_duration_seconds_bucket`; `easyslack_events_received_total` by type; `easyslack_messages_total` by disposition; message size p95 | Where does inbound time go: parse, ack, or dispatch? |
| Handlers | p95 by `easyslack_event` from `easyslack_handler_duration_seconds_bucket`; error rate from `easyslack_handler_invocations_total` | Is the application's own code the bottleneck? |
| Errors | `sum by (easyslack_component, error_type) (increase(easyslack_errors_total[1h]))`; `easyslack_action_required_total` by code | What failed, grouped by component? |

From any panel, jump to Tempo with `{ resource.service.name = "<your service>" && name =~ "slack .*" && status = error }` to see a failing call's trace.

## Privacy and cardinality

- Metric labels are bounded. Ids (channel, user, envelope), message text, reasons, and free-form strings never appear on metrics.
- Spans carry the channel id and envelope id for navigation, plus the free-form disconnect reason. They never carry message text, user ids, user names, tokens, or raw payloads. Slack error codes are normalized to a bounded form before being used as `error.type`.
- Exception events on spans include the exception message and stack trace (standard OpenTelemetry `exception` event). EasySlack's own exception messages contain Slack error codes but no tokens or payloads.
- The pre-existing `SlackConnector.Logger` callback is separate from telemetry and does write raw Socket Mode payloads and ids at diagnostic verbosity. Do not forward it to a shared log store unless that is acceptable for your workspace.

## Known gaps

- Config gauges are not emitted. Connector options (reconnect delays, buffer size, auto-reconnect) are per instance, and a process can host several connectors with no bounded identity to label them by. The effective backoff is visible through `easyslack.socket.reconnect.delay`.
- On `net8.0`, histogram bucket advice is unavailable; configure buckets in the host (see [Histogram buckets](#histogram-buckets)).
- Pre-existing behavior, surfaced by this telemetry and not changed by it: after an automatic reconnect, the reconnect path starts a new receive loop while the loop that handled the disconnect keeps running. With a real `ClientWebSocket` the two loops race on `ReceiveAsync`, and one of them ends with `InvalidOperationException`. The connector keeps receiving on the surviving loop, but `easyslack.socket.receive_loop.exits{easyslack.loop.exit_reason="faulted"}` increments. Until that is fixed, correlate the `EasySlackReceiveLoopFaulted` alert with `easyslack_socket_connections_active`: a fault with an active connection right after a reconnect is this race, while a fault followed by zero active connections or a stale `easyslack_envelope_last_success_time_seconds` is a real outage.
