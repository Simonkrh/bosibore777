using UnityEngine;
using UnityEngine.UI;
using TMPro; // If using TextMeshPro

public class PlayerDisplay : MonoBehaviour
{
    public TextMeshProUGUI scoreText; 

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
}
