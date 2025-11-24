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

        // UI Elements
        private Label timerValue;
        private Label timerSubValueLabel;
        private Label timerDetailLabel;
        private Label provinceNameLabel;
        private Label provinceDescLabel;
        private VisualElement provinceInfoContainer;

        // Game State
        private HashSet<string> visitedProvinces = new HashSet<string>();
        private RegionEntry selectedRegion;
        private InGameTimeSnapshot latestTimeSnapshot;
        private bool hasTimeSnapshot;
        private bool provinceInfoVisible;
        private float provinceInfoTimer;

        private const float ProvinceInfoDisplayDuration = 4f;
        private const float TimeScaleDisplayBaseline = 30f;

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
        }
    }
}
