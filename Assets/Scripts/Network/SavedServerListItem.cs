using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SavedServerListItem : MonoBehaviour
{
    [SerializeField] private TMP_Text endpointLabel;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button removeButton;

    private int itemIndex;
    private Action<int> joinCallback;
    private Action<int> removeCallback;

    public void Configure(int index, string label, Action<int> onJoin, Action<int> onRemove)
    {
        itemIndex = index;
        joinCallback = onJoin;
        removeCallback = onRemove;

        if (endpointLabel != null)
        {
            endpointLabel.text = label;
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(HandleJoinClicked);
            joinButton.onClick.AddListener(HandleJoinClicked);
        }

        if (removeButton != null)
        {
            removeButton.onClick.RemoveListener(HandleRemoveClicked);
            removeButton.onClick.AddListener(HandleRemoveClicked);
        }
    }

    private void HandleJoinClicked()
    {
        joinCallback?.Invoke(itemIndex);
    }

    private void HandleRemoveClicked()
    {
        removeCallback?.Invoke(itemIndex);
    }
}
