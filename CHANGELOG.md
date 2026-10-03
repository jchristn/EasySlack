# Changelog

## 1.1.0 - 2026-10-02

- Added built-in observability. EasySlack now emits metrics and traces on a `System.Diagnostics.Metrics.Meter` and `System.Diagnostics.ActivitySource`, both named `EasySlack`, with no new package dependencies.
- Metrics cover public operations, every Slack Web API call (by method, outcome, HTTP status, and Slack error code), Socket Mode connects, disconnects, reconnect backoff, and receive loop exits, the inbound envelope pipeline (per-stage parse, ack, and dispatch), application handler duration and failures, ActionRequired conditions, connection state, an active connection gauge, a last-processed-envelope timestamp, and build info.
- Traces add an operation span per public call, a client span per Slack API call, a connect span, and one consumer trace per inbound envelope with a child span per stage and per handler raise, linked back to the connection.
- Added `EasySlackTelemetryNames`, the public constants for every meter, instrument, span, and attribute name.
- Added `TELEMETRY.md` with the full metrics and spans catalog, subscription examples (Radiant, OpenTelemetry SDK), recommended PromQL alerts, and a dashboard map.
- Added a telemetry test suite (in-memory `MeterListener` and `ActivityListener`) covering every instrumented category, the failure paths, and the no-listener path.

## Testing infrastructure - 2026-08-15

- Introduced Touchstone-based testing infrastructure with a single shared source of truth for test descriptors.
- Added `Test.Shared`, a runner-agnostic library (`Touchstone.Core`) holding every EasySlack test case and its test doubles.
- Migrated `Test.Automated` to the Touchstone console runner (`Touchstone.Cli`) executing the shared descriptors.
- Added `Test.Xunit` (Touchstone xUnit adapter) and `Test.Nunit` (Touchstone NUnit adapter) exposing the same shared descriptors under `dotnet test`.
- Expanded coverage to an exhaustive positive and negative suite across auth material, options, the Web API surface, connector lifecycle, Socket Mode processing, and envelope parsing.

## 0.1.0 - 2026-03-18

- Created the `EasySlack` solution.
- Added a native Slack connector using Slack Web API and Socket Mode.
- Added the `EasySlackConsole` interactive manual test application using `Inputty`.
- Added async event support for connection, disconnection, message receipt, and action-required conditions.
- Added a console-based `Test.Automated` project with pass/fail output and runtime reporting.
