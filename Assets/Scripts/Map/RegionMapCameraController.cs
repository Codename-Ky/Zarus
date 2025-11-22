using UnityEngine;
using UnityEngine.InputSystem;

namespace Zarus.Map
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class RegionMapCameraController : MonoBehaviour
    {
        [SerializeField]
        private RegionMapController mapController;

        [SerializeField]
        private float minOrthoSize = 3f;

        [SerializeField]
        private float maxOrthoSize = 18f;

        [SerializeField]
        private float zoomSpeed = 0.15f;

        [SerializeField]
        private float panSpeed = 0.0025f;

        [SerializeField]
        private float focusLerpSpeed = 5f;

        [SerializeField]
        private bool clampToBounds = true;

        [SerializeField]
        private Vector2 clampPadding = new Vector2(0.5f, 0.5f);

        [SerializeField]
        private bool drawDebugBounds;

        private Camera targetCamera;
        private Vector3 targetPosition;
        private float targetOrthoSize;
        private Vector2 previousPointerPosition;
        private bool dragging;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            targetPosition = transform.position;
            targetOrthoSize = targetCamera.orthographicSize;
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
                if (mapController == null)
                {
                    Debug.LogWarning("[RegionMapCameraController] No RegionMapController found in scene. Camera bounds clamping will be disabled.");
                }
            }
        }

        private void LateUpdate()
        {
            HandleZoom();
            HandlePan();
            ApplyCameraState();
        }

        public void FocusOnRegion(RegionEntry entry)
        {
            if (entry == null || mapController == null || targetCamera == null)
            {
                return;
            }

            var worldPos = mapController.GetWorldPosition(entry.Centroid);
            targetPosition = new Vector3(worldPos.x, worldPos.y, transform.position.z);
            
            // Use consistent zoom level for all provinces
            targetOrthoSize = minOrthoSize;
        }

        public void FocusOnRegionById(string regionId)
        {
            if (mapController == null)
            {
                return;
            }

            var entry = mapController.GetEntry(regionId);
            FocusOnRegion(entry);
        }

        private void HandleZoom()
        {
            var scrollDelta = 0f;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                scrollDelta += mouse.scroll.ReadValue().y;
            }

            if (!Mathf.Approximately(scrollDelta, 0f))
            {
                targetOrthoSize = Mathf.Clamp(targetOrthoSize - scrollDelta * zoomSpeed, minOrthoSize, maxOrthoSize);
            }
        }

        private void HandlePan()
        {
            if (targetCamera == null)
            {
                return;
            }

            TryGetPointer(out var pointerPosition, out var dragInputActive);
            if (dragInputActive)
            {
                if (!dragging)
                {
                    dragging = true;
                    previousPointerPosition = pointerPosition;
                }
                else
                {
                    var delta = pointerPosition - previousPointerPosition;
                    previousPointerPosition = pointerPosition;
                    var normalizedDelta = new Vector2(-delta.x / Screen.height, -delta.y / Screen.height);
                    var scaledDelta = new Vector3(normalizedDelta.x * targetCamera.orthographicSize * panSpeed * Screen.height,
                                                  normalizedDelta.y * targetCamera.orthographicSize * panSpeed * Screen.height,
                                                  0f);
                    targetPosition += scaledDelta;
                }
            }
            else
            {
                dragging = false;
            }

            if (clampToBounds && mapController != null)
            {
                targetPosition = ClampPosition(targetPosition);
            }
        }

        private Vector3 ClampPosition(Vector3 desired)
        {
            var bounds = mapController.GetWorldBounds();
            var extents = bounds.extents;
            extents.x = Mathf.Max(0.01f, extents.x - clampPadding.x);
            extents.y = Mathf.Max(0.01f, extents.y - clampPadding.y);
            var min = bounds.center - extents;
            var max = bounds.center + extents;
            desired.x = Mathf.Clamp(desired.x, min.x, max.x);
            desired.y = Mathf.Clamp(desired.y, min.y, max.y);
            return desired;
        }

        private bool TryGetPointer(out Vector2 position, out bool dragActive)
        {
            position = default;
            dragActive = false;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                dragActive = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                var primary = touch.primaryTouch;
                dragActive = primary.press.isPressed;
                if (dragActive)
                {
                    position = primary.position.ReadValue();
                    return true;
                }
            }

            return false;
        }

        private void ApplyCameraState()
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * focusLerpSpeed);
            targetCamera.orthographicSize = Mathf.Lerp(targetCamera.orthographicSize, targetOrthoSize, Time.deltaTime * focusLerpSpeed);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawDebugBounds || mapController == null)
            {
                return;
            }

            var bounds = mapController.GetWorldBounds();
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bounds.center, bounds.size + new Vector3(clampPadding.x * 2f, clampPadding.y * 2f, 0f));
        }
#endif
    }
}
