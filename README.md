# Zarus

Zarus is a Unity 6000.2.10f1 URP project that showcases an interactive South African provincial map with Input System–driven UI controls. The main experience lives in `Assets/Scenes/Main.unity`, with supporting menus wired through `UIManager`.

## Getting started

- **Unity**: open the project with `6000.2.10f1`. URP and the Input System are already enabled (see `Assets/InputSystem_Actions.inputactions`).
- **Core scenes**: Start → Main → End. Use `UIManager.ReturnToMenu()`, `.RestartGame()`, or `.ShowEndScreen()` to navigate at runtime; ESC opens the pause menu that already bridges to ReturnToMenu.
- **Work inside Assets/** to preserve GUIDs; avoid manual edits to `ProjectSettings/`, `Packages/`, or generated folders (`Library/`, `Temp/`, `Logs/`).
- **Compile cycle**: Unity builds the GIS assets the first time scripts compile. Re-run `Zarus/Map/Rebuild Region Assets` whenever the source GIS data (`Assets/Sprites/za.json`) changes.

## Project layout

- `Assets/Map/Meshes`: province meshes used by `RegionMapController`.
- `Assets/Resources/Map`: regenerated `RegionDatabase.asset` plus metadata per region.
- `Assets/Scripts/Map`: runtime map logic, including `RegionMapController`, `RegionMapCameraController`, and hover/select utilities.
- `Assets/UI`: layouts (`Layouts/`), styles (`Styles/`), and controllers (`Scripts/Core`).
- `Assets/Scenes`: Start, Main, End scenes that combine HUDs, map, and menus.
- `Assets/Settings` & `Sprites`: configuration and data sources (`za.json`).
- `Packages/`, `ProjectSettings/`, `UserSettings/`: Unity-managed; avoid manual edits unless absolutely necessary.

## Core systems

- **Region map overlay**: the interactive, colored provincial map is generated from `RegionDatabase.asset` and driven by `RegionMapController` + `RegionMapCameraController` inside `Assets/Scenes/Main.unity`. Material toggles use `MaterialPropertyBlock` for emission and hover states.
- **UI toolkit**: layouts live in `Assets/UI/Layouts`, USS in `Assets/UI/Styles`, and UXML controllers under `Assets/UI/Scripts`. Shared overlays like `SettingsPanelView` can be inserted via `SettingsPanelView.Create(hostElement, template)`, as shown in StartMenu and PauseMenu controllers.
- **GIS workflow**: data originates from SimpleMaps; the JSON source is `Assets/Sprites/za.json`. Artists can tweak colors/descriptions/links in `RegionDatabase.asset`.

## Development guidelines

- Prefer MCP-safe edits (`script_apply_edits`, `manage_asset`, `manage_prefabs`) so Unity keeps GUIDs and references intact.
- Read the console after changes; revalidate scripts with `validate_script` if needed and rerun affected tests (`run_tests` for Play/Edit Mode suites) when possible.
- Document new pipelines or workflows directly in `AGENTS.md` so future contributors onboard quickly.

## Licensing

- SimpleMaps South Africa dataset licensing details live in `Assets/Documentation/ThirdPartyLicenses/SimpleMaps_SouthAfrica.txt` and must ship with the build.

## Next steps

1. Open the project in Unity 6000.2.10f1 and verify the Main scene boots with the map overlay.
2. Regenerate GIS assets after editing `Assets/Sprites/za.json` via `Zarus/Map/Rebuild Region Assets`.
