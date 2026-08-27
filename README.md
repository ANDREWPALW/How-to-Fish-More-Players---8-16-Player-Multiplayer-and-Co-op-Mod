# How To Fish More Players — 16 Players

BepInEx 5 plugin for **How To Fish** that raises the game's multiplayer cap from the stock **8 players** to **16 players**.

## What is patched

The current game build contains two relevant hard-coded `8` values:

- `SteamManager.CreateLobby()` → Steam lobby capacity.
- `SteamManager.OnLobbyCreated(...)` → value passed to `ConnectionManager.CreateOnlineLobby(..., maxPlayers)`.

The plugin replaces those two specific literals with `16` and adds narrow runtime failsafes for:

- `ConnectionManager.CreateOnlineLobby`
- `Steamworks.SteamMatchmaking.CreateLobby`
- `Steamworks.SteamMatchmaking.SetLobbyMemberLimit`
- concrete FishySteamworks `SetMaximumClients` / `StartConnection` / `GetMaximumClients` methods, when present.

The mod deliberately does **not** patch arbitrary integer constants throughout the game.

## Requirements

- How To Fish (Unity Mono build)
- BepInEx 5.x, tested against the supplied **BepInEx 5.4.23.4** references
- All players should use the same game version. Installing the plugin on the host is essential; installing it on every participant is recommended.

## Installation

Copy:

`HowToFishMorePlayers.dll`

into:

`How To Fish/BepInEx/plugins/HowToFishMorePlayers/`

Then start the game and check `BepInEx/LogOutput.log`.

Expected startup lines include:

- `Loading [How To Fish More Players 2.1.0]`
- `SteamManager.CreateLobby(): hard-coded 8 -> 16.`
- `SteamManager.OnLobbyCreated(): hard-coded 8 -> 16.`
- `How To Fish More Players: installed ... Harmony patches.`

When a lobby is created, the transpilers will also report that the two player-limit literals were replaced.

## Build

The included GitHub Actions workflow compiles the plugin with Roslyn/MSBuild. This avoids the invalid CLR metadata problem in the previous hand-built DLL which caused Mono.Cecil `ReadMetadataStream` / `ArgumentOutOfRangeException` errors.
