# EasySlack

<img src="assets/icon-grey.png" width="128" height="128" alt="EasySlack icon" />

EasySlack is a native C# Slack connector built directly on Slack Web API and Socket Mode. It avoids a dependency on a third-party Slack wrapper while providing a small, focused API for validation, sending messages, and receiving inbound Slack message events.

## Projects

- `src/EasySlack`: class library containing the connector, auth/options models, Web API calls, and Socket Mode event handling
- `src/EasySlackConsole`: interactive console app using `Inputty` for manual Slack testing
- `src/Test.Automated`: console-based automated test runner with pass/fail output per test, suite summary, total runtime, and failed test enumeration

## Current Capabilities

- Instantiate a connector from Slack auth material
- Validate bot-token connectivity using `auth.test`
- Send a message to a channel or conversation using `chat.postMessage`
- Send a message to a user by opening a direct conversation with `conversations.open`
- Retrieve basic conversation metadata with `conversations.info`
- Retrieve basic user metadata with `users.info`
- Receive Socket Mode message events over WebSocket
- Fire async events for:
  - message received
  - connected
  - disconnected
  - action required

## Slack App Setup

Start at `https://api.slack.com/apps/` and create a new Slack app **From scratch**.

For the current version of `EasySlack`, the runtime only needs:

- a **bot token** starting with `xoxb-`
- an **app-level token** starting with `xapp-`

### Setup Order

Use this order. It reflects the working configuration for this repository:

1. Create the Slack app
2. Add OAuth scopes
3. Enable **Socket Mode** under **Settings**
4. Enable **Event Subscriptions** under **Features**
5. Confirm Slack shows:
   - `Socket Mode is enabled. You won't need to specify a Request URL.`
6. Add bot events such as `message.channels`
7. Install or reinstall the app to the workspace
8. Invite the app to the channels where you want it to operate

### 1. Basic Information

After the app is created, open **Basic Information**.

This page contains several values commonly grouped together as "Slack auth information":

- **App ID**
- **Client ID**
- **Client Secret**
- **Signing Secret**
- **Verification Token**

What they are for:

- **App ID**: Slack's identifier for the app, usually `A...`
- **Client ID**: used in OAuth authorization flows
- **Client Secret**: used when exchanging OAuth authorization codes for tokens
- **Signing Secret**: used to verify inbound HTTP requests from Slack
- **Verification Token**: legacy compatibility value; Slack recommends the Signing Secret instead

`EasySlack` does not currently use those values directly at runtime, but you may need them later if you add distributed OAuth installs, HTTP-delivered Events API, slash commands, or interactivity.

### 2. Set The App Icon

Still under **Basic Information**, go to **Display Information** and upload the app icon.

Recommended:

- use a square PNG
- use an icon that remains readable at small sizes

### 3. Add OAuth Scopes

Open **OAuth & Permissions** and add these **Bot Token Scopes**:

- `channels:history`
- `channels:read`
- `chat:write`
- `groups:history`
- `groups:read`
- `im:history`
- `im:read`
- `im:write`
- `mpim:history`
- `mpim:read`
- `mpim:write`
- `users:read`

Optional but useful:

- `chat:write.public`
- `app_mentions:read`

What these scopes are for:

- Message subscription and read access:
  - `channels:history`
  - `groups:history`
  - `im:history`
  - `mpim:history`
- Metadata lookup:
  - `channels:read`
  - `groups:read`
  - `im:read`
  - `mpim:read`
  - `users:read`
- Sending messages:
  - `chat:write`
- Sending a direct message to a user via `SendMessageToUserAsync`:
  - `im:write`
- Multi-person DM creation or opening:
  - `mpim:write`
- Posting to public channels without first inviting the app:
  - `chat:write.public`
- Receiving `@YourAppName` mentions in channels:
  - `app_mentions:read`

Important:

- After changing scopes, click **Reinstall to Workspace** or the new scopes will not take effect.

### 4. Enable Socket Mode

Open **Socket Mode** under **Settings** and enable it.

If you do not see **Socket Mode**, the app is likely not the right app type for this workflow. In practice, the cleanest fix is usually to create a fresh modern Slack app and configure it again from scratch.

Socket Mode means:

- Slack does not send events to your public web server
- your app opens an authenticated WebSocket connection to Slack
- Slack delivers Events API payloads over that WebSocket

### 5. Generate The App-Level Token

Go back to **Basic Information**, find **App-Level Tokens**, and click **Generate Token and Scopes**.

Create an app-level token with:

- `connections:write`

Slack will issue a token starting with `xapp-`.

Use that value as:

- `SlackAuthMaterial.AppToken`

### 6. Enable Event Subscriptions

Open **Event Subscriptions** under **Features** and enable them.

Important distinction:

- **Socket Mode** removes the need for a public inbound HTTP endpoint
- but you still must enable **Event Subscriptions**
- and you still must add the bot events you want Slack to deliver

When Socket Mode is enabled correctly, Slack should display a message similar to:

- `Socket Mode is enabled. You won't need to specify a Request URL.`

Under **Subscribe to bot events**, add the events you need:

- `message.channels` for public-channel messages
- `message.groups` for private-channel messages
- `message.im` for direct-message events
- `message.mpim` for multi-person direct-message events
- `app_mention` if you want the app to receive `@YourAppName` mentions in channels

Important distinction:

- `message.channels` is an **event subscription**, not a scope
- `message.groups` is an **event subscription**, not a scope
- `message.im` is an **event subscription**, not a scope
- `message.mpim` is an **event subscription**, not a scope
- `app_mention` is an **event subscription**, not a scope
- `app_mentions:read` is a **scope**

### 7. Install Or Reinstall The App

Under **OAuth & Permissions**, click **Install to Workspace** or **Reinstall to Workspace**.

After approval, Slack generates the bot token shown as:

- **Bot User OAuth Token**

That token:

- usually starts with `xoxb-`
- is the token `EasySlack` uses for:
  - `auth.test`
  - `chat.postMessage`
  - `conversations.info`
  - `conversations.open`
  - `users.info`

Use that value as:

- `SlackAuthMaterial.BotToken`

### 8. Invite The App To Conversations

Installing the app gives it credentials, but it does not automatically make the app a member of every conversation.

This is the most common cause of errors such as:

- `not_in_channel`

For public channels:

- invite the app with `/invite @YourAppName`
- or grant `chat:write.public` if you want the app to post to public channels without being invited first

For private channels:

- the app must be explicitly invited
- `chat:write.public` does not help
- open the private channel and run:

```text
/invite @YourAppName
```

For direct messages:

- `SendMessageToUserAsync` expects a **user ID** such as `U...`
- it does **not** expect a DM conversation ID such as `D...`
- the library opens or reuses the DM conversation automatically
- this requires `im:write`

## How To Find Slack IDs

`EasySlack` methods generally work with Slack IDs rather than human-readable names.

Common ID prefixes:

- `C...`: public channel
- `G...`: private channel
- `D...`: direct-message conversation
- `U...`: user
- `A...`: app

### How To Get A Channel ID

1. Open the channel in Slack
2. Look at the URL
3. The last path segment is usually the channel or conversation ID

Example:

- `https://yourworkspace.slack.com/client/T12345678/C0123456789`
- Channel ID: `C0123456789`

### How To Get A User ID

1. Open the user's profile in Slack
2. Use Slack's copy-member-ID style action if your client exposes it
3. If that option is not visible, inspect profile-related links or user-detail surfaces where the `U...` identifier appears

Example:

- User ID: `U0123456789`

### Important ID Usage Rules

- `channel send` expects a conversation ID such as `C...`, `G...`, or `D...`
- `user send` expects a user ID such as `U...`
- once a DM exists, Slack represents it as a conversation ID such as `D...`
- `SendMessageToUserAsync` avoids needing the `D...` ID up front because it accepts a `U...` user ID and opens or reuses the DM conversation

## Getting Started

Create Slack auth material with your bot token and app token, then build a connector from it.

```csharp
using EasySlack;
using System.Threading;

SlackAuthMaterial auth = new SlackAuthMaterial(
    "xoxb-your-bot-token",
    "xapp-your-app-token");

SlackConnectorOptions options = new SlackConnectorOptions(auth)
{
    AutoReconnect = true
};

using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
using SlackConnector connector = new SlackConnector(options, cancellationTokenSource);
```

### Subscribe To A Channel

The connector raises `MessageReceived` for inbound Slack message events delivered through Socket Mode. Filter to the channel you care about inside the handler.

```csharp
using EasySlack;
using System;
using System.Threading;
using System.Threading.Tasks;

SlackAuthMaterial auth = new SlackAuthMaterial(
    "xoxb-your-bot-token",
    "xapp-your-app-token");

SlackConnectorOptions options = new SlackConnectorOptions(auth);

using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
using SlackConnector connector = new SlackConnector(options, cancellationTokenSource);

string channelId = "C0123456789";

connector.MessageReceived += async (sender, eventArgs) =>
{
    if (eventArgs.ChannelId == channelId)
    {
        Console.WriteLine("New message received.");
        Console.WriteLine("User: " + eventArgs.UserId);
        Console.WriteLine("Text: " + eventArgs.Text);
        Console.WriteLine("Timestamp: " + eventArgs.Timestamp);
    }

    await Task.CompletedTask.ConfigureAwait(false);
};

await connector.StartAsync(cancellationTokenSource.Token).ConfigureAwait(false);

Console.WriteLine("Listening for messages. Press ENTER to stop.");
Console.ReadLine();

await connector.StopAsync(cancellationTokenSource.Token).ConfigureAwait(false);
```

### Send A Message To A User

To send a direct message to a user, pass the Slack user ID to `SendMessageToUserAsync`. The connector opens or reuses the direct-message conversation before posting the message.

```csharp
using EasySlack;
using System;
using System.Threading;

SlackAuthMaterial auth = new SlackAuthMaterial(
    "xoxb-your-bot-token",
    "xapp-your-app-token");

SlackConnectorOptions options = new SlackConnectorOptions(auth);

using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
using SlackConnector connector = new SlackConnector(options, cancellationTokenSource);

SlackSendMessageResult result = await connector
    .SendMessageToUserAsync("U0123456789", "Hello from EasySlack.", cancellationTokenSource.Token)
    .ConfigureAwait(false);

if (result.Ok)
{
    Console.WriteLine("Message sent.");
    Console.WriteLine("Conversation: " + result.ChannelId);
    Console.WriteLine("Timestamp: " + result.Timestamp);
}
else
{
    Console.WriteLine("Slack rejected the message: " + result.Error);
}
```

## Build

```powershell
cd C:\Code\EasySlack\src
dotnet build
dotnet run --project .\Test.Automated\Test.Automated.csproj --framework net8.0
dotnet run --project .\EasySlackConsole\EasySlackConsole.csproj --framework net8.0
```

## Attribution

<a target="_blank" href="https://icons8.com/icon/OBMhWEebAWe9/slack-new">Slack New</a> icon by <a target="_blank" href="https://icons8.com">Icons8</a>
