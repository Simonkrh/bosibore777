using Unity.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class InGameMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject inGameMenuPanel;
    [SerializeField] private MenuDisplaySettings settingsMenu;

    private Button serverSettingsButton;
    private TMP_Text serverSettingsButtonLabel;
    private TMP_Text serverSettingsStatusLabel;
    private LobbyServerSettingsOverlay serverSettingsOverlay;
    private GameManager gameManager;
    private bool closeMenuWhenServerSettingsOpens;

    [Header("Scene")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Input")]
    [SerializeField] private bool allowEscapeToggle = true;
    [SerializeField] private KeyCode toggleKey = KeyCode.Escape;

    private void Update()
    {
        RefreshServerSettingsLauncher();
        CloseMenuIfServerSettingsOpened();

        if (!allowEscapeToggle)
        {
            return;
        }

        if (Input.GetKeyDown(toggleKey))
        {
            if (IsServerSettingsOpen())
            {
                return;
            }

            ToggleMenu();
        }
    }

    private void OnDestroy()
    {
        if (serverSettingsOverlay != null)
        {
            serverSettingsOverlay.SetExternalHostActive(false);
        }
    }

    public void OpenMenu()
    {
        if (IsServerSettingsOpen())
        {
            return;
        }

        if (inGameMenuPanel != null)
        {
            inGameMenuPanel.SetActive(true);
        }

        RefreshServerSettingsLauncher();
    }

    public void CloseMenu()
    {
        closeMenuWhenServerSettingsOpens = false;
        HideServerSettingsOverlay();

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

        if (inGameMenuPanel.activeSelf)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
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

        HideServerSettingsOverlay();
        settingsMenu.OpenSettings();
    }

    public void ReturnToMainMenu()
    {
        if (Application.isBatchMode)
        {
            Debug.LogWarning("[InGameMenuController] ReturnToMainMenu is not valid in batch mode.");
            return;
        }

        HideServerSettingsOverlay();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && (networkManager.IsServer || networkManager.IsClient))
        {
            networkManager.Shutdown();
        }

        SceneManager.LoadScene(mainMenuSceneName, LoadSceneMode.Single);
    }

    public void HandleServerSettingsClicked()
    {
        EnsureServerSettingsRefs();
        EnsureServerSettingsOverlay();
        if (serverSettingsOverlay == null || gameManager == null)
        {
            return;
        }

        serverSettingsOverlay.SetExternalHostActive(true);
        serverSettingsOverlay.HandleExternalLauncherClicked();
        serverSettingsOverlay.Refresh(gameManager);

        if (serverSettingsOverlay.IsOpen)
        {
            closeMenuWhenServerSettingsOpens = false;
            HideMenuPanelOnly();
        }
        else
        {
            closeMenuWhenServerSettingsOpens = true;
        }

        RefreshServerSettingsLauncher();
    }

    private void RefreshServerSettingsLauncher()
    {
        EnsureServerSettingsRefs();
        if (serverSettingsButton == null || serverSettingsButtonLabel == null || serverSettingsStatusLabel == null)
        {
            return;
        }

        EnsureServerSettingsOverlay();
        bool overlayOpen = serverSettingsOverlay != null && serverSettingsOverlay.IsOpen;
        bool menuVisible = inGameMenuPanel != null && inGameMenuPanel.activeInHierarchy;
        if (!menuVisible)
        {
            if (serverSettingsOverlay != null && overlayOpen && gameManager != null)
            {
                serverSettingsOverlay.SetExternalHostActive(true);
                serverSettingsOverlay.Refresh(gameManager);
            }
            else if (serverSettingsOverlay != null)
            {
                serverSettingsOverlay.SetExternalHostActive(false);
            }

            serverSettingsStatusLabel.gameObject.SetActive(false);
            return;
        }

        if (serverSettingsOverlay == null || gameManager == null)
        {
            serverSettingsButtonLabel.text = "Server Settings";
            serverSettingsButton.interactable = false;
            serverSettingsStatusLabel.gameObject.SetActive(false);
            return;
        }

        serverSettingsOverlay.SetExternalHostActive(true);
        serverSettingsOverlay.Refresh(gameManager);

        serverSettingsButtonLabel.text = serverSettingsOverlay.CurrentLauncherButtonText;
        serverSettingsButton.interactable = serverSettingsOverlay.CurrentLauncherInteractable;

        string statusText = serverSettingsOverlay.CurrentLauncherStatusText;
        serverSettingsStatusLabel.gameObject.SetActive(!string.IsNullOrEmpty(statusText));
        if (serverSettingsStatusLabel.gameObject.activeSelf)
        {
            serverSettingsStatusLabel.text = statusText;
        }
    }

    private void EnsureServerSettingsRefs()
    {
        if (serverSettingsButton == null)
        {
            Transform buttonTransform = transform.Find("ServerSettingsButton");
            if (buttonTransform != null)
            {
                serverSettingsButton = buttonTransform.GetComponent<Button>();
            }
        }

        if (serverSettingsButtonLabel == null)
        {
            Transform labelTransform = transform.Find("ServerSettingsButton/Text");
            if (labelTransform != null)
            {
                serverSettingsButtonLabel = labelTransform.GetComponent<TMP_Text>();
            }
        }

        if (serverSettingsStatusLabel == null)
        {
            Transform statusTransform = transform.Find("ServerSettingsStatus");
            if (statusTransform != null)
            {
                serverSettingsStatusLabel = statusTransform.GetComponent<TMP_Text>();
            }
        }
    }

    private void EnsureServerSettingsOverlay()
    {
        if (serverSettingsOverlay == null)
        {
            serverSettingsOverlay = FindFirstObjectByType<LobbyServerSettingsOverlay>(FindObjectsInactive.Include);
        }

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }
    }

    private void HideServerSettingsOverlay()
    {
        closeMenuWhenServerSettingsOpens = false;
        EnsureServerSettingsOverlay();
        if (serverSettingsOverlay != null)
        {
            serverSettingsOverlay.HandleExternalHostHidden();
        }

        if (serverSettingsStatusLabel != null)
        {
            serverSettingsStatusLabel.gameObject.SetActive(false);
        }
    }

    private void CloseMenuIfServerSettingsOpened()
    {
        if (!closeMenuWhenServerSettingsOpens)
        {
            return;
        }

        EnsureServerSettingsOverlay();
        if (serverSettingsOverlay == null)
        {
            closeMenuWhenServerSettingsOpens = false;
            return;
        }

        if (!serverSettingsOverlay.IsOpen)
        {
            return;
        }

        closeMenuWhenServerSettingsOpens = false;
        HideMenuPanelOnly();
    }

    private void HideMenuPanelOnly()
    {
        if (inGameMenuPanel != null)
        {
            inGameMenuPanel.SetActive(false);
        }

        if (serverSettingsStatusLabel != null)
        {
            serverSettingsStatusLabel.gameObject.SetActive(false);
        }
    }

    private bool IsServerSettingsOpen()
    {
        EnsureServerSettingsOverlay();
        return serverSettingsOverlay != null && serverSettingsOverlay.IsOpen;
    }
}
