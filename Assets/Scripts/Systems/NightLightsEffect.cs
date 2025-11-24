using System.Collections.Generic;
using UnityEngine;
using Zarus.Map;

namespace Zarus.Systems
{
    /// <summary>
    /// Spawns lightweight point lights across the country to mimic city illumination at night.
    /// </summary>
    public class NightLightsEffect : MonoBehaviour
    {
        private const float DefaultHeightOffset = 1.5f;

        [SerializeField]
        private RegionMapController mapController;

        [SerializeField, Range(1, 10)]
        private int lightsPerRegion = 4;

        [SerializeField]
        private float localRadius = 0.75f;

        [SerializeField]
        private Color lightColor = new(1f, 0.82f, 0.62f);

        [SerializeField]
        private Vector2 intensityRange = new(0.4f, 1.2f);

        [SerializeField]
        private float lightRange = 4f;

        private readonly List<LightInstance> lightPool = new();

        private void Awake()
        {
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }
        }

        private void Start()
        {
            BuildLights();
            ApplyIntensity(0f);
        }

        private void OnDestroy()
        {
            ClearLights();
        }

        public void ApplyIntensity(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);
            foreach (var instance in lightPool)
            {
                if (instance.Light == null)
                {
                    continue;
                }

                var targetIntensity = Mathf.Lerp(0f, instance.MaxIntensity, normalized);
                instance.Light.intensity = targetIntensity;
                instance.Light.enabled = targetIntensity > 0.01f;
            }
        }

        [ContextMenu("Rebuild Lights")]
        public void RebuildLights()
        {
            BuildLights();
        }

        private void BuildLights()
        {
            ClearLights();
            if (mapController == null || mapController.Entries == null)
            {
                return;
            }

            foreach (var entry in mapController.Entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var center = mapController.GetWorldPosition(entry.Centroid);
                for (int i = 0; i < lightsPerRegion; i++)
                {
                    var offset = Random.insideUnitCircle * localRadius;
                    var point = center + new Vector3(offset.x, offset.y, DefaultHeightOffset);
                    var go = new GameObject($"NightLight_{entry.RegionId}_{i}")
                    {
                        hideFlags = HideFlags.DontSave
                    };

                    go.transform.SetParent(transform, false);
                    go.transform.position = point;

                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = lightColor;
                    light.range = lightRange;
                    light.shadows = LightShadows.None;
                    light.intensity = 0f;

                    lightPool.Add(new LightInstance
                    {
                        Light = light,
                        MaxIntensity = Random.Range(intensityRange.x, intensityRange.y)
                    });
                }
            }
        }

        private void ClearLights()
        {
            foreach (var instance in lightPool)
            {
                if (instance.Light == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(instance.Light.gameObject);
                }
                else
                {
                    DestroyImmediate(instance.Light.gameObject);
                }
            }

            lightPool.Clear();
        }

        private struct LightInstance
        {
            public Light Light;
            public float MaxIntensity;
        }
    }
}
