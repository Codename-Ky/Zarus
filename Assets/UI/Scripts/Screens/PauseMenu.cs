using UnityEngine;
using UnityEngine.UIElements;

namespace Zarus.UI
{
    /// <summary>
    /// Controls the pause menu screen.
    /// </summary>
    public class PauseMenu : UIScreen
    {
        private Button resumeButton;
        private Button settingsButton;
        private Button quitButton;
        private VisualElement menuPanel;

        protected override void Initialize()
        {
            // Query UI elements
            resumeButton = Query<Button>("ResumeButton");
            settingsButton = Query<Button>("SettingsButton");
            quitButton = Query<Button>("QuitButton");
            menuPanel = Query<VisualElement>("MenuPanel");

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
            Debug.Log("[PauseMenu] Settings clicked (not yet implemented).");
            // TODO: Implement settings screen
        }

        private void OnQuitClicked()
        {
            Debug.Log("[PauseMenu] Quit to menu clicked.");
            UIManager.Instance?.QuitGame();
        }
    }
}
