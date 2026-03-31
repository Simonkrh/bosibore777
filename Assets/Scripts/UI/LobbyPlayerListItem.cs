using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyPlayerListItem : MonoBehaviour
{
    [SerializeField] private Image background;
    [SerializeField] private Image tankIcon;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text statusLabel;

    public void SetDisplay(Color iconColor, string displayName, string statusText, bool isLocalPlayer)
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

        if (background != null)
        {
            Color backgroundColor = isLocalPlayer
                ? new Color(0.207f, 0.321f, 0.431f, 0.88f)
                : new Color(0.137f, 0.184f, 0.247f, 0.7f);
            background.color = backgroundColor;
        }
    }
}
