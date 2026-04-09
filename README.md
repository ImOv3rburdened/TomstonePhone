# TomestonePhone

TomestonePhone is a self-contained local project with a Dalamud plugin, shared contracts, and a deployable companion server. It provides a modern phone-style interface for direct messages, group chat, calls, contacts, friend requests, support, legal policy review, and staff moderation.

## Projects

- `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone`
- `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Shared`
- `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server`

## Commands

- `/tomestone`
- `/ts`

## Server

The backend is ready for local hosting, Docker, or Coolify deployment. It stores state in MariaDB, serves a lightweight browser portal for account access and policy review, and can bootstrap the first owner account from configuration.

## Local Layout

- Solution: `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.sln`
- Plugin: `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone\TomestonePhone.csproj`
- Shared: `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Shared\TomestonePhone.Shared.csproj`
- Server: `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server\TomestonePhone.Server.csproj`

## Current Scope

- Dalamud plugin entrypoint with `/tomestone` and `/ts`
- Portrait phone window with locked aspect ratio and contained ImGui styling
- Home screen with Messages, Calls, Contacts, Friends, Settings, Legal, Privacy, Support, and Staff apps
- Direct messaging to any username or phone number
- Group conversations with ownership and moderator controls
- Call history, incoming call banner, active call controls, missed call counts, and voice-session metadata for Murmur-style voice rooms
- Contacts, bilateral friendships, pending friend requests, blocking, and unblock controls
- Local machine acceptance flow for Terms of Service and Privacy Policy
- Always-accessible Legal and Privacy screens in both the plugin and the browser portal
- Support tickets for normal support and moderation cases
- Staff dashboard for accounts, reports, audit logs, and owner password reset
- Persistent account, message, moderation, and IP logging
- IP bans and local plugin lockout after banned-account or banned-IP enforcement
- User-hosted chat image uploads disabled
- External image and GIF links allowed as message embeds
- Dockerfile, Compose file, and Coolify deployment files


## Voice Roadmap

TomestonePhone now includes a server-side voice-session scaffold aimed at a low-bandwidth Murmur or Mumble deployment.

Current state:
- The server can attach voice-room metadata to calls.
- The plugin can surface the configured voice profile in the Calls UI.
- Real microphone capture and playback are not wired yet.

Voice config lives in [appsettings.json](E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server\appsettings.json):

```json
"Voice": {
  "Enabled": false,
  "Provider": "Murmur",
  "Host": "",
  "TcpPort": 64738,
  "UdpPort": 64738,
  "QualityLabel": "Aether Voice (Low Bandwidth)",
  "SampleRateHz": 16000,
  "BitrateKbps": 16,
  "FrameSizeMs": 20
}
```

A Murmur template file is included here:
- `E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server\murmur.ini.example`
## Runtime Files

- Web portal: `http://localhost:5050/`
- Health endpoint: `http://localhost:5050/health`
- Bundled SVG assets: `E:\Github\TomstonePhone\images`

## MariaDB

Configure the database connection in [appsettings.json](E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server\appsettings.json):

```json
"MariaDb": {
  "Server": "127.0.0.1",
  "Port": 3306,
  "Database": "TomestonePhone",
  "Username": "TomestonePhone",
  "Password": "8buS~kuw6rHd",
  "SslMode": "None"
}
```

The server creates its required table automatically on first start. All application state is stored in MariaDB instead of local JSON files.

## Bootstrap Owner

Set the first owner account in [appsettings.json](E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server\appsettings.json) before first launch:

```json
"BootstrapOwner": {
  "Username": "youradmin",
  "Password": "set-a-strong-password",
  "DisplayName": "Your Admin Name",
  "CharacterName": "Your Character",
  "WorldName": "Your World"
}
```

The bootstrap owner is only created when the MariaDB app-state row does not exist yet.

## Legal Terms

TomestonePhone includes a machine-local first-launch agreement in the plugin and required acceptance tracking during account registration. This improves notice and consent tracking. It does not create complete legal immunity for the operator.

## Privacy Policy

TomestonePhone includes a separate privacy-policy acceptance step and reread surface. The policy explains collection and use of usernames, password hashes, assigned phone numbers, message data, support tickets, moderation records, audit logs, and IP addresses for service operation, security, abuse prevention, bans, moderation, support handling, and legal compliance.

## Cloudflare Moderation

The server is prepared for Cloudflare moderation via `CloudflareModeration:AlertSharedSecret` in [appsettings.json](E:\Github\TomstonePhone\TomestonePhone\TomestonePhone.Server\appsettings.json). If you later place hosted public assets behind Cloudflare, you can relay a confirmed alert into `POST /api/moderation/cloudflare/csam-alert` with header `X-Tomestone-Moderation-Secret`.

Expected alert payload:

```json
{
  "accountId": "00000000-0000-0000-0000-000000000000",
  "contentUrl": "https://your-domain/path/to/asset",
  "reportedIpAddress": "203.0.113.10",
  "reason": "Cloudflare CSAM match notification",
  "sourceReference": "cloudflare-alert-id"
}
```

When received, the backend suspends the account, records an audit log, opens a moderation support ticket, and IP-bans the reported IP when one is provided.



