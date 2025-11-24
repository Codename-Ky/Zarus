using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zarus.UI
{
    /// <summary>
    /// Controls the Game Over / End scene menu.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class EndMenuController : MonoBehaviour
    {
        [Header("Scenes")]
        [SerializeField]
        private string gameplaySceneName = "Main";

        [SerializeField]
        private string startSceneName = "Start";

        private UIDocument uiDocument;
        private VisualElement root;
        private Button restartButton;
        private Button menuButton;
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
            restartButton = root.Q<Button>("RestartButton");
            menuButton = root.Q<Button>("MenuButton");
            exitButton = root.Q<Button>("ExitButton");

            RegisterCallbacks();
            isInitialized = true;
        }

        private void RegisterCallbacks()
        {
            if (restartButton != null)
            {
                restartButton.clicked += RestartGame;
            }

            if (menuButton != null)
            {
                menuButton.clicked += ReturnToMenu;
            }

            if (exitButton != null)
            {
                exitButton.clicked += ExitGame;
            }
        }

        private void UnregisterCallbacks()
        {
            if (restartButton != null)
            {
                restartButton.clicked -= RestartGame;
            }

            if (menuButton != null)
            {
                menuButton.clicked -= ReturnToMenu;
            }

            if (exitButton != null)
            {
                exitButton.clicked -= ExitGame;
            }
        }

        private void RestartGame()
        {
            if (string.IsNullOrEmpty(gameplaySceneName))
            {
                Debug.LogWarning("[EndMenu] Gameplay scene name not set.");
                return;
            }

            SceneManager.LoadScene(gameplaySceneName);
        }

        private void ReturnToMenu()
        {
            if (string.IsNullOrEmpty(startSceneName))
            {
                Debug.LogWarning("[EndMenu] Start scene name not set.");
                return;
            }

            SceneManager.LoadScene(startSceneName);
        }

        private void ExitGame()
        {
            Debug.Log("[EndMenu] Exit clicked.");
    #if UNITY_EDITOR
            EditorApplication.isPlaying = false;
    #else
            Application.Quit();
    #endif
        }
    }
}
