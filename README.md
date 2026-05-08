# TomestonePhone

TomestonePhone is a self-contained local project with a Dalamud plugin, shared contracts, and a deployable companion server. It provides a modern phone-style interface for direct messages, group chat, calls, contacts, friend requests, support, and staff moderation.


## Latest Update

- Removed ip for conenction and now requires a domain with dns and ssl.
- Finalized switch from murmur/mumble to Opus PCM over websocket and removed old references.
- Replaced applicable ImGui with scoped ImRaii and Dalamud windowing for stability.
- Fixed a bug where opening and closing the window too quickly in succession would cause the game to lock for a moment.
- Fixed an issue where sometimes the button to create a ticket wouldn't appear.
- Removed the ability to search friends/contacts by in-game name and world.
- Made the ability to show your in-game name and world as your contact an opt-in feature which is now disabled by default.
- Revised privacy/use policy.


## Known Bugs

- Sometimes the privacy policy will pop up again on login in game
- Sometiems notifications do not appear on screen
- Graceful recconect will on occasion, after the end user's internet is lost and returns, fail. This will require you to press the refresh button on the top right of the phone to reconnect.


## License

This repository, including:
- TomestonePhone Plugin
- Shared Libraries
- Server Components

is licensed under the Mozilla Public License Version 2.0 (MPL-2.0).

The source code is intended to remain open and available for inspection,
modification, and self-hosting in accordance with Dalamud plugin
guidelines.

Commercial hosting, support, and maintenance are permitted under the
terms of the MPL-2.0 license; however, modifications to MPL-covered
source files must also remain available under the same license.

https://www.mozilla.org/en-US/MPL/2.0/
