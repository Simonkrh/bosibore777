using UnityEngine;
using System.Collections.Generic;

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
        }
        else
        {
            Debug.LogWarning($"PlayerDisplay for {clientId} not found.");
        }
    }
}
