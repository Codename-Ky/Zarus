#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Zarus.Systems.Editor
{
    /// <summary>
    /// Editor-only tooling to drive the day/night cycle for quick visual testing.
    /// </summary>
    public class DayNightCycleDevPanel : EditorWindow
    {
        private const string WindowTitle = "Day/Night Dev Tools";
        private DayNightCycleController controller;
        private float sliderValue = 0f;
        private bool autoApply = true;
        private bool syncWithController = true;

        [MenuItem("Zarus/Time Tools/Day-Night Dev Panel")]
        public static void ShowWindow()
        {
            GetWindow<DayNightCycleDevPanel>(WindowTitle);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Day/Night Cycle", EditorStyles.boldLabel);
            controller = (DayNightCycleController)EditorGUILayout.ObjectField("Controller", controller, typeof(DayNightCycleController), true);

            if (controller == null)
            {
                EditorGUILayout.HelpBox("Select a DayNightCycleController in the scene or drag it here to enable controls.", MessageType.Info);
                if (GUILayout.Button("Find In Scene"))
                {
                    controller = FindObjectOfType<DayNightCycleController>();
                }
                return;
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Current Snapshot", EditorStyles.boldLabel);
                if (controller.HasTime)
                {
                    var snapshot = controller.CurrentTime;
                    EditorGUILayout.LabelField("Day", snapshot.DayIndex.ToString());
                    EditorGUILayout.LabelField("Clock", snapshot.DateTime.ToString("HH:mm"));
                    EditorGUILayout.LabelField("Date", snapshot.DateTime.ToString("ddd, dd MMM yyyy"));
                    EditorGUILayout.LabelField("Segment", snapshot.Segment.ToString());
                }
                else
                {
                    EditorGUILayout.HelpBox("Cycle has not started yet. Enter Play mode or restart the controller.", MessageType.Warning);
                }
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("Time Scrubber", EditorStyles.boldLabel);
                autoApply = EditorGUILayout.ToggleLeft("Auto Apply", autoApply);
                syncWithController = EditorGUILayout.ToggleLeft("Sync Slider With Controller", syncWithController);

                if (controller.HasTime && syncWithController)
                {
                    sliderValue = controller.NormalizedTimeOfDay;
                }

                EditorGUI.BeginChangeCheck();
                var newSliderValue = EditorGUILayout.Slider("Normalized Time", sliderValue, 0f, 1f);
                if (EditorGUI.EndChangeCheck())
                {
                    sliderValue = newSliderValue;
                    if (autoApply)
                    {
                        ApplySlider();
                    }
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Dawn")) ApplyPreset(0.2f);
                if (GUILayout.Button("Noon")) ApplyPreset(0.5f);
                if (GUILayout.Button("Dusk")) ApplyPreset(0.8f);
                if (GUILayout.Button("Midnight")) ApplyPreset(0.98f);
                EditorGUILayout.EndHorizontal();

                if (!autoApply)
                {
                    if (GUILayout.Button("Apply Slider"))
                    {
                        ApplySlider();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Restart Day"))
                {
                    controller?.RestartCycle();
                }

                if (GUILayout.Button("Randomize Start"))
                {
                    controller?.RestartCycle(true);
                }
            }
        }

        private void ApplyPreset(float value)
        {
            sliderValue = value;
            ApplySlider();
        }

        private void ApplySlider()
        {
            if (controller == null)
            {
                return;
            }

            controller.SetNormalizedTime(sliderValue);
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
#endif
