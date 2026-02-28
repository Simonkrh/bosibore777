using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class JoinServerPage : MonoBehaviour
{
    [Serializable]
    private class SavedServerEntry
    {
        public string Address;
        public int Port;
    }

    [Serializable]
    private class SavedServerCollection
    {
        public List<SavedServerEntry> Entries = new List<SavedServerEntry>();
    }

    [Header("Page Visibility")]
    [SerializeField] private GameObject pageRoot;
    [SerializeField] private GameObject panelToHideWhenOpen;

    [Header("Join Form")]
    [SerializeField] private TMP_InputField addressInput;
    [SerializeField] private TMP_InputField portInput;
    [SerializeField] private string defaultPort = "7777";
    [SerializeField] private bool clearAddressAfterAdd = false;

    [Header("Saved List")]
    [SerializeField] private Transform listContainer;
    [SerializeField] private SavedServerListItem listItemPrefab;
    [SerializeField] private bool autoConfigureVerticalListLayout = true;
    [SerializeField] private float listItemSpacing = 6f;
    [SerializeField] private bool forceChildExpandWidth = true;
    [SerializeField] private bool enforceItemRectLayout = true;
    [SerializeField] private float fallbackItemHeight = 56f;

    [Header("Status (Optional)")]
    [SerializeField] private TMP_Text statusLabel;

    [Header("Networking")]
    [SerializeField] private NetworkUI networkUI;

    private const string SavedServersPrefKey = "join.savedServers";

    private readonly List<SavedServerEntry> savedServers = new List<SavedServerEntry>();
    private readonly List<SavedServerListItem> spawnedItems = new List<SavedServerListItem>();

    private void Awake()
    {
        if (networkUI == null)
        {
            networkUI = FindFirstObjectByType<NetworkUI>();
        }

        if (portInput != null && string.IsNullOrWhiteSpace(portInput.text))
        {
            portInput.text = defaultPort;
        }

        LoadSavedServers();
        EnsureListLayoutConfigured();
        RefreshListUi();
    }

    private void OnEnable()
    {
        AttachNetworkUiStatusListener();
    }

    private void OnDisable()
    {
        DetachNetworkUiStatusListener();
    }

    public void OpenJoinPage()
    {
        if (panelToHideWhenOpen != null)
        {
            panelToHideWhenOpen.SetActive(false);
        }

        ResolvePageRoot().SetActive(true);
    }

    public void CloseJoinPage()
    {
        ResolvePageRoot().SetActive(false);

        if (panelToHideWhenOpen != null)
        {
            panelToHideWhenOpen.SetActive(true);
        }
    }

    public void AddServerFromInput()
    {
        string address = addressInput != null ? addressInput.text : string.Empty;
        string portText = portInput != null ? portInput.text : string.Empty;

        if (!TryParseEndpoint(address, portText, out string normalizedAddress, out int port, out string error))
        {
            SetStatus(error);
            return;
        }

        if (ContainsServer(normalizedAddress, port))
        {
            SetStatus("Server already exists in the list.");
            return;
        }

        savedServers.Add(new SavedServerEntry
        {
            Address = normalizedAddress,
            Port = port
        });

        SortSavedServers();
        SaveSavedServers();
        RefreshListUi();

        if (clearAddressAfterAdd && addressInput != null)
        {
            addressInput.text = string.Empty;
        }

        if (portInput != null)
        {
            portInput.text = port.ToString();
        }

        SetStatus($"Added {normalizedAddress}:{port}");
    }

    public void JoinServerFromInput()
    {
        string address = addressInput != null ? addressInput.text : string.Empty;
        string portText = portInput != null ? portInput.text : string.Empty;

        if (!TryParseEndpoint(address, portText, out string normalizedAddress, out int port, out string error))
        {
            SetStatus(error);
            return;
        }

        JoinEndpoint(normalizedAddress, port);
    }

    public void JoinServerByIndex(int index)
    {
        if (index < 0 || index >= savedServers.Count)
        {
            SetStatus("Selected server no longer exists.");
            return;
        }

        SavedServerEntry entry = savedServers[index];
        JoinEndpoint(entry.Address, entry.Port);
    }

    public void RemoveServerByIndex(int index)
    {
        if (index < 0 || index >= savedServers.Count)
        {
            return;
        }

        string label = savedServers[index].Address + ":" + savedServers[index].Port;
        savedServers.RemoveAt(index);
        SaveSavedServers();
        RefreshListUi();
        SetStatus("Removed " + label);
    }

    public void ClearAllSavedServers()
    {
        savedServers.Clear();
        SaveSavedServers();
        RefreshListUi();
        SetStatus("Cleared saved servers.");
    }

    private void JoinEndpoint(string address, int port)
    {
        if (networkUI == null)
        {
            networkUI = FindFirstObjectByType<NetworkUI>();
        }

        if (networkUI == null)
        {
            SetStatus("NetworkUI not found in scene.");
            return;
        }

        AttachNetworkUiStatusListener();

        bool started = networkUI.StartClientTo(address, port);
        if (!started)
        {
            SetStatus($"Failed to start join to {address}:{port}");
        }
    }

    private bool TryParseEndpoint(string addressText, string portText, out string address, out int port, out string error)
    {
        address = string.Empty;
        port = -1;
        error = string.Empty;

        address = string.IsNullOrWhiteSpace(addressText) ? string.Empty : addressText.Trim();
        if (string.IsNullOrEmpty(address))
        {
            error = "Enter an IP or hostname.";
            return false;
        }

        if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
        {
            error = "Port must be a number between 1 and 65535.";
            return false;
        }

        return true;
    }

    private bool ContainsServer(string address, int port)
    {
        for (int i = 0; i < savedServers.Count; i++)
        {
            SavedServerEntry entry = savedServers[i];
            if (string.Equals(entry.Address, address, StringComparison.OrdinalIgnoreCase) && entry.Port == port)
            {
                return true;
            }
        }

        return false;
    }

    private void SortSavedServers()
    {
        savedServers.Sort((a, b) =>
        {
            int addressCompare = string.Compare(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);
            return addressCompare != 0 ? addressCompare : a.Port.CompareTo(b.Port);
        });
    }

    private void LoadSavedServers()
    {
        savedServers.Clear();

        string raw = PlayerPrefs.GetString(SavedServersPrefKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        SavedServerCollection collection = JsonUtility.FromJson<SavedServerCollection>(raw);
        if (collection == null || collection.Entries == null)
        {
            return;
        }

        for (int i = 0; i < collection.Entries.Count; i++)
        {
            SavedServerEntry entry = collection.Entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.Address) || entry.Port < 1 || entry.Port > 65535)
            {
                continue;
            }

            string trimmedAddress = entry.Address.Trim();
            if (ContainsServer(trimmedAddress, entry.Port))
            {
                continue;
            }

            savedServers.Add(new SavedServerEntry
            {
                Address = trimmedAddress,
                Port = entry.Port
            });
        }

        SortSavedServers();
    }

    private void SaveSavedServers()
    {
        SavedServerCollection collection = new SavedServerCollection
        {
            Entries = new List<SavedServerEntry>(savedServers)
        };

        string serialized = JsonUtility.ToJson(collection);
        PlayerPrefs.SetString(SavedServersPrefKey, serialized);
        PlayerPrefs.Save();
    }

    private void RefreshListUi()
    {
        for (int i = 0; i < spawnedItems.Count; i++)
        {
            if (spawnedItems[i] != null)
            {
                Destroy(spawnedItems[i].gameObject);
            }
        }

        spawnedItems.Clear();

        if (listContainer == null || listItemPrefab == null)
        {
            return;
        }

        EnsureListLayoutConfigured();

        for (int i = 0; i < savedServers.Count; i++)
        {
            SavedServerEntry entry = savedServers[i];
            SavedServerListItem item = Instantiate(listItemPrefab, listContainer, false);
            ConfigureSpawnedItemLayout(item);
            item.Configure(
                i,
                entry.Address + ":" + entry.Port,
                JoinServerByIndex,
                RemoveServerByIndex);
            spawnedItems.Add(item);
        }

        if (listContainer is RectTransform listRect)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(listRect);
        }
    }

    private void SetStatus(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.text = message;
        }

        Debug.Log("[JoinServerPage] " + message);
    }

    private GameObject ResolvePageRoot()
    {
        return pageRoot != null ? pageRoot : gameObject;
    }

    private void AttachNetworkUiStatusListener()
    {
        if (networkUI == null)
        {
            networkUI = FindFirstObjectByType<NetworkUI>();
        }

        if (networkUI == null)
        {
            return;
        }

        networkUI.ClientJoinStatusChanged -= HandleClientJoinStatusChanged;
        networkUI.ClientJoinStatusChanged += HandleClientJoinStatusChanged;
    }

    private void DetachNetworkUiStatusListener()
    {
        if (networkUI == null)
        {
            return;
        }

        networkUI.ClientJoinStatusChanged -= HandleClientJoinStatusChanged;
    }

    private void HandleClientJoinStatusChanged(string status)
    {
        SetStatus(status);
    }

    private void EnsureListLayoutConfigured()
    {
        if (!autoConfigureVerticalListLayout || listContainer == null)
        {
            return;
        }

        RectTransform listRect = listContainer as RectTransform;
        if (listRect == null)
        {
            return;
        }

        VerticalLayoutGroup verticalLayout = listContainer.GetComponent<VerticalLayoutGroup>();
        if (verticalLayout == null)
        {
            verticalLayout = listContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        GridLayoutGroup gridLayout = listContainer.GetComponent<GridLayoutGroup>();
        if (gridLayout != null)
        {
            gridLayout.enabled = false;
        }

        HorizontalLayoutGroup horizontalLayout = listContainer.GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayout != null)
        {
            horizontalLayout.enabled = false;
        }

        verticalLayout.childAlignment = TextAnchor.UpperLeft;
        verticalLayout.childControlWidth = true;
        verticalLayout.childControlHeight = true;
        verticalLayout.childForceExpandWidth = forceChildExpandWidth;
        verticalLayout.childForceExpandHeight = false;
        verticalLayout.spacing = Mathf.Max(0f, listItemSpacing);

        ContentSizeFitter contentSizeFitter = listContainer.GetComponent<ContentSizeFitter>();
        if (contentSizeFitter == null)
        {
            contentSizeFitter = listContainer.gameObject.AddComponent<ContentSizeFitter>();
        }

        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = listContainer.GetComponentInParent<ScrollRect>();
        if (scrollRect != null && scrollRect.content == null)
        {
            scrollRect.content = listRect;
        }
    }

    private void ConfigureSpawnedItemLayout(SavedServerListItem item)
    {
        if (!enforceItemRectLayout || item == null)
        {
            return;
        }

        RectTransform itemRect = item.transform as RectTransform;
        if (itemRect == null)
        {
            return;
        }

        float targetHeight = Mathf.Max(1f, fallbackItemHeight);
        if (listItemPrefab != null)
        {
            RectTransform prefabRect = listItemPrefab.transform as RectTransform;
            if (prefabRect != null && prefabRect.rect.height > 1f)
            {
                targetHeight = prefabRect.rect.height;
            }
        }

        itemRect.anchorMin = new Vector2(0f, 1f);
        itemRect.anchorMax = new Vector2(1f, 1f);
        itemRect.pivot = new Vector2(0.5f, 1f);
        itemRect.anchoredPosition = Vector2.zero;
        itemRect.offsetMin = new Vector2(0f, itemRect.offsetMin.y);
        itemRect.offsetMax = new Vector2(0f, itemRect.offsetMax.y);
        itemRect.sizeDelta = new Vector2(0f, targetHeight);
        itemRect.localScale = Vector3.one;

        LayoutElement layoutElement = item.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = item.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.minHeight = targetHeight;
        layoutElement.preferredHeight = targetHeight;
        layoutElement.flexibleHeight = 0f;
    }
}
