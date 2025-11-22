using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zarus.Map
{
    [CreateAssetMenu(menuName = "Zarus/Map/Region Database", fileName = "RegionDatabase")]
    public class RegionDatabase : ScriptableObject
    {
        [SerializeField]
        private List<RegionEntry> regions = new();

        [SerializeField, Tooltip("Optional padding that gets added to the calculated map bounds.")]
        private Vector2 boundsPadding = new Vector2(0.25f, 0.25f);

        private Dictionary<string, RegionEntry> lookup;
        private Bounds globalBounds;

        public IReadOnlyList<RegionEntry> Regions => regions;
        public Bounds GlobalBounds => globalBounds;

        public bool TryGetRegion(string regionId, out RegionEntry entry)
        {
            if (lookup == null || lookup.Count != regions.Count)
            {
                BuildLookup();
            }

            if (lookup != null && lookup.TryGetValue(regionId, out entry))
            {
                return true;
            }

            entry = null;
            return false;
        }

        public IEnumerable<string> GetRegionIds()
        {
            foreach (var entry in regions)
            {
                if (!string.IsNullOrEmpty(entry.RegionId))
                {
                    yield return entry.RegionId;
                }
            }
        }

        public RegionEntry GetEntryAtIndex(int index)
        {
            if (regions == null || index < 0 || index >= regions.Count)
            {
                throw new IndexOutOfRangeException("Region index out of range");
            }

            return regions[index];
        }

        private void OnValidate()
        {
            BuildLookup();
            RecalculateBounds();
        }

        private void BuildLookup()
        {
            lookup = new Dictionary<string, RegionEntry>(StringComparer.OrdinalIgnoreCase);
            if (regions == null)
            {
                return;
            }

            foreach (var entry in regions)
            {
                if (entry == null || string.IsNullOrEmpty(entry.RegionId))
                {
                    continue;
                }

                if (lookup.ContainsKey(entry.RegionId))
                {
                    Debug.LogWarning($"[RegionDatabase] Duplicate region ID detected: '{entry.RegionId}'. Only the last entry will be accessible via lookup.", this);
                }

                lookup[entry.RegionId] = entry;
            }
        }

        private void RecalculateBounds()
        {
            if (regions == null || regions.Count == 0)
            {
                globalBounds = new Bounds(Vector3.zero, Vector3.one);
                return;
            }

            var initialized = false;
            foreach (var entry in regions)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!initialized)
                {
                    globalBounds = entry.Bounds;
                    initialized = true;
                }
                else
                {
                    globalBounds.Encapsulate(entry.Bounds);
                }
            }

            var paddedCenter = globalBounds.center;
            var paddedSize = globalBounds.size + new Vector3(boundsPadding.x * 2f, boundsPadding.y * 2f, 0f);
            globalBounds = new Bounds(paddedCenter, paddedSize);
        }

#if UNITY_EDITOR
        public void ReplaceEntries(List<RegionEntry> newEntries)
        {
            regions = newEntries;
            BuildLookup();
            RecalculateBounds();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    [Serializable]
    public class RegionEntry
    {
        [SerializeField]
        private string regionId;

        [SerializeField]
        private string displayName;

        [SerializeField]
        private Mesh mesh;

        [SerializeField]
        private Vector3 centroid;

        [SerializeField]
        private Bounds bounds;

        [SerializeField]
        private RegionVisualStyle visualStyle = new();

        [SerializeField, TextArea]
        private string description;

        [SerializeField]
        private string externalUrl;

        [SerializeField]
        private Sprite icon;

        [SerializeField]
        private bool allowCameraFocus = true;

        public string RegionId => regionId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? regionId : displayName;
        public Mesh Mesh => mesh;
        public Vector3 Centroid => centroid;
        public Bounds Bounds => bounds;
        public RegionVisualStyle VisualStyle => visualStyle;
        public string Description => description;
        public string ExternalUrl => externalUrl;
        public Sprite Icon => icon;
        public bool AllowCameraFocus => allowCameraFocus;

        public void SetRuntimeData(string id, string display, Mesh regionMesh, Vector3 regionCentroid, Bounds regionBounds)
        {
            regionId = id;
            displayName = display;
            mesh = regionMesh;
            centroid = regionCentroid;
            bounds = regionBounds;
        }

        public void CopyArtistFacingData(RegionEntry other)
        {
            if (other == null)
            {
                return;
            }

            if (other.visualStyle != null)
            {
                visualStyle.CopyFrom(other.visualStyle);
            }

            description = other.description;
            externalUrl = other.externalUrl;
            icon = other.icon;
            allowCameraFocus = other.allowCameraFocus;
        }
    }

    [Serializable]
    public class RegionVisualStyle
    {
        [SerializeField]
        private Color baseColor = new Color(0.11f, 0.59f, 0.68f, 1f);

        [SerializeField]
        private Color hoverColor = Color.white;

        [SerializeField]
        private Color selectedColor = new Color(1f, 0.76f, 0.25f, 1f);

        public Color BaseColor => baseColor;
        public Color HoverColor => hoverColor;
        public Color SelectedColor => selectedColor;

        public void CopyFrom(RegionVisualStyle other)
        {
            if (other == null)
            {
                return;
            }

            baseColor = other.baseColor;
            hoverColor = other.hoverColor;
            selectedColor = other.selectedColor;
        }
    }
}
