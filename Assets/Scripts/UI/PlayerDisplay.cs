using UnityEngine;
using UnityEngine.UI;
using TMPro; // If using TextMeshPro

public class PlayerDisplay : MonoBehaviour
{
    public TextMeshProUGUI scoreText; 
    public Image playerIcon;

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
}
