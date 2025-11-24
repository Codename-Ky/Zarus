using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Zarus.Map;

namespace Zarus.Systems
{
    /// <summary>
    /// Drives province infection, outpost curing, and the global cure meter.
    /// </summary>
    [DisallowMultipleComponent]
    public class OutbreakSimulationController : MonoBehaviour
    {
        private const float MinutesPerDay = 1440f;

        public enum OutpostBuildError
        {
            None,
            InvalidRegion,
            ProvinceFullyInfected,
            NotEnoughZar
        }

        [Header("References")]
        [SerializeField]
        private RegionMapController mapController;

        [SerializeField]
        private DayNightCycleController dayNightController;

        [Header("Rates & Economy")]
        [SerializeField]
        private OutpostRateConfig outpostRates = new OutpostRateConfig
        {
            LocalCurePerHour = 0.02f,
            GlobalCurePerHourPerOutpost = 0.01f,
            DiminishingReturnFactor = 0.9f,
            TargetWinDayMin = 10f,
            TargetWinDayMax = 15f
        };

        [SerializeField]
        private VirusRateConfig virusRates = new VirusRateConfig
        {
            BaseInfectionPerHour = 0.0125f,
            DailyVirusGrowth = 0.06f,
            OutpostDisableThreshold01 = 0.8f,
            FullyInfectedThreshold01 = 0.99f
        };

        [SerializeField]
        private OutpostCostConfig costConfig = new OutpostCostConfig
        {
            BaseCostR = 20,
            CostPerExistingOutpostR = 8
        };

        [Header("Special Provinces")]
        [SerializeField]
        private string[] urbanHubRegionIds = { "ZAGP", "ZAWC", "ZAKZN" };

        [SerializeField, Min(1f)]
        private float urbanHubBonusMultiplier = 1.25f;

        [Header("Startup Settings")]
        [SerializeField]
        private Vector2 initialInfectionRange = new Vector2(0.05f, 0.2f);

        [SerializeField]
        private int startingZarBalance = 200;

        [Header("Events")]
        [SerializeField]
        private ProvinceStateEvent onProvinceStateChanged = new ProvinceStateEvent();

        [SerializeField]
        private GlobalStateEvent onGlobalStateChanged = new GlobalStateEvent();

        [SerializeField]
        private UnityEvent onAllProvincesFullyInfected = new UnityEvent();

        [SerializeField]
        private UnityEvent onCureCompleted = new UnityEvent();

        public event Action<ProvinceInfectionState> ProvinceStateChanged;
        public event Action<GlobalCureState> GlobalStateChanged;
        public event Action AllProvincesFullyInfected;
        public event Action CureCompleted;
        public UnityEvent<ProvinceInfectionState> OnProvinceStateChanged => onProvinceStateChanged;
        public UnityEvent<GlobalCureState> OnGlobalStateChanged => onGlobalStateChanged;
        public UnityEvent OnAllProvincesFullyInfected => onAllProvincesFullyInfected;
        public UnityEvent OnCureCompleted => onCureCompleted;

        private readonly Dictionary<string, ProvinceInfectionState> provinces =
            new Dictionary<string, ProvinceInfectionState>(StringComparer.OrdinalIgnoreCase);

        private GlobalCureState globalState;
        private InGameTimeSnapshot? lastSnapshot;
        private bool initialized;
        private bool cureCompleteRaised;
        private bool allProvincesFullyInfectedRaised;

        public IReadOnlyDictionary<string, ProvinceInfectionState> Provinces => provinces;
        public GlobalCureState GlobalState => globalState;
        public OutpostCostConfig CostConfig => costConfig;
        public OutpostRateConfig OutpostRates => outpostRates;
        public VirusRateConfig VirusRates => virusRates;

        private void Awake()
        {
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }

            if (dayNightController == null)
            {
                dayNightController = FindFirstObjectByType<DayNightCycleController>();
            }

            globalState = new GlobalCureState
            {
                CureProgress01 = 0f,
                TotalOutpostCount = 0,
                ActiveOutpostCount = 0,
                ZarBalance = startingZarBalance
            };
        }

        private void OnEnable()
        {
            if (dayNightController != null)
            {
                dayNightController.TimeUpdated += OnTimeUpdated;
                if (dayNightController.HasTime)
                {
                    lastSnapshot = dayNightController.CurrentTime;
                }
            }

            if (!initialized)
            {
                InitializeFromMap();
            }
        }

        private void OnDisable()
        {
            if (dayNightController != null)
            {
                dayNightController.TimeUpdated -= OnTimeUpdated;
            }
        }

        public void InitializeFromMap()
        {
            provinces.Clear();
            initialized = false;

            if (mapController == null)
            {
                Debug.LogWarning("[OutbreakSimulation] MapController is missing; cannot initialize provinces.");
                return;
            }

            var entries = mapController.Entries;
            if (entries == null || entries.Count == 0)
            {
                Debug.LogWarning("[OutbreakSimulation] No map entries available for initialization.");
                return;
            }

            var minSeed = Mathf.Clamp01(Mathf.Min(initialInfectionRange.x, initialInfectionRange.y));
            var maxSeed = Mathf.Clamp01(Mathf.Max(initialInfectionRange.x, initialInfectionRange.y));
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.RegionId))
                {
                    continue;
                }

                var infectionSeed = Mathf.Approximately(minSeed, maxSeed)
                    ? minSeed
                    : UnityEngine.Random.Range(minSeed, maxSeed);

                var state = new ProvinceInfectionState
                {
                    RegionId = entry.RegionId,
                    Infection01 = infectionSeed,
                    OutpostCount = 0,
                    OutpostDisabled = false,
                    IsFullyInfected = false
                };

                provinces[entry.RegionId] = state;
                RaiseProvinceStateChanged(state);
            }

            globalState.CureProgress01 = 0f;
            globalState.ActiveOutpostCount = 0;
            globalState.TotalOutpostCount = 0;
            globalState.ZarBalance = startingZarBalance;
            RaiseGlobalStateChanged();

            initialized = true;
            cureCompleteRaised = false;
            allProvincesFullyInfectedRaised = false;
        }

        private void OnTimeUpdated(InGameTimeSnapshot snapshot)
        {
            if (!initialized)
            {
                InitializeFromMap();
            }

            if (!initialized)
            {
                return;
            }

            if (!lastSnapshot.HasValue)
            {
                lastSnapshot = snapshot;
                return;
            }

            var previous = lastSnapshot.Value;
            var dayDelta = snapshot.DayIndex - previous.DayIndex;
            var deltaMinutes = snapshot.TimeOfDayMinutes - previous.TimeOfDayMinutes + dayDelta * MinutesPerDay;
            if (deltaMinutes < 0f)
            {
                deltaMinutes = 0f;
            }

            lastSnapshot = snapshot;

            if (deltaMinutes <= 0f)
            {
                return;
            }

            var deltaHours = deltaMinutes / 60f;
            SimulateStep(deltaHours, snapshot.DayIndex);
        }

        private void SimulateStep(float deltaHours, int dayIndex)
        {
            if (deltaHours <= 0f || provinces.Count == 0)
            {
                return;
            }

            var virusStrengthFactor = 1f + Mathf.Max(0, dayIndex - 1) * virusRates.DailyVirusGrowth;
            var fullyInfectedCount = 0;

            foreach (var state in provinces.Values)
            {
                var previousInfection = state.Infection01;
                var previousDisabled = state.OutpostDisabled;
                var previousFullyInfected = state.IsFullyInfected;

                var infectionIncrease = Mathf.Max(0f, virusRates.BaseInfectionPerHour) * virusStrengthFactor * deltaHours;
                var localCure = 0f;
                if (state.OutpostCount > 0 && !state.OutpostDisabled)
                {
                    localCure = Mathf.Max(0f, outpostRates.LocalCurePerHour) * state.OutpostCount * deltaHours;
                }

                state.Infection01 = Mathf.Clamp01(state.Infection01 + infectionIncrease - localCure);

                if (state.OutpostCount > 0)
                {
                    if (!state.OutpostDisabled && state.Infection01 >= virusRates.OutpostDisableThreshold01)
                    {
                        state.OutpostDisabled = true;
                    }
                    else if (state.OutpostDisabled && state.Infection01 < virusRates.OutpostDisableThreshold01)
                    {
                        state.OutpostDisabled = false;
                    }
                }
                else
                {
                    state.OutpostDisabled = false;
                }

                state.IsFullyInfected = state.Infection01 >= virusRates.FullyInfectedThreshold01;
                if (state.IsFullyInfected)
                {
                    fullyInfectedCount++;
                }

                if (!Mathf.Approximately(previousInfection, state.Infection01) || previousDisabled != state.OutpostDisabled || previousFullyInfected != state.IsFullyInfected)
                {
                    RaiseProvinceStateChanged(state);
                }
            }

            UpdateGlobalCure(deltaHours);

            var allFullyInfected = fullyInfectedCount == provinces.Count && provinces.Count > 0;
            if (allFullyInfected && !allProvincesFullyInfectedRaised)
            {
                allProvincesFullyInfectedRaised = true;
                onAllProvincesFullyInfected?.Invoke();
                AllProvincesFullyInfected?.Invoke();
            }
        }

        private void UpdateGlobalCure(float deltaHours)
        {
            var totalOutposts = 0;
            var activeOutposts = 0;
            var effectiveOutpostFactor = 0f;
            var activeIndex = 0;

            foreach (var state in provinces.Values)
            {
                if (state.OutpostCount <= 0)
                {
                    continue;
                }

                totalOutposts += state.OutpostCount;
                if (state.OutpostDisabled)
                {
                    continue;
                }

                activeOutposts += state.OutpostCount;
                for (int i = 0; i < state.OutpostCount; i++)
                {
                    var multiplier = OutbreakMath.ComputeGlobalOutpostMultiplierForIndex(activeIndex, outpostRates.DiminishingReturnFactor);
                    if (IsUrbanHub(state.RegionId))
                    {
                        multiplier *= urbanHubBonusMultiplier;
                    }

                    effectiveOutpostFactor += multiplier;
                    activeIndex++;
                }
            }

            globalState.TotalOutpostCount = totalOutposts;
            globalState.ActiveOutpostCount = activeOutposts;

            if (deltaHours > 0f && effectiveOutpostFactor > 0f && outpostRates.GlobalCurePerHourPerOutpost > 0f)
            {
                globalState.CureProgress01 = Mathf.Clamp01(
                    globalState.CureProgress01 + outpostRates.GlobalCurePerHourPerOutpost * effectiveOutpostFactor * deltaHours);
            }

            RaiseGlobalStateChanged();

            if (!cureCompleteRaised && Mathf.Approximately(globalState.CureProgress01, 1f))
            {
                cureCompleteRaised = true;
                onCureCompleted?.Invoke();
                CureCompleted?.Invoke();
            }
        }

        public bool TryGetProvinceState(string regionId, out ProvinceInfectionState state)
        {
            state = null;
            if (string.IsNullOrEmpty(regionId))
            {
                return false;
            }

            return provinces.TryGetValue(regionId, out state);
        }

        public bool CanBuildOutpost(string regionId, out int costR, out OutpostBuildError error)
        {
            costR = OutbreakMath.ComputeOutpostCostR(globalState.TotalOutpostCount, costConfig);
            error = OutpostBuildError.None;

            if (string.IsNullOrEmpty(regionId) || !provinces.TryGetValue(regionId, out var state))
            {
                error = OutpostBuildError.InvalidRegion;
                return false;
            }

            if (state.Infection01 >= virusRates.FullyInfectedThreshold01)
            {
                error = OutpostBuildError.ProvinceFullyInfected;
                return false;
            }

            if (globalState.ZarBalance < costR)
            {
                error = OutpostBuildError.NotEnoughZar;
                return false;
            }

            return true;
        }

        public bool TryBuildOutpost(string regionId, out int costR, out OutpostBuildError error)
        {
            if (!CanBuildOutpost(regionId, out costR, out error))
            {
                return false;
            }

            var state = provinces[regionId];
            globalState.ZarBalance -= costR;
            state.OutpostCount++;
            state.OutpostDisabled = state.Infection01 >= virusRates.OutpostDisableThreshold01;
            state.IsFullyInfected = state.Infection01 >= virusRates.FullyInfectedThreshold01;

            RaiseProvinceStateChanged(state);
            UpdateGlobalCure(0f);
            return true;
        }

        private bool IsUrbanHub(string regionId)
        {
            if (urbanHubRegionIds == null)
            {
                return false;
            }

            foreach (var id in urbanHubRegionIds)
            {
                if (string.Equals(id, regionId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RaiseProvinceStateChanged(ProvinceInfectionState state)
        {
            if (state == null)
            {
                return;
            }

            onProvinceStateChanged?.Invoke(state);
            ProvinceStateChanged?.Invoke(state);
        }

        private void RaiseGlobalStateChanged()
        {
            if (globalState == null)
            {
                return;
            }

            onGlobalStateChanged?.Invoke(globalState);
            GlobalStateChanged?.Invoke(globalState);
        }

        [Serializable]
        private class ProvinceStateEvent : UnityEvent<ProvinceInfectionState> { }

        [Serializable]
        private class GlobalStateEvent : UnityEvent<GlobalCureState> { }
    }
}
