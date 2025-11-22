# Agent Onboarding

Welcome to Zarus. This document orients automation agents and coders who touch the project so they can work efficiently without rummaging through the tree.

## Unity baseline

- **Engine**: Unity 6000.2.10f1 (match this editor version to avoid serialization churn).
- **Render pipeline**: URP 3D (see `Assets/Settings` and `UniversalRenderPipelineGlobalSettings.asset`).
  - Uses `ForwardRenderer.asset` for 3D rendering (orthographic camera, unlit materials).
  - Converted from 2D URP to 3D URP for desktop deployment.
- **Input**: Uses the Input System via `Assets/InputSystem_Actions.inputactions`.

## Repository structure

- `Assets/` — Primary gameplay content; holds art, prefabs, scenes, shaders, settings, and tutorial info. Keep everything user-facing inside this folder so the Unity asset database can track GUIDs correctly.
- `Packages/` — Unity package manifest and lock files (leave edits to the Unity Package Manager).
- `ProjectSettings/`, `UserSettings/` — Unity-generated settings; let the editor own them.
- `Library/`, `Logs/`, `Temp/` — Generated caches/logs; ignore unless debugging engine-level issues.

### Assets overview

- `Documentation/ThirdPartyLicenses/` — Attribution requirements (SimpleMaps/CC BY 4.0, etc.).
- `Map/Meshes/` — Generated meshes for each South African province (overwritten by the importer).
- `Materials/` — Shared material assets.
- `Prefabs/` — Prefabricated objects for reuse.
- `Resources/Map/` — RegionDatabase.asset and runtime-loaded map data.
- `Resources/UI/Cursors/` — Custom cursor textures.
- `Scenes/` — Unity scenes; `Main.unity` is the primary game scene.
- `Scripts/Map/` — Runtime map controllers + geo tooling. Prefab/scene hooks live here.
- `Scripts/Map/Editor/` — GeoJSON importer for rebuilding region assets from `za.json`.
- `Settings/` — URP pipeline, volume profiles, and other project-wide configurations.
- `Shaders/` — Custom shader graphs/shader code.
- `Sprites/` — 2D textures and sprite sheets. Contains `za.json` (South Africa GeoJSON data).
- `UI/` — UI Toolkit assets (UXML layouts, USS styles, C# controllers).

## Unity MCP server

The Unity MCP server provides tools to interact with Unity Editor while it's running. Unity must be open with the MCP for Unity package installed for these tools to work.

### Core principles

- **Always use MCP tools for Unity operations** — They maintain GUID integrity and avoid conflicts with the Unity asset database.
- **Unity must be running** — The MCP bridge only works when Unity Editor is open and the MCP for Unity package is active.
- **Assets go in `Assets/` only** — Unity only tracks files inside this directory; scripts/assets outside won't be recognized.
- **Don't touch generated folders** — `Library`, `Temp`, `Logs` are auto-generated; Unity will recreate them.

### Available MCP tools

#### Script management

- **`read_resource`** — Read C# script contents with optional line slicing
  - URI format: `unity://path/Assets/Scripts/MyScript.cs` or just `Assets/Scripts/MyScript.cs`
  - Supports `start_line` and `line_count` for partial reads
  - Always read before editing to understand context

- **`create_script`** — Create new C# scripts
  - Set `path` relative to Assets (e.g., `Assets/Scripts/Map/MyNewScript.cs`)
  - Provide full script `contents` (Base64 encoded over transport)
  - Optional `namespace` and `script_type` (MonoBehaviour, ScriptableObject, etc.)

- **`script_apply_edits`** — Structured edits to C# scripts (PREFERRED for modifications)
  - Operations: `replace_method`, `insert_method`, `delete_method`, `anchor_insert`, `anchor_replace`, `anchor_delete`
  - Safer than raw text edits; maintains balanced braces/namespaces
  - Example:
    ```json
    {
      "name": "MyScript",
      "path": "Assets/Scripts",
      "edits": [{
        "op": "replace_method",
        "className": "MyScript",
        "methodName": "Update",
        "replacement": "void Update() { Debug.Log(\"Hello\"); }"
      }]
    }
    ```

- **`apply_text_edits`** — Low-level character-range edits (use only when necessary)
  - Requires precise line/column positions (1-indexed)
  - Use `read_resource` with line numbers first to verify content
  - Higher risk of syntax errors

- **`validate_script`** — Check script for compilation errors
  - Returns diagnostics without modifying the file
  - Useful before/after edits

- **`delete_script`** — Remove a C# script by URI

#### GameObject & Scene operations

- **`manage_scene`** — Scene operations
  - Actions: `create`, `load`, `save`, `get_hierarchy`, `get_active`, `get_build_settings`
  - Scene hierarchy returns GameObject tree with instance IDs

- **`manage_gameobject`** — GameObject CRUD
  - Actions: `create`, `modify`, `delete`, `find`, `add_component`, `remove_component`, `set_component_property`, `get_components`
  - Find methods: `by_name`, `by_id`, `by_path`, `by_tag`, `by_layer`, `by_component`
  - Always use `get_components` to inspect before modifying

- **`manage_prefabs`** — Prefab operations
  - Actions: `create`, `modify`, `delete`, `get_components`
  - Prefab path must be under `Assets/`

#### Asset management

- **`manage_asset`** — Asset CRUD operations
  - Actions: `import`, `create`, `modify`, `delete`, `duplicate`, `move`, `rename`, `search`, `get_info`, `create_folder`, `get_components`
  - Specify `asset_type` for filtering
  - Search supports pagination with `page_number` and `page_size`

#### Editor operations

- **`manage_editor`** — Editor state control
  - Actions: `play`, `pause`, `stop`, `get_state`, `get_project_root`, `get_windows`, `get_active_tool`, `get_selection`
  - Tag/Layer management: `add_tag`, `remove_tag`, `get_tags`, `add_layer`, `remove_layer`, `get_layers`
  - Tool selection: `set_active_tool`

- **`read_console`** — Read Unity console logs
  - Actions: `get`, `clear`
  - Filter by type: `error`, `warning`, `log`
  - Use `count` parameter (quoted string, e.g., `"10"` for max compatibility)
  - Helpful for debugging compilation errors

- **`execute_menu_item`** — Trigger Unity menu commands
  - Example: `menu_path: "Assets/Reimport All"`

#### Testing

- **`run_tests`** — Execute Unity tests
  - Modes: `EditMode`, `PlayMode`
  - Returns test results and failures

### Common workflows

#### Fixing compilation errors

1. Use `read_console` to see error messages
2. Identify the files and line numbers
3. Use `read_resource` to inspect the problematic code
4. Fix with `script_apply_edits` for method changes or `apply_text_edits` for precise fixes
5. Check `read_console` again to verify

Example from this project:
- Error: Missing assembly reference for `UnityEngine.InputSystem`
- Fix: Modified `Assets/Scripts/Map/Zarus.Map.asmdef` to add `Unity.InputSystem` to references array

#### Modifying assembly definitions

Assembly definition files (.asmdef) are JSON; use standard file tools:
- `read_file` to inspect
- `replace_in_file` to modify
- Must include references to Unity packages (e.g., `Unity.InputSystem`, `Unity.Netcode.Runtime`)

#### Working with GameObjects

1. Use `manage_scene` with action `get_hierarchy` to see scene structure
2. Use `manage_gameobject` with `get_components` to inspect components
3. Add/remove components or modify properties as needed
4. Note: `set_component_property` uses JSON for the properties object

### Best practices

1. **Read before writing** — Always inspect files/components before modifying
2. **Use structured edits** — Prefer `script_apply_edits` over `apply_text_edits` for C# changes
3. **Check the console** — Use `read_console` to catch errors early
4. **Mind the GUIDs** — Never manually edit .meta files; use MCP tools to preserve Unity's asset database
5. **Test incrementally** — Make small changes and verify with console/tests
6. **URI formats** — Scripts accept `unity://path/Assets/...`, `file://...`, or just `Assets/...`

### Troubleshooting

**"No Unity Editor instances found"**
- Unity Editor is not running or MCP for Unity package is not installed/enabled
- Launch Unity and wait for the MCP bridge to start

**Component operations fail**
- Verify the GameObject exists with `manage_scene` → `get_hierarchy`
- Check exact component name with `get_components` first
- Some components can't be added via script (engine restrictions)

**Script edits don't apply**
- Check if file is open in another editor (lock conflicts)
- Verify path is under `Assets/` directory
- Use `validate_script` to check for syntax errors

**Assembly definition issues**
- Missing package references cause "type or namespace not found" errors
- Check `Packages/manifest.json` for installed packages
- Add references in .asmdef files (use standard file tools, not MCP script tools)

### Project-specific notes

- The GIS importer (`Zarus/Map/Rebuild Region Assets`) auto-generates `Assets/Resources/Map/RegionDatabase.asset` and meshes from `Assets/Sprites/za.json`. Run it after updating GIS data so the `Main.scene` overlay stays in sync. Never hand-edit generated meshes.
- Artists configure colors/descriptions/URLs inside `RegionDatabase.asset`. Runtime toggles exist on `RegionMapController` in `Main.scene`; keep those values artist-friendly.

## UI System (UI Toolkit)

The game uses Unity's UI Toolkit for all user interface elements. The UI is designed to be artist-friendly with centralized theming.

### Structure

- `Assets/UI/Styles/` — USS theme files (like CSS) for visual styling
- `Assets/UI/Layouts/` — UXML files (like HTML) defining UI structure
- `Assets/UI/Scripts/` — C# controllers for UI behavior
- `Assets/UI/Settings/` — PanelSettings and other UI configuration assets

### Key Systems

**UIManager** (`Assets/UI/Scripts/Core/UIManager.cs`)
- Singleton managing all UI screens
- Handles pause/resume with Input System integration
- Switches between Player and UI action maps automatically

**Screens**
- `PauseMenu` — ESC key toggles, allows resume/quit
- `GameHUD` — Non-diegetic overlay showing timer, province counter, selected province info
- Future: Province detail cards (diegetic), settings screen

**Theme System**
- `MainTheme.uss` contains CSS variables for colors, spacing, fonts
- Artists can modify these variables to restyle the entire UI
- No code changes needed for visual updates

### Adding New UI Screens

1. Create UXML layout in `Assets/UI/Layouts/Screens/`
2. Create C# controller inheriting from `UIScreen` in `Assets/UI/Scripts/Screens/`
3. Apply `MainTheme.uss` stylesheet
4. Register with UIManager if needed

### MCP Integration

Use MCP tools for UI work:
- `manage_asset` for creating UXML/USS files
- `create_script` for new UI controllers
- `manage_gameobject` to add UIDocument components to scene

**Important:** UIDocument components must reference both UXML and USS files. PanelSettings define rendering mode (Screen Space Overlay for HUD, World Space for diegetic UI).

### Input System

Pause is handled via "UI/Cancel" action (ESC key). UIManager automatically:
- Disables "Player" action map when paused
- Enables "UI" action map for menu navigation
- Reverses on resume

Keep this document short and update it whenever new workflows or directories appear so future agents can ramp up quickly.
