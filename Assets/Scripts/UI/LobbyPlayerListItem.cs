using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

public class LobbyPlayerListItem : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Image tankIcon;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private TMP_Text scoreLabel;
    [SerializeField] private Button decreaseScoreButton;
    [SerializeField] private Button increaseScoreButton;

    private Action decreaseScoreAction;
    private Action increaseScoreAction;

    private void Awake()
    {
        if (decreaseScoreButton != null)
        {
            decreaseScoreButton.onClick.AddListener(HandleDecreaseScoreClicked);
        }

        if (increaseScoreButton != null)
        {
            increaseScoreButton.onClick.AddListener(HandleIncreaseScoreClicked);
        }
    }

    private void OnDestroy()
    {
        if (decreaseScoreButton != null)
        {
            decreaseScoreButton.onClick.RemoveListener(HandleDecreaseScoreClicked);
        }

        if (increaseScoreButton != null)
        {
            increaseScoreButton.onClick.RemoveListener(HandleIncreaseScoreClicked);
        }
    }

    public void SetDisplay(
        Color iconColor,
        string displayName,
        string statusText,
        int score,
        bool isLocalPlayer,
        bool canAdjustScore,
        Action onDecreaseScore,
        Action onIncreaseScore)
    {
        if (tankIcon != null)
        {
            tankIcon.color = iconColor;
        }

        if (nameLabel != null)
        {
            nameLabel.text = displayName;
        }

        if (statusLabel != null)
        {
            statusLabel.text = statusText;
        }

        if (scoreLabel != null)
        {
            scoreLabel.text = score.ToString();
        }

        decreaseScoreAction = canAdjustScore ? onDecreaseScore : null;
        increaseScoreAction = canAdjustScore ? onIncreaseScore : null;

        if (decreaseScoreButton != null)
        {
            decreaseScoreButton.interactable = canAdjustScore;
        }

        if (increaseScoreButton != null)
        {
            increaseScoreButton.interactable = canAdjustScore;
        }

        if (background != null)
        {
            Color backgroundColor = isLocalPlayer
                ? new Color(0.207f, 0.321f, 0.431f, 0.88f)
                : new Color(0.137f, 0.184f, 0.247f, 0.7f);
            background.color = backgroundColor;
        }
    }

    private void HandleDecreaseScoreClicked()
    {
        decreaseScoreAction?.Invoke();
    }

    private void HandleIncreaseScoreClicked()
    {
        increaseScoreAction?.Invoke();
    }
}
