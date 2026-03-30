using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPageController : MonoBehaviour
{
    [SerializeField] private GameObject contentRoot;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text playerListLabel;
    [SerializeField] private Button primaryActionButton;
    [SerializeField] private TMP_Text primaryActionButtonLabel;

    private readonly List<ulong> playerIdsBuffer = new List<ulong>();
    private readonly StringBuilder playerListBuilder = new StringBuilder();

    private GameManager gameManager;

    private void Awake()
    {
        if (primaryActionButton != null)
        {
            primaryActionButton.onClick.AddListener(HandlePrimaryActionClicked);
        }
    }

    private void OnDestroy()
    {
        if (primaryActionButton != null)
        {
            primaryActionButton.onClick.RemoveListener(HandlePrimaryActionClicked);
        }
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

        if (playerListLabel != null)
        {
            playerListLabel.text = BuildPlayerListText(localClientId, hasLocalClient);
        }

        if (primaryActionButton == null || primaryActionButtonLabel == null)
        {
            return;
        }

        primaryActionButton.interactable = hasLocalParticipant;
        if (!hasLocalParticipant)
        {
            primaryActionButtonLabel.text = "Connecting...";
        }
        else if (canJoinCurrentGame)
        {
            primaryActionButtonLabel.text = "Join";
        }
        else
        {
            primaryActionButtonLabel.text = isLocalReady ? "Unready" : "Ready";
        }
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

    private string BuildPlayerListText(ulong localClientId, bool hasLocalClient)
    {
        playerIdsBuffer.Clear();
        gameManager.GetLobbyParticipantIds(playerIdsBuffer);
        playerIdsBuffer.Sort();

        if (playerIdsBuffer.Count <= 0)
        {
            return "No players connected.";
        }

        playerListBuilder.Clear();
        for (int i = 0; i < playerIdsBuffer.Count; i++)
        {
            ulong playerId = playerIdsBuffer[i];
            bool isLocalPlayer = hasLocalClient && playerId == localClientId;

            playerListBuilder.Append("Player ");
            playerListBuilder.Append(playerId);

            if (isLocalPlayer)
            {
                playerListBuilder.Append(" (You)");
            }

            playerListBuilder.Append("  ");
            playerListBuilder.Append(ResolvePlayerStatusText(playerId, isLocalPlayer));

            if (i < playerIdsBuffer.Count - 1)
            {
                playerListBuilder.AppendLine();
            }
        }

        return playerListBuilder.ToString();
    }

    private string ResolvePlayerStatusText(ulong playerId, bool isLocalPlayer)
    {
        if (gameManager.IsGameplayParticipant(playerId))
        {
            return "READY | IN GAME";
        }

        if (gameManager.CurrentFlowState == GameManager.MatchFlowState.Countdown &&
            gameManager.IsLobbyParticipantReady(playerId))
        {
            return "READY | STARTING";
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
}
