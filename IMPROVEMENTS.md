# EasySlack Improvements For AssistantHub Slack Integration

## Goal

Add the minimum library support required for AssistantHub to run a usable Slack channel bot in v1, without forcing downstream consumers to fork the library or post top-level replies into noisy channels.

The required EasySlack work is narrow:

1. Support sending replies into an existing Slack thread via `thread_ts`
2. Surface inbound `thread_ts` on message events so consumers can map Slack threads to their own conversation IDs
3. Update tests, XML docs, README examples, and the console sample to reflect the new contract

This document is a plan only. It does not implement the changes.

## Required Changes

### 1. Extend outbound send API to support `thread_ts`

Current gap:

- `ISlackConnector.SendMessageToChannelAsync(string channelId, string text, CancellationToken cancellationToken = default)`
- `SlackConnector.SendMessageToChannelAsync(...)`

These only send `{ channel, text }` to `chat.postMessage`. That is insufficient for a threaded assistant experience.

Required change:

- Add optional thread support to channel sends
- Preferred shape:
  - `Task<SlackSendMessageResult> SendMessageToChannelAsync(string channelId, string text, string? threadTimestamp = null, CancellationToken cancellationToken = default);`

Implementation requirements:

- Only include `thread_ts` in the outbound JSON body when a non-empty value is supplied
- Preserve existing behavior when `threadTimestamp` is null or whitespace
- Keep input validation consistent with existing methods
- Keep the send result contract unchanged unless a concrete Slack API need is discovered

Backward compatibility:

- Do not remove channel-send capability or force callers to pass a thread timestamp
- If necessary for compatibility, add an overload and have the old signature delegate to the new one
- Update both the interface and implementation together

Files:

- `src/EasySlack/ISlackConnector.cs`
- `src/EasySlack/SlackConnector.cs`
- `src/EasySlack/EasySlack.xml`

### 2. Surface inbound `thread_ts` on message events

Current gap:

- `SlackMessageReceivedEventArgs` exposes `ChannelId`, `UserId`, `Text`, `Timestamp`, `Subtype`, `RawPayload`
- `SocketModeEnvelopeProcessor` parses `ts` but not `thread_ts`

AssistantHub needs to know whether an incoming message belongs to a thread and, if so, which thread root Slack assigned.

Required change:

- Add `ThreadTimestamp` to `SlackMessageReceivedEventArgs`
- Populate it from the inbound Slack event’s `thread_ts`

Behavior requirements:

- For thread replies, `ThreadTimestamp` should contain the Slack thread root timestamp
- For top-level channel messages, `ThreadTimestamp` may be null; downstream consumers can fall back to `Timestamp` when they want to treat the root message as the conversation key
- Do not guess or rewrite values inside the library; expose Slack’s data as received

Files:

- `src/EasySlack/SlackMessageReceivedEventArgs.cs`
- `src/EasySlack/Internal/SocketModeEnvelopeProcessor.cs`
- `src/EasySlack/EasySlack.xml`

### 3. Keep subtype filtering behavior, but document it clearly

Current behavior:

- `SlackConnector.HandleMessageReceivedAsync(...)` drops messages with a non-empty `Subtype`

That behavior is still useful because it suppresses common bot/system events by default. AssistantHub can use the existing `RawPayload` plus `UserId` and validation info to implement its own self-message protection.

Required action:

- Do not broaden message delivery in this change set unless tests show a concrete requirement
- Document in README/XML that only plain message events are raised by default and subtype messages are skipped

This keeps the library focused and avoids widening the event contract during the same release as thread support.

## Tests

Automated coverage must be updated before release.

### Connector API tests

Add coverage in `src/Test.Automated/Suites/ConnectorApiTests.cs` for:

- Channel send without `thread_ts` preserves existing JSON body
- Channel send with `thread_ts` includes `"thread_ts":"..."`
- User send with thread support behavior is explicit

Decision for direct messages:

- Either keep `SendMessageToUserAsync` unchanged and let callers open a DM then call `SendMessageToChannelAsync` for threaded follow-ups
- Or add parallel thread support to `SendMessageToUserAsync`

Preferred plan:

- Keep `SendMessageToUserAsync` unchanged for now
- Limit the v1 library change to channel/conversation sends because AssistantHub’s Slack channel bot path uses channel IDs plus thread timestamps
- Document that threaded replies in DMs should use the channel/conversation ID after open/reuse if needed

### Socket Mode processing tests

Add coverage in `src/Test.Automated/Suites/SocketModeProcessingTests.cs` for:

- Inbound top-level message leaves `ThreadTimestamp` empty
- Inbound threaded reply populates `ThreadTimestamp`
- Existing ack behavior still happens for threaded events
- Existing subtype filtering still prevents dispatch of subtype messages

## Documentation And Samples

The library documentation must be updated as part of the same change. AssistantHub depends on this library being self-explanatory.

### README

Update `README.md` to include:

- Threaded reply support in the feature list
- Updated `SendMessageToChannelAsync` signature
- Example showing how to reply in-thread with `thread_ts`
- Example showing how to read `ThreadTimestamp` from `MessageReceived`
- Clarification that top-level messages may not have `thread_ts`

Recommended example pattern:

- On receive, inspect `eventArgs.ThreadTimestamp ?? eventArgs.Timestamp`
- On reply, send back to the same `channelId` with the resolved thread timestamp

### XML docs

Update `src/EasySlack/EasySlack.xml` and source comments for:

- New `threadTimestamp` parameter
- New `ThreadTimestamp` event property
- The distinction between `Timestamp` and `ThreadTimestamp`

### Console sample

Update `src/EasySlackConsole/Program.cs` so the sample app demonstrates the new capability.

Minimum changes:

- When printing inbound messages, show `ThreadTimestamp` if present
- Add an option or prompt path for sending a threaded channel reply
- Keep the existing non-threaded send path working

## Acceptance Criteria

The EasySlack library is ready for AssistantHub v1 when all of the following are true:

1. A caller can send `chat.postMessage` into an existing thread by passing `thread_ts`
2. A caller receiving a Socket Mode message can read `thread_ts` from the event args when Slack provides it
3. Existing non-threaded send behavior still works
4. Automated tests cover both threaded and non-threaded send/receive paths
5. README, XML docs, and console sample all reflect the new contract

## Non-Goals For This Change Set

These items should not be folded into the same EasySlack release unless a concrete blocker appears:

- Full Slack markdown or formatting helpers
- Assistant-specific message chunking
- Conversation persistence or thread mapping storage
- Slack rate-limit orchestration
- Broadening event delivery to every Slack message subtype

Those belong in the consuming application or in a later library expansion. The library change needed now is thread-aware transport support.
