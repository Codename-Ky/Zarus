using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zarus.Map
{
    [DisallowMultipleComponent]
    public class RegionMapController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField]
        private RegionDatabase regionDatabase;

        [SerializeField]
        private TextAsset fallbackGeoJson;

        [Header("Rendering")]
        [SerializeField]
        private Material regionMaterial;

        [SerializeField]
        private Transform regionContainer;

        [SerializeField]
        private float mapScale = 10f;

        [SerializeField]
        private float regionDepth = -0.1f;

        [Header("Animation")]
        [SerializeField]
        [Min(0f)]
        private float colorTransitionDuration = 0.2f;

        [Header("Interaction")]
        [SerializeField]
        private Camera interactionCamera;

        [SerializeField]
        private LayerMask interactionMask = ~0;

        [SerializeField]
        private int regionLayer = 0;

        [SerializeField]
        private RegionMapCameraController autoFocusController;

        [SerializeField]
        private float raycastDistance = 500f;

        [SerializeField]
        private bool enableHover = true;

        [SerializeField]
        private bool enableSelection = true;

        [SerializeField]
        private bool highlightSelection = true;

        [Header("Debug")]
        [SerializeField]
        private bool drawBoundsGizmo = true;

        [SerializeField]
        private Color gizmoColor = new Color(0.66f, 0.86f, 1f, 0.65f);

        [SerializeField]
        private Color gizmoSelectedColor = new Color(1f, 0.8f, 0.2f, 0.75f);

        [Header("Events")]
        [SerializeField]
        private RegionEntryEvent onRegionHovered = new();

        [SerializeField]
        private RegionEntryEvent onRegionSelected = new();

        private readonly List<RegionEntry> runtimeEntries = new();
        private readonly List<RegionRuntime> activeRegions = new();
        private readonly Dictionary<int, RegionRuntime> colliderLookup = new();
        private readonly List<Mesh> runtimeGeneratedMeshes = new();
        private Bounds localBounds;
        private RegionRuntime currentHover;
        private RegionRuntime currentSelection;
        private bool interactionEnabled = true;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
#if UNITY_EDITOR
        private bool pendingRebuild;
#endif

        public IReadOnlyList<RegionEntry> Entries => runtimeEntries;
        public RegionEntryEvent OnRegionHovered => onRegionHovered;
        public RegionEntryEvent OnRegionSelected => onRegionSelected;
        public Bounds LocalBounds => localBounds;
        public float MapScale => mapScale;
        public Transform RegionContainer => regionContainer;

        private void Reset()
        {
            interactionCamera = Camera.main;
        }

        private void Awake()
        {
            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            ResolveEntries();
            BuildRuntimeRegions();
        }

        private void OnDestroy()
        {
            CleanupRuntimeMeshes();
        }

        private void Update()
        {
            UpdateRegionAnimations(Time.deltaTime);

            if (!interactionEnabled)
            {
                return;
            }

            HandlePointer();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!isActiveAndEnabled || pendingRebuild)
            {
                return;
            }

            pendingRebuild = true;
            // Defer GameObject operations to avoid "SendMessage cannot be called during OnValidate" errors
            EditorApplication.delayCall += HandleDeferredRebuild;
        }

        private void HandleDeferredRebuild()
        {
            pendingRebuild = false;
            if (this != null && isActiveAndEnabled)
            {
                ResolveEntries();
                BuildRuntimeRegions();
            }
        }
#endif

        public Bounds GetWorldBounds()
        {
            var scaledCenter = new Vector3(localBounds.center.x * mapScale, localBounds.center.y * mapScale, 0f);
            var scaledSize = new Vector3(localBounds.size.x * mapScale, localBounds.size.y * mapScale, 0.1f);
            var worldCenter = transform.TransformPoint(scaledCenter);
            var worldBounds = new Bounds(worldCenter, scaledSize);
            return worldBounds;
        }

        public Vector3 GetWorldPosition(Vector3 normalizedPosition)
        {
            var scaled = new Vector3(normalizedPosition.x * mapScale, normalizedPosition.y * mapScale, 0f);
            return transform.TransformPoint(scaled);
        }

        public RegionEntry GetEntry(string regionId)
        {
            foreach (var entry in runtimeEntries)
            {
                if (string.Equals(entry.RegionId, regionId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public void Rebuild()
        {
            ResolveEntries();
            BuildRuntimeRegions();
        }

        private void ResolveEntries()
        {
            if (regionDatabase == null)
            {
                regionDatabase = Resources.Load<RegionDatabase>("Map/RegionDatabase");
            }

            runtimeEntries.Clear();
            if (regionDatabase != null && regionDatabase.Regions != null && regionDatabase.Regions.Count > 0)
            {
                runtimeEntries.AddRange(regionDatabase.Regions);
                localBounds = regionDatabase.GlobalBounds;
            }
            else if (fallbackGeoJson != null)
            {
                var geometries = RegionGeometryFactory.ParseGeoJson(fallbackGeoJson.text, out var normalization);
                var (centeredMeshes, centroids) = RegionGeometryFactory.CreateCenteredMeshes(geometries, normalization);
                
                for (int i = 0; i < geometries.Count; i++)
                {
                    var geometry = geometries[i];
                    var mesh = centeredMeshes[i];
                    var centroid = centroids[i];
                    mesh.hideFlags = HideFlags.DontSave;
                    runtimeGeneratedMeshes.Add(mesh);
                    var entry = new RegionEntry();
                    entry.SetRuntimeData(geometry.Id, geometry.Name, mesh, centroid, mesh.bounds);
                    runtimeEntries.Add(entry);
                }

                localBounds = CalculateBounds(runtimeEntries);
            }
            else
            {
                localBounds = new Bounds(Vector3.zero, Vector3.one);
            }
        }

        private static Bounds CalculateBounds(IEnumerable<RegionEntry> entries)
        {
            var hasEntry = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var entry in entries)
            {
                if (!hasEntry)
                {
                    bounds = entry.Bounds;
                    hasEntry = true;
                }
                else
                {
                    bounds.Encapsulate(entry.Bounds);
                }
            }

            if (!hasEntry)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }

            return bounds;
        }

        private void CleanupRuntimeMeshes()
        {
            // Clean up runtime-generated meshes when using fallback GeoJSON
            foreach (var mesh in runtimeGeneratedMeshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
            runtimeGeneratedMeshes.Clear();
        }

        private void BuildRuntimeRegions()
        {
            CleanupRuntimeMeshes();
            EnsureContainer();
            foreach (Transform child in regionContainer)
            {
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            activeRegions.Clear();
            colliderLookup.Clear();
            regionContainer.localScale = new Vector3(mapScale, mapScale, 1f);

            if (runtimeEntries.Count == 0)
            {
                return;
            }

            var material = regionMaterial;
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
                material.name = "RegionFillRuntime";
            }

            foreach (var entry in runtimeEntries)
            {
                if (entry.Mesh == null)
                {
                    continue;
                }

                var regionObject = new GameObject(entry.DisplayName)
                {
                    layer = Mathf.Clamp(regionLayer, 0, 31)
                };
                regionObject.transform.SetParent(regionContainer, false);
                regionObject.transform.localPosition = new Vector3(0f, 0f, regionDepth);
                regionObject.transform.localRotation = Quaternion.identity;

                var meshFilter = regionObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = entry.Mesh;

                var renderer = regionObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var collider = regionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = entry.Mesh;

                var runtime = new RegionRuntime(entry, renderer, collider);
                runtime.UpdateColor(entry.VisualStyle.BaseColor, true, colorTransitionDuration);
                activeRegions.Add(runtime);
                colliderLookup[collider.GetInstanceID()] = runtime;
            }
        }

        private void UpdateRegionAnimations(float deltaTime)
        {
            if (activeRegions.Count == 0)
            {
                return;
            }

            foreach (var region in activeRegions)
            {
                region.TickColorAnimation(deltaTime);
            }
        }

        private void HandlePointer()
        {
            if (!enableHover && !enableSelection)
            {
                return;
            }

            var cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            if (!TryGetPointerPosition(out var pointerPosition, out var pressedThisFrame))
            {
                ClearHover();
                return;
            }

            var ray = cam.ScreenPointToRay(pointerPosition);
            if (Physics.Raycast(ray, out var hitInfo, raycastDistance, interactionMask))
            {
                if (colliderLookup.TryGetValue(hitInfo.collider.GetInstanceID(), out var runtime))
                {
                    if (enableHover && runtime != currentHover)
                    {
                        SetHover(runtime);
                    }

                    if (enableSelection && pressedThisFrame)
                    {
                        SetSelection(runtime);
                    }
                }
            }
            else
            {
                ClearHover();
            }
        }

        private bool TryGetPointerPosition(out Vector2 position, out bool clicked)
        {
            position = default;
            clicked = false;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                clicked = mouse.leftButton.wasPressedThisFrame;
                
                // WebGL fallback: If Input System fails, use legacy Input
                #if UNITY_WEBGL && !UNITY_EDITOR
                if (position == Vector2.zero)
                {
                    position = Input.mousePosition;
                    clicked = Input.GetMouseButtonDown(0);
                }
                #endif
                
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                var primary = touch.primaryTouch;
                if (primary.press.isPressed)
                {
                    position = primary.position.ReadValue();
                    clicked = primary.press.wasPressedThisFrame;
                    return true;
                }
            }
            
            // WebGL ultimate fallback: Use legacy Input system
            #if UNITY_WEBGL && !UNITY_EDITOR
            position = Input.mousePosition;
            clicked = Input.GetMouseButtonDown(0);
            return true;
            #endif

            return false;
        }

        private void SetHover(RegionRuntime runtime)
        {
            if (currentHover == runtime)
            {
                return;
            }

            if (currentHover != null && currentHover != currentSelection)
            {
                currentHover.UpdateColor(currentHover.Entry.VisualStyle.BaseColor, false, colorTransitionDuration);
            }

            currentHover = runtime;
            if (currentHover != null && currentHover != currentSelection)
            {
                currentHover.UpdateColor(currentHover.Entry.VisualStyle.HoverColor, false, colorTransitionDuration);
                onRegionHovered?.Invoke(currentHover.Entry);
            }
        }

        private void ClearHover()
        {
            if (currentHover != null && currentHover != currentSelection)
            {
                currentHover.UpdateColor(currentHover.Entry.VisualStyle.BaseColor, false, colorTransitionDuration);
            }

            currentHover = null;
        }

        private void SetSelection(RegionRuntime runtime)
        {
            if (!highlightSelection)
            {
                onRegionSelected?.Invoke(runtime.Entry);
                autoFocusController?.FocusOnRegion(runtime.Entry);
                return;
            }

            if (currentSelection != null && currentSelection != runtime)
            {
                currentSelection.UpdateColor(currentSelection.Entry.VisualStyle.BaseColor, false, colorTransitionDuration);
            }

            currentSelection = runtime;
            currentSelection?.UpdateColor(currentSelection.Entry.VisualStyle.SelectedColor, false, colorTransitionDuration);
            onRegionSelected?.Invoke(runtime.Entry);
            autoFocusController?.FocusOnRegion(runtime.Entry);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawBoundsGizmo)
            {
                return;
            }

            Gizmos.color = gizmoColor;
            var bounds = GetWorldBounds();
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            if (currentSelection != null)
            {
                Gizmos.color = gizmoSelectedColor;
                var center = GetWorldPosition(currentSelection.Entry.Centroid);
                var size = currentSelection.Entry.Bounds.size * mapScale;
                Gizmos.DrawWireCube(center, new Vector3(size.x, size.y, 0.05f));
            }
        }
#endif

        private void EnsureContainer()
        {
            if (regionContainer != null)
            {
                return;
            }

            var containerGo = new GameObject("RegionContainer");
            containerGo.transform.SetParent(transform, false);
            regionContainer = containerGo.transform;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            if (interactionEnabled == enabled)
            {
                return;
            }

            interactionEnabled = enabled;

            if (!interactionEnabled)
            {
                ClearHover();
            }
        }

        public void SetGlobalEmissionMultiplier(float multiplier)
        {
            var clamped = Mathf.Max(0f, multiplier);
            foreach (var region in activeRegions)
            {
                region.SetEmissionScale(clamped);
            }
        }

        [Serializable]
        public class RegionEntryEvent : UnityEvent<RegionEntry> { }

        private sealed class RegionRuntime
        {
            public RegionEntry Entry { get; }
            public MeshRenderer Renderer { get; }
            public MeshCollider Collider { get; }
            private readonly MaterialPropertyBlock propertyBlock = new();
            private float emissionScale = 0.1f;

            public RegionRuntime(RegionEntry entry, MeshRenderer renderer, MeshCollider collider)
            {
                Entry = entry;
                Renderer = renderer;
                Collider = collider;
            }

            private Color currentColor;
            private Color startColor;
            private Color targetColor;
            private float colorLerpTime;
            private float colorLerpDuration;
            private bool colorAnimating;

            public void UpdateColor(Color color, bool instant, float transitionDuration)
            {
                if (instant || transitionDuration <= 0f)
                {
                    currentColor = color;
                    targetColor = color;
                    colorAnimating = false;
                    ApplyColor(color);
                    return;
                }

                startColor = currentColor;
                targetColor = color;
                colorLerpTime = 0f;
                colorLerpDuration = transitionDuration;
                colorAnimating = true;
            }

            public void TickColorAnimation(float deltaTime)
            {
                if (!colorAnimating)
                {
                    return;
                }

                colorLerpTime += deltaTime;
                var t = Mathf.Clamp01(colorLerpTime / Mathf.Max(colorLerpDuration, Mathf.Epsilon));
                var eased = Mathf.SmoothStep(0f, 1f, t);
                currentColor = Color.Lerp(startColor, targetColor, eased);
                ApplyColor(currentColor);

                if (t >= 1f)
                {
                    colorAnimating = false;
                }
            }

            private void ApplyColor(Color color)
            {
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(EmissionColorId, color * emissionScale);
                Renderer.SetPropertyBlock(propertyBlock);
            }

            public void SetEmissionScale(float scale)
            {
                emissionScale = Mathf.Max(0f, scale);
                ApplyColor(currentColor);
            }
        }
    }
}
