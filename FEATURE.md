# Feature: Day/Night Cycle & HUD Clock Revamp

## Goals
- decouple the HUD clock from `DateTime.Now` so it reflects an in-world calendar/day counter rather than the player’s local clock
- introduce a deterministic-yet-randomized in-game calendar (day 1 at a random modern-era date) that advances at 60× speed (1 real second = 1 in-game minute)
- drive a full day/night cycle that affects directional light, ambient colors, and the map’s emission so the “sun” and “country lights” read clearly
- leave room for future systems (events that depend on time of day, saving/loading time state) by building a reusable controller + event surface

## Current State Recap
- `Assets/UI/Scripts/Screens/GameHUD.cs` currently calls `DateTime.Now` every `Update()` and simply prints the player’s local `HH:mm`.
- There is no shared in-game clock or calendar object; nothing else depends on time-of-day.
- The map rendering (`RegionMapController`) already pushes emissive color via a `MaterialPropertyBlock`, which gives us a hook for “night lights” by raising emission.
- Lighting in `Main.unity` is static: URP ForwardRenderer + whichever `Light`/Volume components are baked into the scene.

## Proposed Systems & Data Flow

### 1. `DayNightCycleController`
- Lives under `Assets/Scripts/Systems/` (new folder) and is a `MonoBehaviour` placed in `Main.unity`.
- Serialized settings:
  - `timeScale` (default 60f) to convert real seconds → in-game minutes; clamps to >0 for robustness.
- `Vector2Int startYearRange`, `startMonthRange`, `startDayRange` or a `SerializableDateRange` struct to pick the initial date (clamped to a “modern era” window, e.g., 1994–present, per direction).
  - `AnimationCurve sunElevationCurve`, `AnimationCurve sunIntensityCurve`, and `Gradient ambientColor` to shape light behavior per normalized day progress.
  - References to scene lighting: `Light sunLight`, `Light moonLight` (optional), `VolumeProfile dayVolume`, `VolumeProfile nightVolume`, `RegionMapController mapController` (for emission boosts), and any “city lights” `GameObject` that should toggle at night.
- Runtime state:
  - `DateTime currentDateTime`, `int dayIndex` (Day 1-based), `float normalizedTimeOfDay` (0..1), `bool isNight`.
  - `float accumulatedMinutes` to avoid floating-point drift.
- Responsibilities:
  - On `Start()`, roll a random initial date, set `dayIndex = 1`, and broadcast state.
  - Each `Update()`, advance minutes by `Time.deltaTime * timeScale`, convert to hours/days, increment `currentDateTime`, and raise events when the date flips.
  - Provide `UnityEvent<InGameTimeSnapshot>` or C# event so UI/other systems can subscribe.
  - Update lighting each frame: rotate the `sunLight` to simulate azimuth, lerp intensity/color via curves, fade moon/ambient light, and toggle emissive “night lights” window when `normalizedTimeOfDay` crosses thresholds (e.g., >0.75 or <0.25).
  - Expose methods like `public InGameTimeSnapshot CurrentTime => ...` for direct polling.

### 2. `InGameTimeSnapshot` data struct
- Plain struct/class describing `DateTime dateTime`, `int dayIndex`, `float timeOfDayMinutes` (0–1440), `float normalized01`, `bool isDaytime`.
- Stored in the controller and re-used for UI updates, logging, and potential save data.
- Keeps formatting logic (e.g., `ToClockString()` returning `Day 3 · 21:45`), ensuring GameHUD only formats data, not compute time.

### 3. HUD integration
- Update `GameHUD` to reference the controller (serialized field or `FindFirstObjectByType<DayNightCycleController>()`).
- Replace `DateTime.Now` usage with either event subscription (`cycle.OnTimeChanged += HandleTimeChanged`) or polling during `Update()`.
- Display plan: `timerValue.text = $"Day {snapshot.DayIndex} — {snapshot.DateTime:MMM d, HH:mm}"` (with an additional dawn/day/dusk/night indicator badge, e.g., icons or short labels) so players can quickly identify the cycle stage.
- Optionally add a subtle progress indicator (later) since snapshot includes normalized value.

### 4. Lighting & Visual hooks
- **Sun/Moon**: Use the normalized time to rotate the `sunLight` around the map pivot. Example: `Quaternion.Euler(new Vector3(normalized * 360f - 90f, sunAzimuth, 0f))`. Intensity/Color come from `AnimationCurve`/`Gradient` to create golden hours.
- **Ambient/Volume**: Blend URP `VolumeProfile`s (day vs night) using `RenderSettings.ambientLight` or `Volume.weight` for color grading/exposure shifts.
- **Country lights**:
  - Implement a dedicated “night lights” asset or effect (custom shader/material or lightweight VFX) that the controller activates at night; lean on `RegionMapController` emission scaling only as supporting polish.
- Provide thresholds (e.g., `nightStart = 0.78`, `nightEnd = 0.25`) so we can fade rather than snap.

### 5. Robustness / Extensibility
- All public APIs live on the controller so other systems (future quests, weather) tap into `CurrentTime`.
- Serialization-friendly data ensures we can later save/load.
- Time scale + curves live in assets, so designers can tune cycle speed or sunrise times without code changes.

## Implementation Steps (once approved)
1. **Scaffold time system**
   - Create `DayNightCycleController` + `InGameTimeSnapshot` scripts under `Assets/Scripts/Systems/`.
   - Implement serialization + inspector validation (e.g., guard against invalid ranges, provide defaults for curve/gradient).
   - Compute normalized clock & events; add `[ContextMenu]` helpers for fast-forwarding when testing.
2. **Integrate HUD**
   - Inject controller reference into `GameHUD` (serialized or `Find`).
   - Subscribe to cycle events in `Initialize()` and update label text using snapshot.
   - Remove direct `DateTime.Now` logic and replace `ResetTimer()` with hooks into the controller if still needed.
3. **Wire up lighting + night lights asset**
   - Identify / create `SunLight` GameObject (Directional Light) and optional `Moon` or `NightLights` objects in `Main.unity`.
   - Use animation curves to drive `Light.intensity`, color, and rotation from the controller.
   - Add emission scaling call into `RegionMapController` (public method like `SetGlobalEmissionMultiplier(float value)` applied to all `RegionRuntime`s).
   - Build/import the dedicated night-lights asset or shader, hook its intensity to the cycle, and toggle/lerp any city lights meshes or VFX as `isNight` changes.
4. **Editor setup**
   - Drop the controller prefab/GameObject into `Main.unity` and wire references (sun light, volume profiles, map controller, etc.).
   - Verify Input System + GameHUD still initialize (controller should start before HUD or expose `IsInitialized`).
5. **Validation**
   - In editor, confirm the cycle completes roughly every 24 minutes and that HUD shows `Day 1` with the random date.
   - Ensure emission + lighting transitions look smooth at sunrise/sunset and that toggling play mode repeatedly re-randomizes Day 1.
   - Document QA steps in PR / commit message.

## Open Questions for Review
- (Answered) Date range stays within the modern era (1994–present) so historical context remains relevant.
- (Answered) HUD will surface day/night indicators alongside the time text.
- (Answered) Implement a dedicated night-light asset/shader effect rather than relying only on emission scaling.

## Commit Strategy
- Follow “feat: …” for user-facing functionality (e.g., `feat: add day-night controller` or `feat: update HUD clock`).
- Use “chore: …” for supportive work (docs, tooling, asset plumbing) when no gameplay functionality changes.
- Keep commits scoped logically (controller scaffolding, HUD integration, lighting asset hookup, etc.) to simplify code review.

Once this plan is approved, implementation can proceed on branch `feat/day-night-cycle`.
