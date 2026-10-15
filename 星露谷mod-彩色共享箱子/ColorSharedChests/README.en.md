# Color Shared Chests

[中文说明](README.md) · Version 1.0.5 · Experimental

A Stardew Valley SMAPI mod for per-chest names, extra storage rows, and save-wide shared inventories based on vanilla chest dye colors.

## Status and requirements

Compiled against Stardew Valley **1.6.15** and **SMAPI 4.5.2** on Windows. This is an experimental release, not a guarantee of compatibility with every mod. Back up your save before installing. Multiplayer concurrency, Automate throughput, overflow recovery, and the latest scrolling/UI changes still need in-game acceptance testing.

Generic Mod Config Menu is optional but recommended for settings. Chests Anywhere and Automate integrations are implemented, not comprehensively verified. Every multiplayer participant must install the same version; the host controls global rules.

## Installation

1. Install SMAPI. Exit the game and back up the save.
2. Extract a compiled package into `Stardew Valley/Mods/ColorSharedChests` so that `manifest.json` and `ColorSharedChests.dll` are in that folder.
3. Start through SMAPI. Open Generic Mod Config Menu, then Color Shared Chests. Without GMCM, edit the generated `config.json` while the game is closed.

Source downloads alone are not compiled installation packages. See Building below.

## Features and controls

- Supported: wooden chest, stone chest, big chest, and big stone chest. Fridges, gift boxes and special/modded chest types are not included.
- Same dyed color shares inventory across maps within the same save. Capacity is the largest single member's capacity, **not the sum**.
- Independent switches for capacity changes and sharing. Capacity changes default off; sharing defaults on.
- Extra rows: 0–10, with **12 slots per extra row**. The same increment applies to all supported types. Base capacities remain type-specific.
- Undyed chests are independent by default; all 20 dyed colors share. Each settings row has a swatch and checkbox.
- Click Rename beside the chest name bar; Ctrl+N is a fallback. Empty input removes the custom name. Names belong to individual chests, not groups.
- Hover chest slots and scroll to move one row at a time. Scrolling is blocked while holding an item.
- Close the menu and Ctrl+right-click a nearby chest to pick it up. If other group members remain, items stay in the shared group; the last chest takes its inventory with it.
- Changing dye leaves the old group's items behind. If no chest remains in that color, the inventory is reserved until a chest rejoins, or the color/sharing is disabled.
- Joins merge existing items. Overflow from merging or reducing capacity goes to the acting player's backpack, then drops near the operation chest. Global changes use the group's oldest remaining anchor. Settings migration waits until chest menus are closed.
- UI follows game language: Chinese for Chinese; English otherwise. Custom names are not translated.

## Configuration

| Key | Default | Meaning |
|---|---|---|
| EnableCapacity | false | Add ExtraRows to base capacity |
| ExtraRows | 0 | Additional rows, clamped to 0–10 |
| EnableSharing | true | Enable color-based sharing |
| SharedColors | 1–20 | Vanilla palette selections; 0 means undyed |

## Safe uninstall

Do **not** simply delete the mod while items remain shared. As host, load the save, close chest menus, then enter `colorchests_export` in the SMAPI console. Wait, inspect exported chests and dropped items, then save and exit before removing the mod. `colorchests_status` lists inventories/settings. If a reserved inventory has no chest access, place a regular chest and export again. Keep a backup until item counts are confirmed.

## Building

Install a .NET SDK that can target .NET 6 and have the game plus SMAPI installed locally:

```powershell
dotnet build ColorSharedChests.csproj -c Release -p:GamePath="D:/Steam/steamapps/common/Stardew Valley"
```

Create a package folder named `ColorSharedChests` with the resulting DLL, `manifest.json`, default `config.json`, README files, and LICENSE. Do not bundle game DLLs, Harmony/SMAPI DLLs, saves, logs, or personal configuration.

The SDK project is provided for portable builds; the current binary was compiled using a local Roslyn build helper, and the SDK build itself has not yet been run in this environment.

## Reporting bugs

Include game/SMAPI/mod versions, host or farmhand, exact reproduction steps, expected versus actual results, related chest mods, and the SMAPI error stack. Remove invite codes and personal information before posting logs publicly. For inventory issues, preserve the original save and avoid overwriting your only backup.

## License

The project is intended for MIT licensing; see LICENSE once the owner confirms the license for publication. Stardew Valley, SMAPI and other mods remain their respective authors' property. No game assets are distributed.
