using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlayerDisplayManager : MonoBehaviour
{
    public static PlayerDisplayManager Instance { get; private set; }

    [Header("Prefabs and References")]
    public GameObject playerDisplayPrefab;
    public Transform displayPlane; 

    private Dictionary<ulong, PlayerDisplay> playerDisplays = new Dictionary<ulong, PlayerDisplay>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool IsReady()
    {
        return playerDisplayPrefab != null && displayPlane != null;
    }

    public bool HasPlayerDisplay(ulong clientId)
    {
        return playerDisplays.ContainsKey(clientId);
    }

    public List<ulong> GetDisplayedClientIds()
    {
        return playerDisplays.Keys.ToList();
    }

    public void CreatePlayerDisplay(ulong clientId, int initialScore)
    {
        if (playerDisplays.ContainsKey(clientId))
        {
            Debug.LogWarning($"PlayerDisplay for {clientId} already exists.");
            return;
        }

        GameObject displayObj = Instantiate(playerDisplayPrefab, displayPlane);
        PlayerDisplay display = displayObj.GetComponent<PlayerDisplay>();
        if (display != null)
        {
            display.SetScore(initialScore); // Initialize score with the provided value
            playerDisplays[clientId] = display;
            RefreshDisplayLayout();
        }
        else
        {
            Debug.LogError("PlayerDisplay prefab does not have a PlayerDisplay component.");
        }
    }

    public void UpdatePlayerScore(ulong clientId, int newScore)
    {
        if (playerDisplays.TryGetValue(clientId, out PlayerDisplay display))
        {
            display.SetScore(newScore);
        }
        else
        {
            Debug.LogWarning($"PlayerDisplay for {clientId} not found.");
        }
    }

    public void RemovePlayerDisplay(ulong clientId)
    {
        if (playerDisplays.TryGetValue(clientId, out PlayerDisplay display))
        {
            Destroy(display.gameObject);
            playerDisplays.Remove(clientId);
            Debug.Log($"[PlayerDisplayManager] Removed display for clientId: {clientId}");
            RefreshDisplayLayout();
        }
        else
        {
            Debug.LogWarning($"PlayerDisplay for {clientId} not found.");
        }
    }

    public void SetIconColor(ulong clientId, Color color)
    {
        if (playerDisplays.TryGetValue(clientId, out PlayerDisplay display))
        {
            display.SetColor(color);
        }
        else
        {
            Debug.LogWarning($"Player display for {clientId} not found.");
        }
    }

    private void RefreshDisplayLayout()
    {
        if (displayPlane == null)
        {
            return;
        }

        ResponsiveGridSizer sizer = displayPlane.GetComponent<ResponsiveGridSizer>();
        if (sizer != null)
        {
            sizer.Recalculate();
        }
    }
}
