using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Zarus.Map;
using Zarus.Systems;

namespace Zarus.UI
{
    /// <summary>
    /// Controls the in-game HUD display with timer, stats, and province info.
    /// </summary>
    public class GameHUD : UIScreen
    {
        [Header("Game References")]
        [SerializeField]
        private RegionMapController mapController;

        [Header("Time Source")]
        [SerializeField]
        private DayNightCycleController dayNightController;

        [Header("Outbreak Simulation")]
        [SerializeField]
        private OutbreakSimulationController outbreakSimulation;

        // UI Elements
        private ProgressBar cureProgressBar;
        private Label cureProgressDetailsLabel;
        private Label outpostCountLabel;
        private Label zarBalanceLabel;
        private Label timerValue;
        private Label timerSubValueLabel;
        private Label timerDetailLabel;
        private Label provinceNameLabel;
        private Label provinceDescLabel;
        private VisualElement provinceInfoContainer;
        private VisualElement outpostActions;
        private Label outpostStatusLabel;
        private Label provinceInfectionLabel;
        private Button buildOutpostButton;
        private Label buildOutpostCostLabel;

        // Game State
        private HashSet<string> visitedProvinces = new HashSet<string>();
        private RegionEntry selectedRegion;
        private InGameTimeSnapshot latestTimeSnapshot;
        private bool hasTimeSnapshot;
        private bool provinceInfoVisible;
        private float provinceInfoTimer;
        private GlobalCureState latestGlobalState;
        private bool simulationEventsHooked;

        private const float ProvinceInfoDisplayDuration = 4f;
        private const float TimeScaleDisplayBaseline = 30f;
        private static readonly string[] OutpostStatusClasses =
        {
            "hud-outpost-status--none",
            "hud-outpost-status--active",
            "hud-outpost-status--disabled"
        };

        private string SelectedRegionId => selectedRegion != null ? selectedRegion.RegionId : null;

        protected override void Initialize()
        {
            // Ensure we have a valid document
            if (uiDocument == null)
            {
                Debug.LogError("[GameHUD] UIDocument is null! Assign it in the Inspector.");
                return;
            }
            
            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[GameHUD] UIDocument root element is null!");
                return;
            }

            Debug.Log($"[GameHUD] Initializing... Root element: {root.name}, childCount: {root.childCount}");

            // Query UI elements directly from root
            timerValue = root.Q<Label>("TimerValue");
            timerSubValueLabel = root.Q<Label>("TimerSubValue");
            timerDetailLabel = root.Q<Label>("TimerDetail");
            provinceInfoContainer = root.Q<VisualElement>("ProvinceInfo");
            provinceNameLabel = root.Q<Label>("ProvinceNameLabel");
            provinceDescLabel = root.Q<Label>("ProvinceDescLabel");
            cureProgressBar = root.Q<ProgressBar>("CureProgressBar");
            cureProgressDetailsLabel = root.Q<Label>("CureProgressDetailsLabel");
            outpostCountLabel = root.Q<Label>("OutpostCountLabel");
            zarBalanceLabel = root.Q<Label>("ZarBalanceLabel");
            outpostActions = root.Q<VisualElement>("OutpostActions");
            outpostStatusLabel = root.Q<Label>("OutpostStatusLabel");
            provinceInfectionLabel = root.Q<Label>("ProvinceInfectionLabel");
            buildOutpostButton = root.Q<Button>("BuildOutpostButton");
            buildOutpostCostLabel = root.Q<Label>("BuildOutpostCostLabel");

            // Verify all elements were found
            Debug.Log($"[GameHUD] Elements found - TimerValue: {timerValue != null}, TimerSubValue: {timerSubValueLabel != null}, TimerDetail: {timerDetailLabel != null}, ProvinceNameLabel: {provinceNameLabel != null}, ProvinceDescLabel: {provinceDescLabel != null}");

            if (timerValue == null) Debug.LogError("[GameHUD] TimerValue not found in UXML!");
            if (provinceNameLabel == null) Debug.LogError("[GameHUD] ProvinceNameLabel not found in UXML!");
            if (provinceDescLabel == null) Debug.LogError("[GameHUD] ProvinceDescLabel not found in UXML!");

            // Force visibility on all elements
            if (timerValue != null)
            {
                timerValue.style.display = DisplayStyle.Flex;
                timerValue.style.visibility = Visibility.Visible;
                timerValue.style.opacity = 1f;
            }
            if (timerSubValueLabel != null)
            {
                timerSubValueLabel.style.display = DisplayStyle.Flex;
                timerSubValueLabel.style.visibility = Visibility.Visible;
                timerSubValueLabel.style.opacity = 1f;
            }
            if (timerDetailLabel != null)
            {
                timerDetailLabel.style.display = DisplayStyle.Flex;
                timerDetailLabel.style.visibility = Visibility.Visible;
                timerDetailLabel.style.opacity = 1f;
            }
            if (provinceNameLabel != null)
            {
                provinceNameLabel.style.display = DisplayStyle.Flex;
                provinceNameLabel.style.visibility = Visibility.Visible;
                provinceNameLabel.style.opacity = 1f;
            }
            if (provinceDescLabel != null)
            {
                provinceDescLabel.style.display = DisplayStyle.Flex;
                provinceDescLabel.style.visibility = Visibility.Visible;
                provinceDescLabel.style.opacity = 1f;
            }

            ResetOutpostActionsUI();

            // Find map controller if not assigned
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }

            // Subscribe to map events
            if (mapController != null)
            {
                mapController.OnRegionHovered.AddListener(OnProvinceHovered);
                mapController.OnRegionSelected.AddListener(OnProvinceSelected);
            }

            if (dayNightController == null)
            {
                dayNightController = FindFirstObjectByType<DayNightCycleController>();
            }

            if (dayNightController == null)
            {
                var bootstrapGo = new GameObject("DayNightCycleAuto");
                dayNightController = bootstrapGo.AddComponent<DayNightCycleController>();
            }

            if (dayNightController != null)
            {
                dayNightController.TimeUpdated += HandleTimeUpdated;
                if (dayNightController.HasTime)
                {
                    HandleTimeUpdated(dayNightController.CurrentTime);
                }
            }
            else
            {
                Debug.LogWarning("[GameHUD] DayNightCycleController not found; timer display will not reflect in-game time.");
            }

            if (buildOutpostButton != null)
            {
                buildOutpostButton.clicked += OnBuildOutpostClicked;
            }

            HookOutbreakSimulationEvents();

            // Initialize displays
            UpdateTimer();
            HideProvinceInfo(true);
            
            Debug.Log($"[GameHUD] Initialization complete. Timer text: '{timerValue?.text}', Timer visible: {timerValue?.visible}, Timer display: {timerValue?.style.display}");
        }

        private void Update()
        {
            if (provinceInfoVisible)
            {
                provinceInfoTimer -= Time.deltaTime;
                if (provinceInfoTimer <= 0f)
                {
                    HideProvinceInfo();
                }
            }
        }

        private void UpdateTimer()
        {
            if (timerValue == null) return;

            if (hasTimeSnapshot)
            {
                var timeText = latestTimeSnapshot.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                timerValue.text = $"Day {latestTimeSnapshot.DayIndex}";

                if (timerSubValueLabel != null)
                {
                    timerSubValueLabel.text = $"{timeText} | {GetTimeScaleDisplay()} speed";
                }

                if (timerDetailLabel != null)
                {
                    timerDetailLabel.text = FormatDetailedDate(latestTimeSnapshot.DateTime);
                }

            }
            else
            {
                timerValue.text = "Day --";
                if (timerSubValueLabel != null) timerSubValueLabel.text = "--:-- | – speed";
                if (timerDetailLabel != null) timerDetailLabel.text = "Waiting for time";
            }
        }

        private void HandleTimeUpdated(InGameTimeSnapshot snapshot)
        {
            latestTimeSnapshot = snapshot;
            hasTimeSnapshot = true;
            UpdateTimer();
        }

        private string GetTimeScaleDisplay()
        {
            var scale = dayNightController != null ? dayNightController.TimeScale : TimeScaleDisplayBaseline;
            if (Mathf.Approximately(TimeScaleDisplayBaseline, 0f))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.#}x", scale);
            }

            var relative = scale / TimeScaleDisplayBaseline;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#}x", relative);
        }

        private static string FormatDetailedDate(System.DateTime dateTime)
        {
            var month = dateTime.ToString("MMM", CultureInfo.InvariantCulture);
            var day = dateTime.Day;
            var suffix = GetDaySuffix(day);
            return $"{month} {day}{suffix} {dateTime:yyyy}";
        }

        private static string GetDaySuffix(int day)
        {
            var rem100 = day % 100;
            if (rem100 >= 11 && rem100 <= 13)
            {
                return "th";
            }

            return (day % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        private void ShowProvinceInfo()
        {
            if (provinceInfoContainer == null)
            {
                return;
            }

            provinceInfoVisible = true;
            provinceInfoTimer = ProvinceInfoDisplayDuration;
            provinceInfoContainer.AddToClassList("hud-province-info--visible");
        }

        private void HideProvinceInfo(bool immediate = false)
        {
            if (provinceInfoContainer == null)
            {
                return;
            }

            provinceInfoVisible = false;
            provinceInfoTimer = 0f;
            provinceInfoContainer.RemoveFromClassList("hud-province-info--visible");
        }

        private void OnProvinceHovered(RegionEntry region)
        {
            if (region == null) return;

            // Only show hover when nothing is selected
            if (selectedRegion != null) return;

            if (provinceNameLabel != null)
                provinceNameLabel.text = region.DisplayName.ToUpper();

            if (provinceDescLabel != null)
                provinceDescLabel.text = !string.IsNullOrEmpty(region.Description)
                    ? region.Description
                    : "Hover over to explore";
        }

        private void OnProvinceSelected(RegionEntry region)
        {
            if (region == null) return;

            selectedRegion = region;

            // Mark province as visited
            if (!visitedProvinces.Contains(region.RegionId))
            {
                visitedProvinces.Add(region.RegionId);
                Debug.Log($"[GameHUD] Province visited: {region.DisplayName}");
            }

            // Update info display
            if (provinceNameLabel != null)
            {
                provinceNameLabel.text = $"★ {region.DisplayName.ToUpper()} ★";
            }

            if (provinceDescLabel != null)
            {
                string desc = !string.IsNullOrEmpty(region.Description)
                    ? region.Description
                    : string.Empty;
                provinceDescLabel.text = desc;
            }

            ShowProvinceInfo();
            RefreshSelectedProvinceState();
        }

        private void HookOutbreakSimulationEvents()
        {
            if (simulationEventsHooked)
            {
                return;
            }

            if (outbreakSimulation == null)
            {
                outbreakSimulation = FindFirstObjectByType<OutbreakSimulationController>();
            }

            if (outbreakSimulation == null)
            {
                Debug.LogWarning("[GameHUD] OutbreakSimulationController not found; cure HUD widgets will remain inactive.");
                return;
            }

            outbreakSimulation.GlobalStateChanged += HandleGlobalStateChanged;
            outbreakSimulation.ProvinceStateChanged += HandleProvinceStateChanged;
            simulationEventsHooked = true;

            if (outbreakSimulation.GlobalState != null)
            {
                HandleGlobalStateChanged(outbreakSimulation.GlobalState);
            }

            RefreshSelectedProvinceState();
        }

        private void HandleGlobalStateChanged(GlobalCureState state)
        {
            latestGlobalState = state;
            var progress01 = state != null ? Mathf.Clamp01(state.CureProgress01) : 0f;
            var progressPercent = progress01 * 100f;

            if (cureProgressBar != null)
            {
                cureProgressBar.value = progressPercent;
                cureProgressBar.title = string.Format(CultureInfo.InvariantCulture, "{0:0}%", progressPercent);
            }

            var activeOutposts = state?.ActiveOutpostCount ?? 0;
            var totalOutposts = state?.TotalOutpostCount ?? 0;

            if (cureProgressDetailsLabel != null)
            {
                cureProgressDetailsLabel.text = string.Format(CultureInfo.InvariantCulture,
                    "Researching cure – {0} active / {1} total",
                    activeOutposts,
                    totalOutposts);
            }

            if (outpostCountLabel != null)
            {
                outpostCountLabel.text = string.Format(CultureInfo.InvariantCulture,
                    "Outposts: {0} active / {1} total",
                    activeOutposts,
                    totalOutposts);
            }

            if (zarBalanceLabel != null)
            {
                var balance = state?.ZarBalance ?? 0;
                zarBalanceLabel.text = string.Format(CultureInfo.InvariantCulture, "Budget: R {0}", balance);
            }

            UpdateBuildControls();
        }

        private void HandleProvinceStateChanged(ProvinceInfectionState province)
        {
            if (province == null || string.IsNullOrEmpty(SelectedRegionId))
            {
                return;
            }

            if (!string.Equals(province.RegionId, SelectedRegionId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UpdateProvinceOutpostDisplay(province);
        }

        private void RefreshSelectedProvinceState()
        {
            if (outbreakSimulation == null || string.IsNullOrEmpty(SelectedRegionId))
            {
                UpdateProvinceOutpostDisplay(null);
                return;
            }

            if (outbreakSimulation.TryGetProvinceState(SelectedRegionId, out var province))
            {
                UpdateProvinceOutpostDisplay(province);
            }
            else
            {
                UpdateProvinceOutpostDisplay(null);
            }
        }

        private void UpdateProvinceOutpostDisplay(ProvinceInfectionState state)
        {
            if (provinceInfectionLabel != null)
            {
                if (state == null)
                {
                    provinceInfectionLabel.text = "Infection: --%";
                }
                else
                {
                    var percent = Mathf.RoundToInt(Mathf.Clamp01(state.Infection01) * 100f);
                    provinceInfectionLabel.text = string.Format(CultureInfo.InvariantCulture, "Infection: {0}%", percent);
                }
            }

            if (state == null)
            {
                var defaultText = string.IsNullOrEmpty(SelectedRegionId) ? "Select a province" : "No outbreak data";
                SetOutpostStatusText(defaultText, "hud-outpost-status--none");
                UpdateBuildControls();
                return;
            }

            if (!state.HasOutpost)
            {
                SetOutpostStatusText("No outposts here", "hud-outpost-status--none");
            }
            else if (state.OutpostDisabled)
            {
                var percent = Mathf.RoundToInt(Mathf.Clamp01(state.Infection01) * 100f);
                var disabledText = string.Format(CultureInfo.InvariantCulture,
                    "{0} outposts DISABLED at {1}% infection",
                    state.OutpostCount,
                    percent);
                SetOutpostStatusText(disabledText, "hud-outpost-status--disabled");
            }
            else
            {
                var activeText = string.Format(CultureInfo.InvariantCulture,
                    "{0} outposts ACTIVE",
                    state.OutpostCount);
                SetOutpostStatusText(activeText, "hud-outpost-status--active");
            }

            UpdateBuildControls(state);
        }

        private void UpdateBuildControls(ProvinceInfectionState provinceState = null)
        {
            if (buildOutpostButton == null)
            {
                return;
            }

            if (outbreakSimulation == null || selectedRegion == null)
            {
                buildOutpostButton.SetEnabled(false);
                if (buildOutpostCostLabel != null)
                {
                    buildOutpostCostLabel.text = "Cost: R --";
                }

                return;
            }

            provinceState ??= (outbreakSimulation.TryGetProvinceState(selectedRegion.RegionId, out var fetchedState)
                ? fetchedState
                : null);

            if (provinceState == null)
            {
                buildOutpostButton.SetEnabled(false);
                if (buildOutpostCostLabel != null)
                {
                    buildOutpostCostLabel.text = "Cost: R --";
                }

                return;
            }

            var canBuild = outbreakSimulation.CanBuildOutpost(selectedRegion.RegionId, out var costR, out _);
            buildOutpostButton.SetEnabled(canBuild);
            if (buildOutpostCostLabel != null)
            {
                buildOutpostCostLabel.text = string.Format(CultureInfo.InvariantCulture, "Cost: R {0}", costR);
            }
        }

        private void ResetOutpostActionsUI()
        {
            SetOutpostStatusText("Select a province", "hud-outpost-status--none");
            if (provinceInfectionLabel != null)
            {
                provinceInfectionLabel.text = "Infection: --%";
            }

            if (buildOutpostCostLabel != null)
            {
                buildOutpostCostLabel.text = "Cost: R --";
            }

            buildOutpostButton?.SetEnabled(false);
        }

        private void SetOutpostStatusText(string text, string statusClass)
        {
            if (outpostStatusLabel == null)
            {
                return;
            }

            outpostStatusLabel.text = text;
            ApplyOutpostStatusClass(statusClass);
        }

        private void ApplyOutpostStatusClass(string className)
        {
            if (outpostStatusLabel == null)
            {
                return;
            }

            foreach (var cls in OutpostStatusClasses)
            {
                outpostStatusLabel.RemoveFromClassList(cls);
            }

            if (!string.IsNullOrEmpty(className))
            {
                outpostStatusLabel.AddToClassList(className);
            }
        }

        private void OnBuildOutpostClicked()
        {
            if (outbreakSimulation == null || selectedRegion == null)
            {
                return;
            }

            if (outbreakSimulation.TryBuildOutpost(selectedRegion.RegionId, out var costR, out var error))
            {
                return;
            }

            switch (error)
            {
                case OutbreakSimulationController.OutpostBuildError.NotEnoughZar:
                    SetOutpostStatusText(string.Format(CultureInfo.InvariantCulture, "Not enough budget (R {0} needed)", costR),
                        "hud-outpost-status--disabled");
                    break;
                case OutbreakSimulationController.OutpostBuildError.ProvinceFullyInfected:
                    SetOutpostStatusText("Province fully infected – cannot deploy", "hud-outpost-status--disabled");
                    break;
                case OutbreakSimulationController.OutpostBuildError.InvalidRegion:
                    SetOutpostStatusText("Unknown province – cannot deploy", "hud-outpost-status--disabled");
                    break;
            }

            UpdateBuildControls();
        }

        /// <summary>
        /// Resets the game timer.
        /// </summary>
        public void ResetTimer()
        {
            dayNightController?.RestartCycle();
        }

        /// <summary>
        /// Resets the visited provinces counter.
        /// </summary>
        public void ResetVisitedProvinces()
        {
            visitedProvinces.Clear();
        }

        /// <summary>
        /// Gets the current game time in seconds.
        /// </summary>
        public float GetGameTime()
        {
            if (!hasTimeSnapshot)
            {
                return 0f;
            }

            return latestTimeSnapshot.TimeOfDayMinutes * 60f;
        }

        /// <summary>
        /// Gets the number of provinces visited.
        /// </summary>
        public int GetVisitedProvincesCount()
        {
            return visitedProvinces.Count;
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (mapController != null)
            {
                mapController.OnRegionHovered.RemoveListener(OnProvinceHovered);
                mapController.OnRegionSelected.RemoveListener(OnProvinceSelected);
            }

            if (dayNightController != null)
            {
                dayNightController.TimeUpdated -= HandleTimeUpdated;
            }

            if (simulationEventsHooked && outbreakSimulation != null)
            {
                outbreakSimulation.GlobalStateChanged -= HandleGlobalStateChanged;
                outbreakSimulation.ProvinceStateChanged -= HandleProvinceStateChanged;
                simulationEventsHooked = false;
            }

            if (buildOutpostButton != null)
            {
                buildOutpostButton.clicked -= OnBuildOutpostClicked;
            }
        }
    }
}
