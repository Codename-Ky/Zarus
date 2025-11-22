using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Zarus.Map;

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

        // UI Elements
        private Label timerValue;
        private Label provincesValue;
        private Label provinceNameLabel;
        private Label provinceDescLabel;

        // Game State
        private DateTime startTime;
        private float gameTime;
        private HashSet<string> visitedProvinces = new HashSet<string>();
        private int totalProvinces = 9; // South Africa has 9 provinces
        private RegionEntry selectedRegion;

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
            provincesValue = root.Q<Label>("ProvincesValue");
            provinceNameLabel = root.Q<Label>("ProvinceNameLabel");
            provinceDescLabel = root.Q<Label>("ProvinceDescLabel");
            
            // Verify all elements were found
            Debug.Log($"[GameHUD] Elements found - TimerValue: {timerValue != null}, ProvincesValue: {provincesValue != null}, ProvinceNameLabel: {provinceNameLabel != null}, ProvinceDescLabel: {provinceDescLabel != null}");
            
            if (timerValue == null) Debug.LogError("[GameHUD] TimerValue not found in UXML!");
            if (provincesValue == null) Debug.LogError("[GameHUD] ProvincesValue not found in UXML!");
            if (provinceNameLabel == null) Debug.LogError("[GameHUD] ProvinceNameLabel not found in UXML!");
            if (provinceDescLabel == null) Debug.LogError("[GameHUD] ProvinceDescLabel not found in UXML!");

            // Force visibility on all elements
            if (timerValue != null)
            {
                timerValue.style.display = DisplayStyle.Flex;
                timerValue.style.visibility = Visibility.Visible;
                timerValue.style.opacity = 1f;
            }
            if (provincesValue != null)
            {
                provincesValue.style.display = DisplayStyle.Flex;
                provincesValue.style.visibility = Visibility.Visible;
                provincesValue.style.opacity = 1f;
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
                totalProvinces = mapController.Entries.Count;
            }

            // Initialize displays
            startTime = DateTime.Now;
            UpdateTimer();
            UpdateProvincesCounter();
            
            Debug.Log($"[GameHUD] Initialization complete. Timer text: '{timerValue?.text}', Provinces text: '{provincesValue?.text}', Timer visible: {timerValue?.visible}, Timer display: {timerValue?.style.display}");
        }

        private void Update()
        {
            // Always show real-world 24h clock (HH:mm)
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (timerValue == null) return;

            var now = DateTime.Now;
            gameTime = (float)(now - startTime).TotalSeconds;
            timerValue.text = now.ToString("HH:mm");
        }

        private void UpdateProvincesCounter()
        {
            if (provincesValue == null) return;

            provincesValue.text = $"{visitedProvinces.Count} / {totalProvinces}";
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
                UpdateProvincesCounter();
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
                    : "Selected province";
                provinceDescLabel.text = desc;
            }
        }

        /// <summary>
        /// Resets the game timer.
        /// </summary>
        public void ResetTimer()
        {
            startTime = DateTime.Now;
            gameTime = 0f;
            UpdateTimer();
        }

        /// <summary>
        /// Resets the visited provinces counter.
        /// </summary>
        public void ResetVisitedProvinces()
        {
            visitedProvinces.Clear();
            UpdateProvincesCounter();
        }

        /// <summary>
        /// Gets the current game time in seconds.
        /// </summary>
        public float GetGameTime()
        {
            return gameTime;
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
        }
    }
}