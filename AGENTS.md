# Agent Onboarding

This repo targets Unity **6000.2.10f1**, URP 3D, Input System (see `Assets/InputSystem_Actions.inputactions`). Work inside `Assets/` so Unity tracks GUIDs.

## Repo map
- `Assets/` — gameplay, UI, shaders, scenes. Key folders: `Map/`, `Resources/Map`, `UI/Layouts`, `UI/Scripts`, `Shaders/`, `Settings/`, `Sprites/` (includes `za.json`).
- `Packages/`, `ProjectSettings/`, `UserSettings/` — Unity-managed; avoid manual edits.
- `Library/`, `Temp/`, `Logs/` — generated caches; keep out of source control.

## MCP workflow (Unity must be running)
1. **Inspect before edit**: `read_resource` for scripts, `manage_scene`/`manage_gameobject` for hierarchy info.
2. **Modify safely**: prefer `script_apply_edits` for C#, `manage_asset` for UXML/USS, `manage_prefabs` for prefab work. Use `apply_text_edits` only for precise tweaks.
3. **Validate**: run `validate_script` or check `read_console` after changes. Use `run_tests` for Edit/Play Mode suites when needed.
4. **Respect Assets**: never hand-edit `.meta`, keep assets in `Assets/`, and rely on MCP tools to preserve GUIDs.

## Common tasks
- **Fix compile errors**: read console → inspect file → edit via MCP → re-check console.
- **Assembly definitions**: edit `.asmdef` JSON with normal file tools; add Unity package references explicitly.
- **GameObjects**: `manage_scene get_hierarchy` to locate objects, `manage_gameobject` to add/remove components, `set_component_property` for serialized values.
- **UI Toolkit**: UXML in `Assets/UI/Layouts`, USS in `Assets/UI/Styles`, controllers under `Assets/UI/Scripts`. UIDocuments must reference layouts + PanelSettings.

## Best practices
- Keep edits incremental; test in small slices.
- Use Input System-friendly APIs (already enabled project-wide).
- Avoid touching generated folders or auto assets (meshes under `Map/Meshes`, etc.).
- Document new workflows directly in this file to keep onboarding tight.

## Commit conventions
- Format: `type: summary`. Common types: `feat`, `fix`, `docs`, `chore`, `tool`, `refactor`, `test`, `style`.
- One logical change per commit; reference assets/scripts only when helpful.

## UI & map quick notes
- `UIManager` (Assets/UI/Scripts/Core) swaps Player/UI action maps, handles pause (ESC via "UI/Cancel").
- `GameHUD` shows timer, provinces visited, province details; hook new HUD data through this class.
- `RegionMapController` manages runtime meshes, hover/select events, and color/emission via `MaterialPropertyBlock` (useful for night-light effects).
- GIS importer (`Zarus/Map/Rebuild Region Assets`) rebuilds `RegionDatabase.asset` + meshes from `Assets/Sprites/za.json`—run it after GIS changes rather than editing generated assets.
