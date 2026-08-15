# Changelog

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
