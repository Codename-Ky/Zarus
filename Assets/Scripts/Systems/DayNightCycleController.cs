using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Zarus.Map;

namespace Zarus.Systems
{
    /// <summary>
    /// Drives the accelerated in-game clock and coordinates lighting/emission changes.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class DayNightCycleController : MonoBehaviour
    {
        private const float MinutesPerDay = 1440f;

        [Header("Time Settings")]
        [SerializeField, Min(0.1f)]
        private float timeScale = 30f;

        [SerializeField]
        private Vector2Int startYearRange = new(1994, 2024);

        [SerializeField]
        private Vector2Int startMonthRange = new(1, 12);

        [SerializeField]
        private Vector2Int startDayRange = new(1, 28);

        [SerializeField]
        private Vector2 startTimeMinutesRange = new(360f, 1080f);

        [SerializeField]
        private bool randomizeStartTime = true;

        [SerializeField, Range(0f, 0.3f)]
        private float dawnDurationNormalized = 0.083f;

        [SerializeField, Range(0f, 0.3f)]
        private float duskDurationNormalized = 0.083f;

        [Header("Lighting")]
        [SerializeField]
        private Light sunLight;

        [SerializeField]
        private Light moonLight;

        [SerializeField]
        private Camera volumeCamera;

        [SerializeField]
        private Volume globalVolume;

        [SerializeField]
        private VolumeProfile defaultVolumeProfile;

        [SerializeField]
        private float sunAzimuth = 35f;

        [SerializeField]
        private AnimationCurve sunElevationCurve = new(
            new Keyframe(0f, 0f),
            new Keyframe(0.25f, 0.8f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.75f, 0.8f),
            new Keyframe(1f, 0f));

        [SerializeField]
        private AnimationCurve sunIntensityCurve = new(
            new Keyframe(0f, 0f),
            new Keyframe(0.2f, 0.5f),
            new Keyframe(0.5f, 1f),
            new Keyframe(0.8f, 0.5f),
            new Keyframe(1f, 0f));

        [SerializeField]
        private AnimationCurve moonIntensityCurve = new(
            new Keyframe(0f, 0.8f),
            new Keyframe(0.5f, 0f),
            new Keyframe(1f, 0.8f));

        [SerializeField]
        private Gradient ambientColor = new()
        {
            colorKeys = new[]
            {
                new GradientColorKey(new Color(0.02f, 0.05f, 0.13f), 0f),
                new GradientColorKey(new Color(0.9f, 0.9f, 0.8f), 0.5f),
                new GradientColorKey(new Color(0.04f, 0.05f, 0.12f), 1f)
            },
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        };

        [SerializeField]
        private Volume dayVolume;

        [SerializeField]
        private Volume nightVolume;

        [Header("Map Emission & Effects")]
        [SerializeField]
        private RegionMapController mapController;

        [SerializeField]
        private NightLightsEffect nightLightsEffect;

        [SerializeField, Range(0f, 1f)]
        private float nightStartNormalized = 0.78f;

        [SerializeField, Range(0f, 1f)]
        private float nightEndNormalized = 0.22f;

        [SerializeField]
        private float dayEmissionStrength = 0.05f;

        [SerializeField]
        private float nightEmissionStrength = 0.7f;

        [Header("Events")]
        [SerializeField]
        private UnityEvent<InGameTimeSnapshot> onTimeChanged = new();

        public event Action<InGameTimeSnapshot> TimeUpdated;

        private InGameTimeSnapshot currentSnapshot;
        private DateTime currentDate;
        private float minutesIntoDay;
        private int currentDayIndex = 1;
        private bool initialized;

        public InGameTimeSnapshot CurrentTime => currentSnapshot;
        public bool HasTime => initialized;
        public UnityEvent<InGameTimeSnapshot> OnTimeChanged => onTimeChanged;
        public float TimeScale => timeScale;
        public float NormalizedTimeOfDay => initialized ? minutesIntoDay / MinutesPerDay : 0f;

        private void Awake()
        {
            EnsureDependencies();
        }

        private void Start()
        {
            InitializeTime();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                if (initialized)
                {
                    ApplyLighting(currentSnapshot);
                }

                return;
            }

            if (!initialized)
            {
                return;
            }

            var scaledDeltaMinutes = Time.deltaTime * Mathf.Max(0.01f, timeScale);
            AdvanceTime(scaledDeltaMinutes);
        }

        public void RestartCycle(bool randomizeDate = true)
        {
            InitializeTime(randomizeDate);
        }

        public void SetNormalizedTime(float normalized)
        {
            normalized = Mathf.Repeat(normalized, 1f);
            EnsureDependencies();
            if (!initialized)
            {
                InitializeTime();
            }

            minutesIntoDay = normalized * MinutesPerDay;
            UpdateSnapshot(true);
        }

        private void InitializeTime(bool randomizeDate = true)
        {
            EnsureDependencies();
            currentDate = GenerateStartDate(randomizeDate);
            minutesIntoDay = randomizeStartTime ? GetRandomStartMinutes() : 0f;
            currentDayIndex = 1;
            initialized = true;
            UpdateSnapshot(true);
        }

        private void EnsureDependencies()
        {
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }

            if (sunLight == null)
            {
                sunLight = AutoCreateLight("Dynamic Sun", LightType.Directional, new Color(1f, 0.956f, 0.839f));
            }

            if (moonLight == null)
            {
                moonLight = AutoCreateLight("Dynamic Moon", LightType.Directional, new Color(0.65f, 0.75f, 0.9f));
            }

            if (nightLightsEffect == null)
            {
                nightLightsEffect = FindFirstObjectByType<NightLightsEffect>();
                if (nightLightsEffect == null)
                {
                    var lightsGo = new GameObject("NightLightsEffect");
                    lightsGo.transform.SetParent(transform, false);
                    nightLightsEffect = lightsGo.AddComponent<NightLightsEffect>();
                }
            }

            if (volumeCamera == null)
            {
                volumeCamera = Camera.main;
            }

            if (volumeCamera != null)
            {
                var additionalData = volumeCamera.GetComponent<UniversalAdditionalCameraData>();
                if (additionalData != null)
                {
                    if (additionalData.volumeTrigger == null)
                    {
                        additionalData.volumeTrigger = volumeCamera.transform;
                    }

                    if (additionalData.volumeLayerMask.value == 0)
                    {
                        additionalData.volumeLayerMask = LayerMask.GetMask("Default");
                    }

                    additionalData.renderPostProcessing = true;
                    additionalData.requiresColorTexture = true;
                    additionalData.requiresDepthTexture = true;
                }
            }

            if (globalVolume == null)
            {
                globalVolume = FindFirstObjectByType<Volume>();
            }

            if (globalVolume == null)
            {
                var volumeGO = new GameObject("Global Volume");
                volumeGO.transform.SetParent(transform, false);
                globalVolume = volumeGO.AddComponent<Volume>();
            }

            if (globalVolume.transform.parent != transform)
            {
                globalVolume.transform.SetParent(transform, false);
            }

            globalVolume.isGlobal = true;
            globalVolume.priority = 0f;
            globalVolume.gameObject.layer = LayerMask.NameToLayer("Default");
            globalVolume.hideFlags = Application.isPlaying ? HideFlags.DontSave : HideFlags.DontSaveInEditor;

            var resolvedProfile = ResolveVolumeProfile();
            if (defaultVolumeProfile != null)
            {
                globalVolume.sharedProfile = defaultVolumeProfile;
            }
            else if (globalVolume.sharedProfile == null)
            {
                globalVolume.profile = resolvedProfile;
            }
        }

        private VolumeProfile ResolveVolumeProfile()
        {
            if (defaultVolumeProfile != null)
            {
                return defaultVolumeProfile;
            }

            if (globalVolume != null)
            {
                if (globalVolume.sharedProfile != null)
                {
                    return globalVolume.sharedProfile;
                }

                if (globalVolume.profile != null)
                {
                    return globalVolume.profile;
                }
            }

            var runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "RuntimeVolumeProfile";

            if (!runtimeProfile.TryGet<ColorAdjustments>(out var colorAdjustments))
            {
                colorAdjustments = runtimeProfile.Add<ColorAdjustments>();
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.postExposure.value = 0f;
                colorAdjustments.saturation.overrideState = true;
                colorAdjustments.saturation.value = -5f;
            }

            if (!runtimeProfile.TryGet<Bloom>(out var bloom))
            {
                bloom = runtimeProfile.Add<Bloom>();
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.25f;
                bloom.scatter.overrideState = true;
                bloom.scatter.value = 0.6f;
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 1.2f;
            }

            if (!runtimeProfile.TryGet<Vignette>(out var vignette))
            {
                vignette = runtimeProfile.Add<Vignette>();
                vignette.intensity.overrideState = true;
                vignette.intensity.value = 0.15f;
                vignette.smoothness.overrideState = true;
                vignette.smoothness.value = 0.5f;
            }

            return runtimeProfile;
        }

        private void AdvanceTime(float minutes)
        {
            if (minutes <= 0f)
            {
                return;
            }

            minutesIntoDay += minutes;

            while (minutesIntoDay >= MinutesPerDay)
            {
                minutesIntoDay -= MinutesPerDay;
                currentDate = currentDate.AddDays(1);
                currentDayIndex++;
            }

            UpdateSnapshot(false);
        }

        private void UpdateSnapshot(bool forceEvent)
        {
            var normalized = Mathf.Repeat(minutesIntoDay / MinutesPerDay, 1f);
            var segment = EvaluateSegment(normalized);
            currentSnapshot = new InGameTimeSnapshot(
                currentDate.Date.AddMinutes(minutesIntoDay),
                currentDayIndex,
                minutesIntoDay,
                normalized,
                segment);

            ApplyLighting(currentSnapshot);
            NotifyListeners(forceEvent);
        }

        private void ApplyLighting(InGameTimeSnapshot snapshot)
        {
            var normalized = snapshot.NormalizedTimeOfDay;
            var sunElevation = sunElevationCurve.Evaluate(normalized);
            var sunIntensity = Mathf.Max(0f, sunIntensityCurve.Evaluate(normalized));
            var moonIntensity = Mathf.Max(0f, moonIntensityCurve.Evaluate(normalized));
            var dayFactor = Mathf.Clamp01(sunIntensity);
            var nightFactor = 1f - dayFactor;

            if (sunLight != null)
            {
                sunLight.transform.rotation = Quaternion.Euler(sunElevation * 180f - 90f, sunAzimuth, 0f);
                sunLight.intensity = sunIntensity;
                sunLight.enabled = sunIntensity > 0.01f;
            }

            if (moonLight != null)
            {
                moonLight.transform.rotation = Quaternion.Euler(180f - (sunElevation * 180f - 90f), sunAzimuth + 180f, 0f);
                moonLight.intensity = moonIntensity * nightFactor;
                moonLight.enabled = moonLight.intensity > 0.02f;
            }

            if (dayVolume != null)
            {
                dayVolume.weight = dayFactor;
            }

            if (nightVolume != null)
            {
                nightVolume.weight = nightFactor;
            }

            RenderSettings.ambientLight = ambientColor.Evaluate(normalized);

            if (mapController != null)
            {
                var emission = Mathf.Lerp(dayEmissionStrength, nightEmissionStrength, nightFactor);
                mapController.SetGlobalEmissionMultiplier(emission);
            }

            if (nightLightsEffect != null)
            {
                nightLightsEffect.ApplyIntensity(nightFactor);
            }
        }

        private void NotifyListeners(bool forceEvent)
        {
            if (!initialized && !forceEvent)
            {
                return;
            }

            onTimeChanged?.Invoke(currentSnapshot);
            TimeUpdated?.Invoke(currentSnapshot);
        }

        private DateTime GenerateStartDate(bool randomize)
        {
            var nowYear = DateTime.Now.Year;
            var yearMin = Mathf.Clamp(startYearRange.x, 1994, nowYear);
            var yearMax = Mathf.Clamp(startYearRange.y, yearMin, nowYear);
            var year = randomize ? UnityEngine.Random.Range(yearMin, yearMax + 1) : yearMin;

            var monthMin = Mathf.Clamp(startMonthRange.x, 1, 12);
            var monthMax = Mathf.Clamp(startMonthRange.y, monthMin, 12);
            var month = randomize ? UnityEngine.Random.Range(monthMin, monthMax + 1) : monthMin;

            var maxDayInMonth = DateTime.DaysInMonth(year, month);
            var dayMin = Mathf.Clamp(startDayRange.x, 1, maxDayInMonth);
            var dayMax = Mathf.Clamp(startDayRange.y, dayMin, maxDayInMonth);
            var day = randomize ? UnityEngine.Random.Range(dayMin, dayMax + 1) : dayMin;

            return new DateTime(year, month, day);
        }

        private float GetRandomStartMinutes()
        {
            var min = Mathf.Clamp(startTimeMinutesRange.x, 0f, MinutesPerDay);
            var max = Mathf.Clamp(startTimeMinutesRange.y, min, MinutesPerDay);
            return UnityEngine.Random.Range(min, max);
        }

        private InGameTimeSnapshot.DaySegment EvaluateSegment(float normalized)
        {
            var night = normalized >= nightStartNormalized || normalized < nightEndNormalized;
            if (night)
            {
                var duskStart = Mathf.Repeat(nightStartNormalized - duskDurationNormalized, 1f);
                if (IsWithinRange(normalized, duskStart, duskDurationNormalized))
                {
                    return InGameTimeSnapshot.DaySegment.Dusk;
                }

                var dawnStart = nightEndNormalized;
                if (IsWithinRange(normalized, dawnStart, dawnDurationNormalized))
                {
                    return InGameTimeSnapshot.DaySegment.Dawn;
                }

                return InGameTimeSnapshot.DaySegment.Night;
            }

            var duskStartDay = Mathf.Repeat(nightStartNormalized - duskDurationNormalized, 1f);
            if (IsWithinRange(normalized, duskStartDay, duskDurationNormalized))
            {
                return InGameTimeSnapshot.DaySegment.Dusk;
            }

            var dawnStartDay = nightEndNormalized;
            if (IsWithinRange(normalized, dawnStartDay, dawnDurationNormalized))
            {
                return InGameTimeSnapshot.DaySegment.Dawn;
            }

            return InGameTimeSnapshot.DaySegment.Day;
        }

        private static bool IsWithinRange(float value, float rangeStart, float length)
        {
            if (length <= 0f)
            {
                return false;
            }

            value = Mathf.Repeat(value, 1f);
            rangeStart = Mathf.Repeat(rangeStart, 1f);
            var end = rangeStart + length;
            if (end <= 1f)
            {
                return value >= rangeStart && value < end;
            }

            return value >= rangeStart || value < Mathf.Repeat(end, 1f);
        }

        private Light AutoCreateLight(string name, LightType type, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            return light;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapIfMissing()
        {
            if (UnityEngine.Object.FindFirstObjectByType<DayNightCycleController>() == null)
            {
                var go = new GameObject("DayNightCycle");
                go.AddComponent<DayNightCycleController>();
            }
        }
    }
}
