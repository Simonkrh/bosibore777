using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerDisplay : MonoBehaviour
{
    public TextMeshProUGUI scoreText;
    public Image playerIcon;
    [SerializeField] private TextMeshProUGUI playerNameText;

    public void SetScore(int score)
    {
        if (scoreText != null)
        {
            scoreText.text = score.ToString();
        }
        else
        {
            Debug.LogWarning("ScoreText is not assigned in PlayerDisplay.");
        }
    }

    public void SetName(string playerName)
    {
        TextMeshProUGUI resolvedNameText = ResolvePlayerNameText();
        if (resolvedNameText != null)
        {
            resolvedNameText.text = string.IsNullOrWhiteSpace(playerName)
                ? PlayerProfileStore.DefaultPlayerName
                : playerName;
        }
    }

    public void SetColor(Color color)
    {
        if (playerIcon != null)
        {
            playerIcon.color = color;
        }
        else
        {
            Debug.LogWarning("PlayerImage is not assigned in PlayerDisplay.");
        }
    }

    private TextMeshProUGUI ResolvePlayerNameText()
    {
        if (playerNameText != null)
        {
            return playerNameText;
        }

        TextMeshProUGUI[] textComponents = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < textComponents.Length; i++)
        {
            TextMeshProUGUI textComponent = textComponents[i];
            if (textComponent != null && textComponent != scoreText)
            {
                playerNameText = textComponent;
                break;
            }
        }

        return playerNameText;
    }
}
