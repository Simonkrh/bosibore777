using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InGameMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject inGameMenuPanel;
    [SerializeField] private MenuDisplaySettings settingsMenu;

    [Header("Scene")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Input")]
    [SerializeField] private bool allowEscapeToggle = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    private void Update()
    {
        if (!allowEscapeToggle)
        {
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            ToggleMenu();
        }
    }

    public void OpenMenu()
    {
        if (inGameMenuPanel != null)
        {
            inGameMenuPanel.SetActive(true);
        }
    }

    public void CloseMenu()
    {
        if (inGameMenuPanel != null)
        {
            inGameMenuPanel.SetActive(false);
        }
    }

    public void ToggleMenu()
    {
        if (inGameMenuPanel == null)
        {
            return;
        }

        inGameMenuPanel.SetActive(!inGameMenuPanel.activeSelf);
    }

    public void OpenSettings()
    {
        if (settingsMenu == null)
        {
            settingsMenu = FindFirstObjectByType<MenuDisplaySettings>();
        }

        if (settingsMenu == null)
        {
            Debug.LogWarning("[InGameMenuController] MenuDisplaySettings was not found.");
            return;
        }

        settingsMenu.OpenSettings();
    }

    public void ReturnToMainMenu()
    {
        if (Application.isBatchMode)
        {
            Debug.LogWarning("[InGameMenuController] ReturnToMainMenu is not valid in batch mode.");
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
        {
            networkManager.Shutdown();
        }

        SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
    }
}
