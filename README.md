# TalkGPT

TalkGPT is a ChatGPT-style web chat built with ASP.NET Core 8, SQLite, and Hugging Face Inference Providers. It includes email/password accounts, saved conversations, an English/Hindi voice-input option, and speech playback for assistant replies.

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Run locally](#run-locally)
- [Configuration](#configuration)
- [Use TalkGPT](#use-talkgpt)
- [Accounts and chat storage](#accounts-and-chat-storage)
- [Voice chat](#voice-chat)
- [HTTP API](#http-api)
- [Project layout](#project-layout)
- [Security and privacy](#security-and-privacy)
- [Deployment notes](#deployment-notes)
- [Troubleshooting](#troubleshooting)

## Features

- Responsive chat interface for desktop and mobile.
- Hugging Face Inference Providers chat completions; the Hugging Face token stays on the server.
- Register, sign in, and sign out with an email address and password.
- Passwords are stored as salted PBKDF2-HMAC-SHA256 hashes, not plaintext.
- Account-owned conversations are stored in SQLite and can be opened again from the sidebar.
- Create a new conversation with **New chat** or the `Ctrl+K` / `⌘K` shortcut.
- English and Hindi speech-recognition language choices, where supported by the browser.
- Optional read-aloud playback for assistant responses, where supported by the device.
- Friendly, explicit responses for common model configuration, provider, and request errors.

## Requirements

- .NET 8 SDK or a later compatible SDK.
- A Hugging Face account and an access token authorized for **Inference Providers**.
- An internet connection so the server can call Hugging Face.
- A modern browser. Microphone access requires the browser to support the Web Speech API and to grant microphone permission. Use `localhost` during local development and HTTPS when deployed.

## Run locally

### 1. Get a Hugging Face token

Create a fine-grained token with **Inference Providers** permission in [Hugging Face token settings](https://huggingface.co/settings/tokens). Keep this token private; do not put it in client-side code or commit it to source control.

### 2. Set the token in PowerShell

For the current PowerShell window only:

```powershell
$env:HuggingFace__Token = "hf_your_token"
```

This environment variable is read by the ASP.NET Core server at startup. Close the terminal when finished to discard the session-scoped value.

To set a persistent user-level environment variable instead:

```powershell
[Environment]::SetEnvironmentVariable("HuggingFace__Token", "hf_your_token", "User")
```

Open a new terminal after setting a persistent variable. Avoid persistent user-level settings on shared machines.

### 3. Restore and run

From the TalkGPT project directory:

```powershell
dotnet restore
dotnet run
```

Open the `http://localhost:...` address printed by `dotnet run`. On first use, register an account. Once signed in, enter a prompt or select one of the sample prompts on the welcome screen.

To run on a specific local address:

```powershell
dotnet run --urls http://localhost:5135
```

Then open <http://localhost:5135>.

## Configuration

The application uses standard ASP.NET Core configuration. Environment variables override values in `appsettings.json`.

| Setting | Default | Purpose |
| --- | --- | --- |
| `HuggingFace__Token` | Not set | Secret Hugging Face access token. Required for model replies. |
| `HuggingFace__Model` | `Qwen/Qwen2.5-7B-Instruct-1M` | Hugging Face model identifier used for chat completions. |
| `ConnectionStrings__TalkGPT` | `<content-root>\app_data\talkgpt.db` | SQLite database file path. The parent directory is created automatically. |

The default model is configurable; it must be available to your Hugging Face account through an Inference Provider that supports chat completions. Provider availability, pricing, rate limits, and model access are determined by Hugging Face and your account.

For example, to select another model for the current PowerShell window:

```powershell
$env:HuggingFace__Model = "google/gemma-2-2b-it"
```

For example, to use a different SQLite database for the current PowerShell window:

```powershell
$env:ConnectionStrings__TalkGPT = "D:\TalkGPTData\talkgpt.db"
```

Do not add access tokens or other secrets to `appsettings.json`, client-side JavaScript, or a checked-in file.

## Use TalkGPT

### Register and sign in

1. Enter a valid email address.
2. Create a password between 10 and 128 characters, or use the password for an existing account.
3. Select **Create account** or **Sign in**.
4. Select **Sign out** in the sidebar to end the current session.

The account is local to this TalkGPT deployment. It is not a Hugging Face account, and TalkGPT does not provide email verification or password-reset emails.

### Start and reopen conversations

- Select **New chat** to start a separate conversation; older conversations remain saved.
- Select a conversation in the sidebar to reopen it.
- The conversation list and messages are associated with the signed-in account.
- A response is saved after the model successfully replies. If the model call fails, TalkGPT displays the error and does not store that incomplete exchange.

### Send messages

- Type a message and select the send button.
- Press `Enter` to send or `Shift+Enter` to insert a new line.
- The first message becomes the conversation title.
- TalkGPT includes recent conversation context when making the model request.
- Assistant replies are shown as plain text; the interface does not execute message content as HTML.

## Accounts and chat storage

The server creates the SQLite database at `app_data\talkgpt.db` under the content root by default. It contains account records and conversation/message data. The `app_data` directory is excluded by the repository `.gitignore`.

To keep conversations across redeployments, provide a persistent, writable location for `ConnectionStrings__TalkGPT` and include that database in your normal backup process. Restrict access to the database file: it contains users' email addresses and chat content.

Passwords are hashed using PBKDF2-HMAC-SHA256 with an individual random salt. Authentication uses a server-issued, HTTP-only cookie with `SameSite=Strict` and a 14-day sliding expiration. Cookies use the `Secure` flag outside the Development environment; production deployments should use HTTPS.

## Voice chat

### Voice input

1. Choose **English** or **हिन्दी** from the voice-language menu.
2. Select the microphone button and allow microphone access when the browser asks.
3. Speak; the recognized text is inserted into the message box.
4. Review or edit the text, then send it normally.

The microphone button starts or stops one speech-recognition session. Voice input does not automatically submit a message. Recognition is performed using the browser's built-in speech-recognition feature; browser/platform support and speech processing behavior may vary.

### Read a reply aloud

Select the speaker icon next to an assistant message. The browser/device's speech-synthesis support reads the selected message using the language selected in the voice menu.

Voice features are optional and do not send microphone audio to the TalkGPT server. If the browser does not support a feature, TalkGPT displays an explanatory message. Microphone use may require HTTPS outside of localhost.

## HTTP API

All endpoints are served by the same ASP.NET Core application. Account and chat endpoints marked **Signed in** require a valid TalkGPT authentication cookie. Request and response bodies use JSON unless noted.

| Method and path | Access | Description |
| --- | --- | --- |
| `POST /api/register` | Public | Create an account and sign in. JSON body: `{ "email": "...", "password": "..." }`. |
| `POST /api/login` | Public | Sign in with an existing account. JSON body: `{ "email": "...", "password": "..." }`. |
| `GET /api/account` | Signed in | Return the signed-in account email. |
| `POST /api/logout` | Signed in | Sign out and clear the authentication cookie. |
| `GET /api/conversations` | Signed in | List conversation IDs, titles, and update times belonging to the current account. |
| `GET /api/conversations/{id}` | Signed in | Return a conversation and its messages if it belongs to the current account. |
| `POST /api/chat` | Signed in | Request a Hugging Face chat completion and save the successful exchange. |

Example `POST /api/chat` request:

```json
{
  "message": "Explain photosynthesis in simple terms.",
  "history": [],
  "conversationId": "0123456789abcdef0123456789abcdef",
  "title": "Explain photosynthesis in simple terms."
}
```

Each history item has a `role` of `user` or `assistant` and a `content` string. A message can contain up to 8,000 characters; up to 200 history messages can be stored/sent by the application. The most recent 20 history messages are sent to the model for context. The conversation ID is a 32-character GUID in `N` format.

Example successful chat response:

```json
{
  "answer": "Photosynthesis is how plants use sunlight to make food..."
}
```

API errors use HTTP status codes and, where applicable, a Problem Details JSON response. Common statuses include `400` for invalid input, `401` when the user needs to sign in, `429` when the model provider is rate-limiting requests, `502` when Hugging Face cannot serve the request, and `503` when Hugging Face credentials are missing or invalid.

## Project layout

```text
TalkGPT/
|-- Program.cs                         ASP.NET Core startup, auth, and API routes
|-- Services/
|   |-- AccountContracts.cs            Account and conversation data contracts
|   |-- AccountService.cs              Input checks and password hashing
|   |-- ChatContracts.cs                Chat API contracts and validation
|   |-- HuggingFaceChatService.cs       Hugging Face Inference Providers client
|   `-- SqliteChatRepository.cs         SQLite schema and persistence
|-- wwwroot/
|   |-- index.html                      Chat and sign-in/register interface
|   |-- styles.css                      Responsive application styling
|   `-- app.js                          Browser chat, auth, and voice behavior
|-- appsettings.json                    Non-secret model and logging settings
`-- README.md                           This guide
```

## Security and privacy

- The Hugging Face token is only read by the .NET backend and is never returned to the browser.
- Use a token with only the permissions needed for Inference Providers. Rotate it if it is exposed.
- Do not commit real tokens, production databases, or passwords.
- Authentication cookies are HTTP-only and `SameSite=Strict`. Host the production application over HTTPS.
- Account records and chats are stored on the server in SQLite. Hugging Face receives the submitted message and recent conversation context so it can generate a reply; do not send information you are not comfortable sharing with the configured model/provider.
- Browser speech input uses browser-provided speech recognition. Browser implementations can handle recognition differently; review your browser's privacy information.
- SQLite contains personal and conversation data. Protect the database and its backups with appropriate operating-system permissions and storage encryption.
- This starter application does not include email verification, account recovery, account deletion, per-user rate limiting, or multi-factor authentication. Add the controls appropriate to your deployment before exposing it as a public service.

## Deployment notes

- Publish and run the ASP.NET Core application with a supported .NET 8 runtime.
- Configure `HuggingFace__Token`, `HuggingFace__Model` (if changing the default), and a writable persistent `ConnectionStrings__TalkGPT` path through the hosting platform's secret/configuration store.
- Terminate HTTPS at the application or a correctly configured trusted reverse proxy. Do not expose account cookies over plain HTTP.
- Ensure the hosting process can create/access the SQLite directory, and persist and back up the database file.
- Keep data protection keys persistent and shared appropriately if deploying multiple application instances; otherwise, a restart or another instance may invalidate authentication cookies.
- SQLite is suitable for a small, single-instance deployment. Consider a managed relational database and additional operational protections for higher traffic or multiple instances.

## Troubleshooting

### "Hugging Face is not configured"

Set `HuggingFace__Token` in the server process environment and restart the application. Confirm it is a Hugging Face token with **Inference Providers** permission. Never paste the token into the browser console or an issue report.

### Hugging Face rejects the token or model

Check that the token has the required permission, the selected model is available through an Inference Provider for your account, and the account has not reached a provider limit. Change the model using `HuggingFace__Model` if needed.

### The chat list does not load after signing in

Confirm the SQLite directory is writable and that the same persistent database path is used after each restart/deployment. Check the server logs for database errors.

### Sign-in does not persist after a restart or across instances

ASP.NET Core protects authentication cookies using Data Protection keys. For multiple instances or durable deployments, configure a persistent, access-controlled Data Protection key store shared by the instances.

### The microphone button reports that voice input is unsupported

Use a current browser that supports the Web Speech API, such as a compatible Chromium-based browser. Grant microphone permission, choose the correct language, and use HTTPS when deployed. Voice recognition support varies by browser and operating system.

### The microphone works, but speech is not recognized

Check the selected voice language, microphone permission, operating-system input device, and network requirements of the browser's recognition service. Try a short phrase in a quiet environment.

### A model request times out or is rate limited

Retry after a short wait. Provider availability, queue time, and rate limits are controlled by Hugging Face and the selected model/provider.
