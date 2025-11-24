using UnityEngine;
using UnityEngine.UIElements;

namespace Zarus.UI
{
    /// <summary>
    /// Controls the pause menu screen.
    /// </summary>
    public class PauseMenu : UIScreen
    {
        [Header("Settings Panel")]
        [SerializeField]
        private VisualTreeAsset settingsPanelAsset;

        private Button resumeButton;
        private Button settingsButton;
        private Button quitButton;
        private VisualElement menuPanel;
        private VisualElement settingsPanelHost;
        private SettingsPanelView settingsPanel;

        protected override void Initialize()
        {
            // Query UI elements
            resumeButton = Query<Button>("ResumeButton");
            settingsButton = Query<Button>("SettingsButton");
            quitButton = Query<Button>("QuitButton");
            menuPanel = Query<VisualElement>("MenuPanel");
            settingsPanelHost = Query<VisualElement>("SettingsPanelHost");

            if (settingsPanelHost != null && settingsPanelAsset != null)
            {
                settingsPanel = SettingsPanelView.Create(settingsPanelHost, settingsPanelAsset);
                if (settingsPanel != null)
                {
                    settingsPanel.Closed += OnSettingsClosed;
                }
            }
            else
            {
                Debug.LogWarning("[PauseMenu] Settings panel host or asset missing.");
            }

            // Register button callbacks
            if (resumeButton != null)
            {
                resumeButton.clicked += OnResumeClicked;
            }

            if (settingsButton != null)
            {
                settingsButton.clicked += OnSettingsClicked;
            }

            if (quitButton != null)
            {
                quitButton.clicked += OnQuitClicked;
            }
        }

        protected override void OnShow()
        {
            base.OnShow();

            // Animate panel in
            if (menuPanel != null)
            {
                menuPanel.AddToClassList("slide-up--active");
            }
        }

        protected override void OnHide()
        {
            base.OnHide();

            // Remove animation class
            if (menuPanel != null)
            {
                menuPanel.RemoveFromClassList("slide-up--active");
            }
        }

        private void OnResumeClicked()
        {
            Debug.Log("[PauseMenu] Resume clicked.");
            UIManager.Instance?.Resume();
        }

        private void OnSettingsClicked()
        {
            if (settingsPanel != null)
            {
                settingsPanel.Toggle();
            }
            else
            {
                Debug.LogWarning("[PauseMenu] Settings panel unavailable.");
            }
        }

        private void OnQuitClicked()
        {
            Debug.Log("[PauseMenu] Quit to menu clicked.");
            UIManager.Instance?.ReturnToMenu();
        }

        private void OnDestroy()
        {
            if (settingsPanel != null)
            {
                settingsPanel.Closed -= OnSettingsClosed;
            }
        }

        private void OnSettingsClosed()
        {
            // Reserved hook for future (e.g., focus return)
        }
    }
}
