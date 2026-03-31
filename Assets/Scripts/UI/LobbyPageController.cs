using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPageController : MonoBehaviour
{
    private enum ButtonVisualStyle
    {
        Orange,
        Green,
        Blue,
        Red
    }

    private const string OrangeButtonResourcePath = "Sprites/UI/Buttons/OrangeButton";
    private const string GreenButtonResourcePath = "Sprites/UI/Buttons/GreenButton";
    private const string BlueButtonResourcePath = "Sprites/UI/Buttons/BlueButton";
    private const string RedButtonResourcePath = "Sprites/UI/Buttons/RedButton";

    [SerializeField] private GameObject contentRoot;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_InputField nameInputField;
    [SerializeField] private Image colorPreviewImage;
    [SerializeField] private Button randomizeColorButton;
    [SerializeField] private RectTransform playerListContainer;
    [SerializeField] private LobbyPlayerListItem playerListItemPrefab;
    [SerializeField] private TMP_Text emptyPlayerListLabel;
    [SerializeField] private Button primaryActionButton;
    [SerializeField] private TMP_Text primaryActionButtonLabel;
    [SerializeField] private float playerRowHeight = 62f;
    [SerializeField] private float playerRowSpacing = 8f;

    private readonly List<ulong> playerIdsBuffer = new List<ulong>();
    private readonly Dictionary<ulong, LobbyPlayerListItem> playerListItems = new Dictionary<ulong, LobbyPlayerListItem>();

    private GameManager gameManager;
    private bool suppressNameInputCallback;
    private static Sprite orangeButtonSprite;
    private static Sprite greenButtonSprite;
    private static Sprite blueButtonSprite;
    private static Sprite redButtonSprite;

    private void Awake()
    {
        if (primaryActionButton != null)
        {
            primaryActionButton.onClick.AddListener(HandlePrimaryActionClicked);
        }

        if (randomizeColorButton != null)
        {
            randomizeColorButton.onClick.AddListener(HandleRandomizeColorClicked);
        }

        if (nameInputField != null)
        {
            nameInputField.characterLimit = 18;
            nameInputField.onEndEdit.AddListener(HandleNameInputSubmitted);
        }
    }

    private void OnDestroy()
    {
        if (primaryActionButton != null)
        {
            primaryActionButton.onClick.RemoveListener(HandlePrimaryActionClicked);
        }

        if (randomizeColorButton != null)
        {
            randomizeColorButton.onClick.RemoveListener(HandleRandomizeColorClicked);
        }

        if (nameInputField != null)
        {
            nameInputField.onEndEdit.RemoveListener(HandleNameInputSubmitted);
        }

        foreach (KeyValuePair<ulong, LobbyPlayerListItem> entry in playerListItems)
        {
            if (entry.Value != null)
            {
                Destroy(entry.Value.gameObject);
            }
        }

        playerListItems.Clear();
    }

    public void Initialize(GameManager sourceGameManager)
    {
        gameManager = sourceGameManager;
    }

    private void Update()
    {
        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        if (contentRoot == null)
        {
            return;
        }

        bool shouldShow =
            gameManager != null &&
            gameManager.IsClient &&
            gameManager.ShouldShowLobbyUi &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsClient;

        if (contentRoot.activeSelf != shouldShow)
        {
            contentRoot.SetActive(shouldShow);
        }

        if (!shouldShow)
        {
            return;
        }

        RefreshUi();
    }

    private void RefreshUi()
    {
        if (gameManager == null)
        {
            return;
        }

        bool hasLocalClient = NetworkManager.Singleton != null;
        ulong localClientId = hasLocalClient ? NetworkManager.Singleton.LocalClientId : 0;
        bool canJoinCurrentGame = gameManager.CanLocalClientJoinCurrentGame();
        bool hasLocalParticipant = hasLocalClient && gameManager.HasLobbyParticipant(localClientId);
        bool isLocalReady = hasLocalParticipant && gameManager.IsLobbyParticipantReady(localClientId);

        if (titleLabel != null)
        {
            if (gameManager.CurrentFlowState == GameManager.MatchFlowState.Countdown)
            {
                titleLabel.text = "Match Starting";
            }
            else if (canJoinCurrentGame)
            {
                titleLabel.text = "Game In Progress";
            }
            else
            {
                titleLabel.text = "Lobby";
            }
        }

        if (statusLabel != null)
        {
            statusLabel.text = BuildStatusText(canJoinCurrentGame);
        }

        RefreshProfileEditor(hasLocalParticipant);
        RefreshPlayerList(localClientId, hasLocalClient);

        if (primaryActionButton == null || primaryActionButtonLabel == null)
        {
            return;
        }

        primaryActionButton.interactable = hasLocalParticipant;
        ButtonVisualStyle primaryActionStyle = ButtonVisualStyle.Orange;
        if (!hasLocalParticipant)
        {
            primaryActionButtonLabel.text = "Connecting...";
        }
        else if (canJoinCurrentGame)
        {
            primaryActionButtonLabel.text = "Join";
            primaryActionStyle = ButtonVisualStyle.Blue;
        }
        else
        {
            primaryActionButtonLabel.text = isLocalReady ? "Unready" : "Ready";
            primaryActionStyle = isLocalReady ? ButtonVisualStyle.Orange : ButtonVisualStyle.Green;
        }

        ApplyButtonStyle(primaryActionButton, primaryActionStyle);
    }

    private string BuildStatusText(bool canJoinCurrentGame)
    {
        int participantCount = gameManager.LobbyParticipantCount;
        int readyCount = gameManager.ReadyLobbyParticipantCount;

        if (participantCount <= 0)
        {
            return "Waiting for players to join...";
        }

        if (gameManager.CurrentFlowState == GameManager.MatchFlowState.Countdown)
        {
            float countdownRemaining = gameManager.GetLobbyCountdownSecondsRemaining();
            return $"All players ready. Starting in {Mathf.CeilToInt(countdownRemaining)}...";
        }

        if (canJoinCurrentGame)
        {
            return "A match is already running. Press join to enter the current game.";
        }

        if (gameManager.CurrentFlowState == GameManager.MatchFlowState.InGame)
        {
            return "Waiting for the current game to finish or for players to join it.";
        }

        return $"Press ready to start. {readyCount}/{participantCount} ready.";
    }

    private void RefreshProfileEditor(bool hasLocalParticipant)
    {
        string localPlayerName = gameManager != null
            ? gameManager.GetLocalPreferredPlayerName()
            : PlayerProfileStore.DefaultPlayerName;
        Color localPlayerColor = gameManager != null
            ? gameManager.GetLocalPreferredPlayerColor()
            : Color.white;

        if (nameInputField != null)
        {
            nameInputField.interactable = hasLocalParticipant;
            if (!nameInputField.isFocused &&
                !string.Equals(nameInputField.text, localPlayerName, System.StringComparison.Ordinal))
            {
                suppressNameInputCallback = true;
                nameInputField.SetTextWithoutNotify(localPlayerName);
                suppressNameInputCallback = false;
            }
        }

        if (colorPreviewImage != null)
        {
            colorPreviewImage.color = localPlayerColor;
        }

        if (randomizeColorButton != null)
        {
            randomizeColorButton.interactable = hasLocalParticipant;
            ApplyButtonStyle(randomizeColorButton, ButtonVisualStyle.Blue);
        }
    }

    private void RefreshPlayerList(ulong localClientId, bool hasLocalClient)
    {
        if (playerListContainer == null || playerListItemPrefab == null || gameManager == null)
        {
            if (emptyPlayerListLabel != null)
            {
                emptyPlayerListLabel.gameObject.SetActive(true);
            }

            return;
        }

        playerIdsBuffer.Clear();
        gameManager.GetLobbyParticipantIds(playerIdsBuffer);
        playerIdsBuffer.Sort();

        if (emptyPlayerListLabel != null)
        {
            emptyPlayerListLabel.gameObject.SetActive(playerIdsBuffer.Count <= 0);
        }

        HashSet<ulong> activePlayerIds = new HashSet<ulong>();
        for (int i = 0; i < playerIdsBuffer.Count; i++)
        {
            ulong playerId = playerIdsBuffer[i];
            activePlayerIds.Add(playerId);

            bool isLocalPlayer = hasLocalClient && playerId == localClientId;
            if (!playerListItems.TryGetValue(playerId, out LobbyPlayerListItem item) || item == null)
            {
                item = Instantiate(playerListItemPrefab, playerListContainer);
                playerListItems[playerId] = item;
            }

            string displayName = gameManager.GetPlayerDisplayName(playerId);
            if (isLocalPlayer)
            {
                displayName = $"{displayName} (You)";
            }

            item.SetDisplay(
                gameManager.GetPlayerDisplayColor(playerId),
                displayName,
                ResolvePlayerStatusText(playerId, isLocalPlayer),
                isLocalPlayer);

            RectTransform rowTransform = item.transform as RectTransform;
            if (rowTransform != null)
            {
                rowTransform.anchorMin = new Vector2(0f, 1f);
                rowTransform.anchorMax = new Vector2(1f, 1f);
                rowTransform.pivot = new Vector2(0.5f, 1f);
                rowTransform.anchoredPosition = new Vector2(0f, -i * (playerRowHeight + playerRowSpacing));
                rowTransform.sizeDelta = new Vector2(0f, playerRowHeight);
            }
        }

        List<ulong> stalePlayerIds = new List<ulong>();
        foreach (KeyValuePair<ulong, LobbyPlayerListItem> entry in playerListItems)
        {
            if (!activePlayerIds.Contains(entry.Key))
            {
                if (entry.Value != null)
                {
                    Destroy(entry.Value.gameObject);
                }

                stalePlayerIds.Add(entry.Key);
            }
        }

        for (int i = 0; i < stalePlayerIds.Count; i++)
        {
            playerListItems.Remove(stalePlayerIds[i]);
        }

        float contentHeight = playerIdsBuffer.Count <= 0
            ? 0f
            : (playerIdsBuffer.Count * playerRowHeight) + ((playerIdsBuffer.Count - 1) * playerRowSpacing);
        Vector2 containerSize = playerListContainer.sizeDelta;
        containerSize.y = contentHeight;
        playerListContainer.sizeDelta = containerSize;
    }

    private string ResolvePlayerStatusText(ulong playerId, bool isLocalPlayer)
    {
        if (gameManager.IsGameplayParticipant(playerId))
        {
            return "READY - IN GAME";
        }

        if (gameManager.CurrentFlowState == GameManager.MatchFlowState.Countdown &&
            gameManager.IsLobbyParticipantReady(playerId))
        {
            return "READY - STARTING";
        }

        if (gameManager.IsLobbyParticipantReady(playerId))
        {
            return "READY";
        }

        if (gameManager.CurrentFlowState == GameManager.MatchFlowState.InGame)
        {
            return isLocalPlayer ? "WAITING | CAN JOIN" : "WAITING IN LOBBY";
        }

        return "WAITING";
    }

    private void HandlePrimaryActionClicked()
    {
        if (gameManager == null || NetworkManager.Singleton == null)
        {
            return;
        }

        if (gameManager.CanLocalClientJoinCurrentGame())
        {
            gameManager.RequestLocalClientJoinCurrentGame();
            return;
        }

        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        bool nextReadyState = !gameManager.IsLobbyParticipantReady(localClientId);
        gameManager.SetLocalClientReady(nextReadyState);
    }

    private void HandleNameInputSubmitted(string rawName)
    {
        if (suppressNameInputCallback || gameManager == null)
        {
            return;
        }

        gameManager.SetLocalPreferredPlayerName(rawName);
    }

    private void HandleRandomizeColorClicked()
    {
        if (gameManager == null)
        {
            return;
        }

        gameManager.RandomizeLocalPreferredPlayerColor();
    }

    private static void ApplyButtonStyle(Button button, ButtonVisualStyle style)
    {
        if (button == null)
        {
            return;
        }

        Image targetImage = button.targetGraphic as Image;
        if (targetImage == null)
        {
            targetImage = button.GetComponent<Image>();
        }

        if (targetImage == null)
        {
            return;
        }

        Sprite desiredSprite = ResolveButtonSprite(style);
        if (desiredSprite != null && targetImage.sprite != desiredSprite)
        {
            targetImage.sprite = desiredSprite;
        }
    }

    private static Sprite ResolveButtonSprite(ButtonVisualStyle style)
    {
        switch (style)
        {
            case ButtonVisualStyle.Green:
                if (greenButtonSprite == null)
                {
                    greenButtonSprite = Resources.Load<Sprite>(GreenButtonResourcePath);
                }

                return greenButtonSprite;
            case ButtonVisualStyle.Blue:
                if (blueButtonSprite == null)
                {
                    blueButtonSprite = Resources.Load<Sprite>(BlueButtonResourcePath);
                }

                return blueButtonSprite;
            case ButtonVisualStyle.Red:
                if (redButtonSprite == null)
                {
                    redButtonSprite = Resources.Load<Sprite>(RedButtonResourcePath);
                }

                return redButtonSprite;
            default:
                if (orangeButtonSprite == null)
                {
                    orangeButtonSprite = Resources.Load<Sprite>(OrangeButtonResourcePath);
                }

                return orangeButtonSprite;
        }
    }
}
