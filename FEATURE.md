# Cure Outposts & Global Cure Meter – Implementation Plan

This document outlines a step‑by‑step, agent‑oriented plan for adding buildable Cure Outposts, infection pushback, and a global cure progress meter to the `Main` scene. The plan assumes the current setup:

- `Main.unity` hosts `SARegionMap` (`RegionMapController` + `RegionContainer` children) and `UIManager` with `GameHUD` and `PauseMenu`.
- `RegionDatabase` / `RegionEntry` provide province meshes and metadata.
- `DayNightCycleController` drives an in‑game clock and notifies listeners via `TimeUpdated`.
- `GameHUD` is a UI Toolkit screen bound to `GameHUD.uxml` and already:
  - Shows a global timer based on `DayNightCycleController`.
  - Tracks hovered/selected provinces via `RegionMapController.OnRegionHovered/OnRegionSelected`.

The goal is to keep behaviour simple, data‑driven where possible, and minimize scene surgery (most work stays in `Assets/Scripts` and `Assets/UI`).

---

## High‑Level Design (updated with answers)

- **Per‑province state**: Track infection %, outpost count, and outpost status (active/disabled) per `RegionEntry.RegionId`.
- **Outpost building & economy**:
  - Player selects a province via the existing map interaction, then uses HUD controls to build outposts in that province.
  - You can build **multiple outposts per province**; each adds local curing and global research power with **diminishing global returns**.
  - Outposts cost a central currency denominated in South African Rand (`R`). This same ZAR budget will later fund upgrades and other systems.
  - Outposts cannot be built in fully infected provinces.
  - Costs scale upward with each additional outpost built globally.
- **Local vs global curing**:
  - Each outpost applies a **local cure rate** in its province (e.g. 2% per in‑game hour).
  - Local curing **stacks linearly** with the number of outposts in that province: 3 outposts → 3× local cure rate.
  - All **active** outposts contribute to a **global cure research speed**, filling a nationwide cure meter.
  - Global contribution has **diminishing returns**: Nth outpost is multiplied by a factor sequence (1.0, 0.9, 0.9², …).
  - “Urban hub” provinces (Gauteng, Western Cape, KwaZulu‑Natal) provide a **modest bonus** to their outposts’ global contribution.
- **Virus pushback**:
  - Infection in every province grows each in‑game hour, scaled by a virus strength factor that increases day by day.
  - If infection in a province reaches a high threshold (e.g. 80%), the outposts there become **disabled** and stop contributing locally and globally until infection drops again below the threshold.
  - A province is considered “fully infected” once it crosses a near‑1.0 threshold (e.g. 99%).
- **Win / loss & pacing**:
  - **Win**: Global cure progress reaches 100% → go to `End` scene with a **Victory** state.
  - **Loss**: All provinces reach fully infected threshold → go to `End` scene with a **Defeat** state.
  - Tuning target: a typical successful run should span **10+ in‑game days** and feel challenging, not trivial. With the existing `DayNightCycleController.timeScale`, we’ll tune infection/cure rates and, if needed, adjust `timeScale` so the average session lands around **15–20 minutes** of real time.

Time progression uses `DayNightCycleController.TimeUpdated` as the single source of truth. Simulation math is deterministic and side‑effect free; MonoBehaviours mostly wire inputs/outputs and trigger scene transitions.

---

## Commit‑Ordered Plan

Below is an ordered list of commits. Each commit is intentionally small and focused so multiple agents can work safely in parallel while preserving a clean history.

### 1) `feat: add outbreak core data model` — ✅ Complete

**Goal:** Introduce plain C# types for infection, outposts, and economy state without touching scenes or UI.

- **New file**: `Assets/Scripts/Systems/OutbreakState.cs`
  - Namespace: `Zarus.Systems`.
  - Types:
    - `ProvinceInfectionState`
      - `string RegionId`
      - `float Infection01` (0 = clean, 1 = fully infected)
      - `int OutpostCount` (0 = none, 1+ = number of outposts in this province)
      - `bool OutpostDisabled` (true if infection above disable threshold)
      - `bool HasOutpost => OutpostCount > 0`
      - `bool IsFullyInfected` (optional convenience flag; maintained by the simulation based on its configured fully‑infected threshold)
    - `GlobalCureState`
      - `float CureProgress01` (0–1)
      - `int ActiveOutpostCount`
      - `int TotalOutpostCount`
      - `int ZarBalance` (current central currency amount, shared for outposts and future upgrades)
    - `OutpostCostConfig`
      - `int BaseCostR` (base cost in R, e.g. R20)
      - `int CostPerExistingOutpostR` (increment per outpost already built)
    - `OutpostRateConfig`
      - `float LocalCurePerHour` (per outpost, e.g. `0.02f` = 2% per in‑game hour)
      - `float GlobalCurePerHourPerOutpost`
      - `float DiminishingReturnFactor` (e.g. `0.9f` for 100%, 90%, 81%, …)
      - `float TargetWinDayMin` / `float TargetWinDayMax` (for reference tuning, e.g. 10–15 days)
    - `VirusRateConfig`
      - `float BaseInfectionPerHour`
      - `float DailyVirusGrowth` (multiplier per in‑game day to ramp tension)
      - `float OutpostDisableThreshold01` (e.g. `0.8f`)
      - `float FullyInfectedThreshold01` (e.g. `0.99f`)
  - Static helpers:
    - `int ComputeOutpostCostR(int existingOutpostCount, OutpostCostConfig config)`
    - `float ComputeGlobalOutpostMultiplierForIndex(int index, float factor)` (index 0 → 1.0, 1 → factor, 2 → factor², …).
  - These are pure data/utility types (no `MonoBehaviour`, no Unity API) to keep them unit‑test‑friendly.

**Agent notes:**
- Use Unity MCP tooling to create the script under `Assets/Scripts/Systems` so Unity tracks it correctly.
- No references from existing systems yet; build should remain unchanged.

---

### 2) `feat: implement province infection and outpost simulation` — ✅ Complete

**Goal:** Add a simulation controller that uses the data model and ticks infection/cure based on in‑game time, but still no UI or input integration.

- **New file**: `Assets/Scripts/Systems/OutbreakSimulationController.cs`
  - Namespace: `Zarus.Systems`.
  - `OutbreakSimulationController : MonoBehaviour`
    - Serialized references:
      - `RegionMapController mapController;`
      - `DayNightCycleController dayNightController;`
    - Serialized config:
      - `OutpostRateConfig outpostRates;`
      - `VirusRateConfig virusRates;`
      - `OutpostCostConfig costConfig;`
      - `string[] urbanHubRegionIds;` (Gauteng, Western Cape, KwaZulu‑Natal, e.g. IDs `ZAGP`, `ZAWC`, `ZAKZN`).
      - `float urbanHubBonusMultiplier;` (modest, e.g. `1.25f`).
    - Economy settings:
      - `int startingZarBalance = 200;`
    - Runtime fields:
      - `Dictionary<string, ProvinceInfectionState> provinces;`
      - `GlobalCureState globalState;`
      - `InGameTimeSnapshot? lastSnapshot;`
      - `float virusStrengthFactor;` (derived from day index).
    - Unity lifecycle:
      - `Awake`: Ensure references (`FindFirstObjectByType<RegionMapController>` / `DayNightCycleController>` as fallback).
      - `OnEnable` / `OnDisable`: subscribe/unsubscribe to `dayNightController.TimeUpdated` (the single driver for simulation).
    - Core methods:
      - `InitializeFromMap()`
        - Iterate `mapController.Entries` to create `ProvinceInfectionState` per `RegionId`.
        - Start with a configurable infection seed (e.g. random between 5–20% or a fixed value).
        - Initialize `OutpostCount = 0`, `OutpostDisabled = false`.
      - `OnTimeUpdated(InGameTimeSnapshot snapshot)`
        - Compute `deltaMinutes` since `lastSnapshot` (handle day rollover).
        - Convert to `deltaHours = deltaMinutes / 60f`.
        - Call `SimulateStep(deltaHours, snapshot.DayIndex)`.
      - `SimulateStep(float deltaHours, int dayIndex)`
        - Compute `virusStrengthFactor = 1f + virusRates.DailyVirusGrowth * (dayIndex - 1);`.
        - For each province:
          - Infection growth:
            - `infectionIncrease = virusRates.BaseInfectionPerHour * virusStrengthFactor * deltaHours;`
          - Local cure:
            - If `state.OutpostCount > 0 && !state.OutpostDisabled`, `localCure = outpostRates.LocalCurePerHour * state.OutpostCount * deltaHours;` else `0`.
          - Apply net:
            - `state.Infection01 = Mathf.Clamp01(state.Infection01 + infectionIncrease - localCure);`
          - Outpost disable/enable:
            - If `state.OutpostCount > 0 && state.Infection01 >= virusRates.OutpostDisableThreshold01` → `OutpostDisabled = true`.
            - If `state.OutpostDisabled && state.Infection01 < virusRates.OutpostDisableThreshold01` → re‑enable (`OutpostDisabled = false`).
        - Global cure:
          - Build a list of all **active** outposts (expand each province’s `OutpostCount` into entries so each outpost has a global index).
          - For each outpost index `i` (0‑based), compute multiplier via `ComputeGlobalOutpostMultiplierForIndex(i, outpostRates.DiminishingReturnFactor)`.
          - If its province is in `urbanHubRegionIds`, multiply by `urbanHubBonusMultiplier` (e.g. 1.25).
          - Sum all multipliers into `effectiveOutpostFactor`.
          - `globalState.CureProgress01 += outpostRates.GlobalCurePerHourPerOutpost * effectiveOutpostFactor * deltaHours;`
          - Clamp `CureProgress01` to `[0, 1]`.
        - Update counts:
          - `globalState.TotalOutpostCount` = sum of all `OutpostCount`.
          - `globalState.ActiveOutpostCount` = number of outposts in provinces where `OutpostDisabled == false`.
        - Detect end conditions:
          - `allFullyInfected`: every province where `Infection01 >= virusRates.FullyInfectedThreshold01`.
          - `cureComplete`: `globalState.CureProgress01 >= 1f`.
    - Events (UnityEvents + C# events):
      - `UnityEvent<ProvinceInfectionState> OnProvinceStateChanged;`
      - `UnityEvent<GlobalCureState> OnGlobalStateChanged;`
      - `UnityEvent OnAllProvincesFullyInfected;`
      - `UnityEvent OnCureCompleted;`

**Pacing note:** Use `TargetWinDayMin/Max` in `OutpostRateConfig` as a guideline when tuning `BaseInfectionPerHour`, `DailyVirusGrowth`, and `GlobalCurePerHourPerOutpost` so that with reasonable play the cure completes after roughly 10+ in‑game days. Final session duration in minutes can be tuned via `DayNightCycleController.timeScale`.

**Agent notes:**
- Keep this controller self‑contained; do **not** reference UI yet.
- Use serialized config defaults that roughly match the desired challenge level; leave exact numbers to playtesting.

---

### 3) `feat: add outpost building API and ZAR cost scaling` — ✅ Complete

**Goal:** Add methods to build outposts with rules (no building in fully infected provinces, cost scaling, shared ZAR budget) and keep it UI‑agnostic but ready for HUD integration.

- **Modify**: `OutbreakSimulationController`
  - Add enum:
    - `OutpostBuildError { None, InvalidRegion, ProvinceFullyInfected, NotEnoughZar }`
  - Initialization:
    - In `Awake` / `Start`, set `globalState.ZarBalance = startingZarBalance` (defined in the previous commit).
  - Public API:
    - `bool CanBuildOutpost(string regionId, out int costR, out OutpostBuildError error);`
      - Valid only if:
        - Region exists in `provinces`.
        - Province is **not** fully infected (`Infection01 < virusRates.FullyInfectedThreshold01`).
        - `globalState.ZarBalance >= costR`, where `costR` is computed via `ComputeOutpostCostR(globalState.TotalOutpostCount, costConfig)`.
      - Multiple outposts per province are allowed; there is **no** “already has outpost” error.
    - `bool TryBuildOutpost(string regionId, out int costR, out OutpostBuildError error);`
      - Uses `CanBuildOutpost`.
      - On success:
        - Deduct `costR` from `globalState.ZarBalance`.
        - Increment `state.OutpostCount` for that province.
        - Ensure `state.OutpostDisabled = false` (fresh outpost starts active if below disable threshold).
        - Update `globalState.TotalOutpostCount`.
        - Raise `OnProvinceStateChanged` and `OnGlobalStateChanged`.
  - Behaviour:
    - Outposts in a disabled province remain present (count does not drop) but inactive; when infection falls below disable threshold, they automatically become active again.

**Agent notes:**
- Keep log messages minimal but helpful (guarded by `bool debugLogging`).
- This API is what the HUD and any future upgrade system will call; keep signatures stable.

---

### 4) `feat: extend HUD layout for cure meter, ZAR and outpost controls` — ✅ Complete

**Goal:** Update the gameplay HUD layout to surface global cure progress, ZAR budget, and province‑specific outpost info, without wiring logic yet.

- **Modify UXML**: `Assets/UI/Layouts/Screens/GameHUD.uxml`
  - Under `TopBar > LeftStats`, add:
    - A **global cure meter** container:
      - `VisualElement` name: `CureProgressStat`, class: `hud-stat hud-stat--vertical hud-cure-progress`.
      - Children:
        - `Label` name `CureProgressLabel`, text “CURE PROGRESS”.
        - `ProgressBar` name `CureProgressBar` (always visible, initial value 0).
        - `Label` name `CureProgressDetailsLabel` for small text like “Racing outbreak – 0 outposts”.
    - A **ZAR + outpost summary**:
      - `VisualElement` name: `OutpostSummaryStat`, class: `hud-stat hud-outpost-summary`.
      - Children:
        - `Label` name `OutpostCountLabel` (e.g. “Outposts: 0 active / 0 total”).
        - `Label` name `ZarBalanceLabel` (e.g. “Budget: R 200”).
  - Under `ProvinceInfo` (or just below it), add a **build outpost** control area:
    - `VisualElement` name `OutpostActions`, class `hud-province-actions`.
    - Children:
      - `Label` name `OutpostStatusLabel` (e.g. “No outposts here”, “3 outposts ACTIVE”, “Outposts DISABLED at 83% infection”).
      - `Label` name `ProvinceInfectionLabel` (e.g. “Infection: 42%”).
      - `Button` name `BuildOutpostButton`, text “Deploy Cure Outpost”.
      - `Label` name `BuildOutpostCostLabel` (e.g. “Cost: R 30”).
- **Modify USS**: `Assets/UI/Styles/MainTheme.uss`
  - Add styles for:
    - `.hud-cure-progress` (layout for the progress section).
    - `.hud-outpost-summary` and `.hud-province-actions`.
    - Status color cues:
      - `.hud-outpost-status--none`
      - `.hud-outpost-status--active`
      - `.hud-outpost-status--disabled`

**Agent notes:**
- Keep layout changes additive; avoid renaming existing element IDs used by `GameHUD.cs`.
- The cure progress bar should be **always visible** from the start of the game.

---

### 5) `feat: bind HUD to outbreak simulation` — ✅ Complete

**Goal:** Wire `GameHUD` to the simulation so the HUD reflects infection / cure state and exposes a clean API for interaction logic, including ZAR costs.

- **Modify**: `Assets/UI/Scripts/Screens/GameHUD.cs`
  - Serialized reference:
    - `OutbreakSimulationController outbreakSimulation;`
  - Cached UI Toolkit elements (queried during initialization):
    - `ProgressBar cureProgressBar;`
    - `Label cureProgressDetailsLabel;`
    - `Label outpostCountLabel;`
    - `Label zarBalanceLabel;`
    - `VisualElement outpostActions;`
    - `Label outpostStatusLabel;`
    - `Label provinceInfectionLabel;`
    - `Button buildOutpostButton;`
    - `Label buildOutpostCostLabel;`
  - Runtime fields:
    - `RegionEntry selectedRegion;` (already in use; ensure we store it).
    - Helper: `string SelectedRegionId => selectedRegion?.RegionId;`
  - Initialization:
    - Resolve `outbreakSimulation` via serialized field or `FindFirstObjectByType<OutbreakSimulationController>()`.
    - Subscribe to:
      - `outbreakSimulation.OnGlobalStateChanged` → `HandleGlobalStateChanged`.
      - `outbreakSimulation.OnProvinceStateChanged` → `HandleProvinceStateChanged`.
    - Hook `buildOutpostButton.clicked` to `OnBuildOutpostClicked`.
  - Event handlers:
    - `HandleGlobalStateChanged(GlobalCureState state)`
      - Update `cureProgressBar.value` and `cureProgressBar.title` (or label text) to something like “42%”.
      - Update `cureProgressDetailsLabel` with a short status: e.g. `string.Format("Outposts: {0} active / {1} total", state.ActiveOutpostCount, state.TotalOutpostCount)`.
      - Update `zarBalanceLabel` to `string.Format("Budget: R {0}", state.ZarBalance)`.
      - Update `outpostCountLabel` with active/total counts.
    - `HandleProvinceStateChanged(ProvinceInfectionState province)`
      - If `province.RegionId != SelectedRegionId`, ignore.
      - Update `provinceInfectionLabel` with the current infection percentage.
      - Update `outpostStatusLabel` and CSS classes:
        - No outposts → “No outposts here” + `.hud-outpost-status--none`.
        - Outposts active → “X outposts ACTIVE” + `.hud-outpost-status--active`.
        - Outposts disabled → “X outposts DISABLED at YY% infection” + `.hud-outpost-status--disabled`.
    - `OnProvinceSelected(RegionEntry region)` (existing method):
      - Keep current behaviour (visited provinces, name/description).
      - Store `selectedRegion = region`.
      - Pull latest `ProvinceInfectionState` from `outbreakSimulation` via a helper like `TryGetProvinceState`.
      - Call `HandleProvinceStateChanged`‑like logic to refresh outpost and infection UI immediately.
    - `OnBuildOutpostClicked()`
      - Early return if `outbreakSimulation == null` or `selectedRegion == null`.
      - Call `outbreakSimulation.CanBuildOutpost(selectedRegion.RegionId, out var costR, out var error)`.
      - Update `buildOutpostCostLabel` with “Cost: R XX”.
      - If `error == OutpostBuildError.None`, call `TryBuildOutpost` and rely on events to update HUD.
      - If `NotEnoughZar`, set `outpostStatusLabel` to something like “Not enough budget (R XX needed)” and keep disabled CSS state.
      - If `ProvinceFullyInfected`, show “Province fully infected – cannot deploy”.
  - Cleanup:
    - On `OnDestroy`, unsubscribe from simulation events and button click.

**Agent notes:**
- Keep `GameHUD` as a presenter; infection math and cost calculation stay inside `OutbreakSimulationController` / `OutbreakState`.
- Mirror the subscription/unsubscription patterns already used for `DayNightCycleController` time updates.

---

### 6) `feat: add win and loss flow with distinct end screen` — ✅ Complete

**Goal:** Trigger scene transitions when the player wins or loses and display a distinct Victory/Defeat view with key stats.

- **New file**: `Assets/Scripts/Systems/GameOutcomeState.cs`
  - Namespace: `Zarus.Systems`.
  - `enum GameOutcomeKind { None, Victory, Defeat }`
  - `static class GameOutcomeState`
    - Static properties:
      - `GameOutcomeKind LastOutcome { get; private set; }`
      - `float LastCureProgress01 { get; private set; }`
      - `int LastTotalOutposts { get; private set; }`
      - `int LastActiveOutposts { get; private set; }`
      - `int LastZarBalance { get; private set; }`
      - `int LastDayIndex { get; private set; }`
      - `int LastSavedProvinces { get; private set; }` (provinces below fully infected threshold)
      - `int LastFullyInfectedProvinces { get; private set; }`
    - Method:
      - `SetOutcome(GameOutcomeKind outcome, GlobalCureState globalState, int dayIndex, int savedProvinces, int fullyInfectedProvinces)`
        - Sets the static fields; called right before switching to `End` scene.
- **Modify**: `OutbreakSimulationController`
  - Serialized reference:
    - `UIManager uiManager;` (optional; fallback via `FindFirstObjectByType<UIManager>()`).
  - Runtime field:
    - `bool outcomeTriggered;` (guards against triggering an outcome twice in a single run).
  - When `cureComplete`:
    - If `outcomeTriggered`, return.
    - Set `outcomeTriggered = true`.
    - Compute `savedProvinces` and `fullyInfectedProvinces`.
    - Call `GameOutcomeState.SetOutcome(GameOutcomeKind.Victory, globalState, dayIndex, savedProvinces, fullyInfectedProvinces)`.
    - Call `uiManager.ShowEndScreen()`.
  - When `allFullyInfected`:
    - If `outcomeTriggered`, return.
    - Set `outcomeTriggered = true`.
    - Compute `savedProvinces` and `fullyInfectedProvinces`.
    - Call `GameOutcomeState.SetOutcome(GameOutcomeKind.Defeat, globalState, dayIndex, savedProvinces, fullyInfectedProvinces)`.
    - Call `uiManager.ShowEndScreen()`.
- **Modify UXML**: `Assets/UI/Layouts/Screens/EndMenu.uxml`
  - Add labels for:
    - `OutcomeTitleLabel` (“CURE DEPLOYED – VICTORY” / “OUTBREAK LOST – DEFEAT”).
    - `OutcomeSubtitleLabel` (short flavour text).
    - `StatsDaysLabel` (e.g. “Days elapsed: 12”).
    - `StatsCureLabel` (e.g. “Cure progress: 100%” or “Cure stalled at 63%”).
    - `StatsProvincesLabel` (e.g. “Provinces saved: 6 / 9”).
    - `StatsOutpostsLabel` (e.g. “Outposts: 5 active / 7 total”).
    - `StatsZarLabel` (e.g. “Budget remaining: R 45”).
  - Keep existing buttons/layout (Restart, Menu, Exit) intact.
- **Modify**: `Assets/UI/Scripts/Screens/EndMenuController.cs`
  - On initialization:
    - Query the new labels by name.
    - Read `GameOutcomeState.LastOutcome` and related stats.
    - Set:
      - `OutcomeTitleLabel.text` and `OutcomeSubtitleLabel.text` to different values for Victory vs Defeat.
      - Stats labels from the stored values.
    - Optionally apply different CSS classes for victory/defeat (e.g. `end-outcome--victory`, `end-outcome--defeat`).

**Agent notes:**
- Keep the outcome data static, not persisted between runs; it’s only for the immediate end screen.
- Ensure that calling `ShowEndScreen()` from the simulation does not conflict with pause flows (it already routes to the `End` scene).

---

### 7) `feat: expose tuning knobs for challenge level` — ✅ Complete

**Goal:** Make infection / cure rates, diminishing returns, and hub bonuses tunable in the editor and document defaults aligned with the desired game length and difficulty.

- **Modify**: `OutbreakSimulationController`
  - Ensure all rate/config structs (`OutpostRateConfig`, `VirusRateConfig`, `OutpostCostConfig`) are marked `[Serializable]` and exposed as `[SerializeField]` fields.
  - Provide initial defaults aimed at “challenging but fair”:
    - `VirusRateConfig.BaseInfectionPerHour`: ~`0.01f`–`0.015f`.
    - `VirusRateConfig.DailyVirusGrowth`: ~`0.05f`–`0.08f`.
    - `OutpostRateConfig.LocalCurePerHour`: ~`0.02f`.
    - `OutpostRateConfig.GlobalCurePerHourPerOutpost`: tuned so that with ~5–8 active outposts, cure reaches 100% after roughly 10–15 in-game days.
    - `OutpostRateConfig.DiminishingReturnFactor`: `0.9f`.
    - `VirusRateConfig.OutpostDisableThreshold01`: `0.8f`.
    - `VirusRateConfig.FullyInfectedThreshold01`: `0.99f`.
    - `OutpostCostConfig.BaseCostR`: `20`.
    - `OutpostCostConfig.CostPerExistingOutpostR`: `5`–`10`.
    - `startingZarBalance`: enough to build a few early outposts (e.g. `R 200`).
  - Populate `urbanHubRegionIds` with IDs matching:
    - Gauteng (`ZAGP`)
    - Western Cape (`ZAWC`)
    - KwaZulu-Natal (`ZAKZN`)
- **Update**: `FEATURE.md` (this file) if real-world tuning deviates significantly, so designers know what each knob does.

Current defaults in `OutbreakSimulationController` land at: `BaseInfectionPerHour = 0.0125f`, `DailyVirusGrowth = 0.06f`, `LocalCurePerHour = 0.02f`, `GlobalCurePerHourPerOutpost = 0.01f`, `DiminishingReturnFactor = 0.9f`, `OutpostDisableThreshold01 = 0.8f`, `FullyInfectedThreshold01 = 0.99f`, `BaseCostR = 20`, `CostPerExistingOutpostR = 8`, and `startingZarBalance = 200`.

**Agent notes:**
- Tune primarily by adjusting rates in this controller; prefer not to touch `DayNightCycleController` unless session length feels very off.
- Re‑validate `Main.unity` after setting references to ensure there are no missing components.

---

### 8) `feat: add light telemetry and optional tests` — ✅ Complete
  - Diagnostics flag implemented; lightweight unit tests still optional if future regressions appear.

**Goal:** Provide basic diagnostics to help tune the system and, optionally, add a couple of unit tests for the pure data model.

- **Modify**: `OutbreakSimulationController`
  - Add an optional `bool logSummaryToConsole` field.
  - When enabled, log a short summary every in‑game day or at a fixed in‑game time interval:
    - Day index, global cure %, average infection %, saved vs fully infected provinces, outposts active/total, ZAR balance.
- **New tests (optional, if test framework is in use)**:
  - Under `Assets/Tests`, add editor or play mode tests that:
    - Verify `ComputeOutpostCostR` and diminishing returns behave as expected.
    - Confirm that a mix of infection growth and local curing leads to:
      - Infection stabilizing below 80% in a province with several outposts under certain parameters.
      - The global cure meter progressing roughly as expected given N outposts and configuration.

**Agent notes:**
- Only add tests if they fit the existing testing approach in `Assets/Tests`.
- Keep logs guarded behind the `logSummaryToConsole` flag to avoid noisy consoles in normal play.

---

## Implementation Checklist (Per‑Agent)

When implementing this feature, agents should:

- Work under `Assets/` (scripts, UXML, USS) using Unity MCP tools (`script_apply_edits`, `manage_asset`, `manage_scene` / `manage_gameobject`) so Unity tracks GUIDs correctly.
- After each commit:
  - Rebuild / enter play mode in the `Main` scene.
  - Verify there are no console errors.
  - Check that time still advances, the map renders correctly, and the existing HUD behaviour (timer + province info) remains intact.
- Avoid modifying:
  - `RegionDatabase.asset` content directly (rely on IDs and existing metadata).
  - Package / ProjectSettings.

This plan should provide a straightforward path to:

- Deploy outposts only in viable provinces using a shared ZAR budget.
- Stack curing power with diminishing global returns and modest urban hub bonuses.
- Maintain tension via virus pushback and temporary outpost disablement.
- Win by pushing the global cure meter to 100% before every province becomes fully infected, over a run that spans 10+ in‑game days and feels meaningfully challenging.
