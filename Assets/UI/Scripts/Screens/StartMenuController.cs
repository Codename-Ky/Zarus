using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zarus.UI
{
    /// <summary>
    /// Controls the Start scene menu.
    /// Handles button interactions for starting the game, tutorial visibility, and exiting.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class StartMenuController : MonoBehaviour
    {
        [Header("Scenes")]
        [SerializeField]
        private string gameplaySceneName = "Main";

        [Header("Element Names")]
        [SerializeField]
        private string tutorialPanelName = "TutorialPanel";

        [SerializeField]
        private string settingsPanelHostName = "SettingsPanelHost";

        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement tutorialPanel;
        private VisualElement settingsPanelHost;
        private Button startButton;
        private Button tutorialButton;
        private Button tutorialCloseButton;
        private Button settingsButton;
        private Button exitButton;
        private bool isInitialized;

        private void Awake()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            TryInitialize();
        }

        private void Start()
        {
            TryInitialize();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
        }

        private void TryInitialize()
        {
            if (isInitialized)
            {
                return;
            }

            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                return;
            }

            root = uiDocument.rootVisualElement;
            tutorialPanel = root.Q<VisualElement>(tutorialPanelName);
            settingsPanelHost = root.Q<VisualElement>(settingsPanelHostName);

            startButton = root.Q<Button>("StartButton");
            tutorialButton = root.Q<Button>("TutorialButton");
            tutorialCloseButton = root.Q<Button>("CloseTutorialButton");
            settingsButton = root.Q<Button>("SettingsButton");
            exitButton = root.Q<Button>("ExitButton");

            RegisterCallbacks();
            isInitialized = true;
        }

        private void RegisterCallbacks()
        {
            if (startButton != null)
            {
                startButton.clicked += OnStartClicked;
            }

            if (tutorialButton != null)
            {
                tutorialButton.clicked += ShowTutorial;
            }

            if (tutorialCloseButton != null)
            {
                tutorialCloseButton.clicked += HideTutorial;
            }

            if (settingsButton != null)
            {
                settingsButton.clicked += OnSettingsClicked;
            }

            if (exitButton != null)
            {
                exitButton.clicked += QuitGame;
            }
        }

        private void UnregisterCallbacks()
        {
            if (startButton != null)
            {
                startButton.clicked -= OnStartClicked;
            }

            if (tutorialButton != null)
            {
                tutorialButton.clicked -= ShowTutorial;
            }

            if (tutorialCloseButton != null)
            {
                tutorialCloseButton.clicked -= HideTutorial;
            }

            if (settingsButton != null)
            {
                settingsButton.clicked -= OnSettingsClicked;
            }

            if (exitButton != null)
            {
                exitButton.clicked -= QuitGame;
            }
        }

        private void OnStartClicked()
        {
            if (string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.LogWarning("[StartMenu] Gameplay scene name not set.");
                return;
            }

            SceneManager.LoadScene(gameplaySceneName);
        }

        private void ShowTutorial()
        {
            tutorialPanel?.RemoveFromClassList("hidden");
        }

        private void HideTutorial()
        {
            tutorialPanel?.AddToClassList("hidden");
        }

        private void OnSettingsClicked()
        {
            Debug.Log("[StartMenu] Settings clicked. Placeholder panel will be replaced once shared settings exist.");
            if (settingsPanelHost == null)
            {
                return;
            }

            bool isHidden = settingsPanelHost.ClassListContains("hidden");
            if (isHidden)
            {
                settingsPanelHost.RemoveFromClassList("hidden");
            }
            else
            {
                settingsPanelHost.AddToClassList("hidden");
            }
        }

        private void QuitGame()
        {
            Debug.Log("[StartMenu] Exit clicked.");
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
