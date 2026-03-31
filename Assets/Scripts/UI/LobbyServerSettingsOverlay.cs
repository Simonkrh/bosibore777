using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyServerSettingsOverlay : MonoBehaviour
{
    private const int RowSlotCount = 11;

    private enum Tab
    {
        General,
        Spawn,
        Bomb,
        Minigun,
        Lazer,
        HomingMissile
    }

    private sealed class FieldDescriptor
    {
        public Tab TabId;
        public ServerSettingsFieldId FieldId;
        public string Label;
        public float Step;
        public Func<ServerGameSettingsState, string> Formatter;
        public Func<float, float> DeltaToServerDelta;

        public FieldDescriptor(
            Tab tabId,
            ServerSettingsFieldId fieldId,
            string label,
            float step,
            Func<ServerGameSettingsState, string> formatter,
            Func<float, float> deltaToServerDelta = null)
        {
            TabId = tabId;
            FieldId = fieldId;
            Label = label;
            Step = step;
            Formatter = formatter;
            DeltaToServerDelta = deltaToServerDelta ?? (delta => delta);
        }
    }

    private sealed class RowView
    {
        public GameObject Root;
        public TMP_Text Label;
        public TMP_Text Value;
        public Button MinusButton;
        public Button PlusButton;
        public FieldDescriptor Descriptor;
    }

    private static readonly FieldDescriptor[] fields =
    {
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.MazeMinSize, "Maze Min Size", 1f, s => s.MazeMinSize.ToString()),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.MazeMaxSize, "Maze Max Size", 1f, s => s.MazeMaxSize.ToString()),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.MazeWallRemovalPercent, "Maze Openings", 0.05f, s => $"{s.MazeWallRemovalPercent * 100f:0}%"),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.PlayerMoveSpeed, "Player Move Speed", 0.1f, s => $"{s.PlayerMoveSpeed:0.00}"),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.PlayerProjectileSpeed, "Standard Shot Speed", 0.1f, s => $"{s.PlayerProjectileSpeed:0.00}"),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.AbilityInitialSpawnDelaySeconds, "Initial Spawn Delay", 0.25f, s => $"{s.AbilityInitialSpawnDelaySeconds:0.00}s"),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.AbilityMinSpawnIntervalSeconds, "Ability Min Interval", 0.25f, s => $"{s.AbilityMinSpawnIntervalSeconds:0.00}s"),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.AbilityMaxSpawnIntervalSeconds, "Ability Max Interval", 0.25f, s => $"{s.AbilityMaxSpawnIntervalSeconds:0.00}s"),
        new FieldDescriptor(Tab.General, ServerSettingsFieldId.AbilityBlockedTileRadius, "Pickup Safe Radius", 1f, s => $"{s.AbilityBlockedTileRadius} tile(s)"),
        new FieldDescriptor(Tab.Spawn, ServerSettingsFieldId.BombSpawnPercent, "Bomb", 1f, s => FormatSpawnShare(s.BombSpawnPercent), ConvertSpawnShareDeltaToServerDelta),
        new FieldDescriptor(Tab.Spawn, ServerSettingsFieldId.MinigunSpawnPercent, "Minigun", 1f, s => FormatSpawnShare(s.MinigunSpawnPercent), ConvertSpawnShareDeltaToServerDelta),
        new FieldDescriptor(Tab.Spawn, ServerSettingsFieldId.LazerSpawnPercent, "Lazer", 1f, s => FormatSpawnShare(s.LazerSpawnPercent), ConvertSpawnShareDeltaToServerDelta),
        new FieldDescriptor(Tab.Spawn, ServerSettingsFieldId.HomingMissileSpawnPercent, "Homing Missile", 1f, s => FormatSpawnShare(s.HomingMissileSpawnPercent), ConvertSpawnShareDeltaToServerDelta),
        new FieldDescriptor(Tab.Bomb, ServerSettingsFieldId.BombProjectileSpeed, "Bomb Speed", 0.1f, s => $"{s.BombProjectileSpeed:0.00}"),
        new FieldDescriptor(Tab.Bomb, ServerSettingsFieldId.BombShardCount, "Explosion Shards", 2f, s => s.BombShardCount.ToString()),
        new FieldDescriptor(Tab.Bomb, ServerSettingsFieldId.BombShardSpeed, "Shard Speed", 0.1f, s => $"{s.BombShardSpeed:0.00}"),
        new FieldDescriptor(Tab.Minigun, ServerSettingsFieldId.MinigunSpreadDegrees, "Spread", 1f, s => $"{s.MinigunSpreadDegrees:0} deg"),
        new FieldDescriptor(Tab.Minigun, ServerSettingsFieldId.MinigunBulletCount, "Bullet Count", 1f, s => s.MinigunBulletCount.ToString()),
        new FieldDescriptor(Tab.Minigun, ServerSettingsFieldId.MinigunFiringDurationSeconds, "Firing Duration", 0.1f, s => $"{s.MinigunFiringDurationSeconds:0.0}s"),
        new FieldDescriptor(Tab.Minigun, ServerSettingsFieldId.MinigunBulletSpeed, "Bullet Speed", 0.1f, s => $"{s.MinigunBulletSpeed:0.00}"),
        new FieldDescriptor(Tab.Minigun, ServerSettingsFieldId.MinigunChargeUpSeconds, "Charge Time", 0.05f, s => $"{s.MinigunChargeUpSeconds:0.00}s"),
        new FieldDescriptor(Tab.Minigun, ServerSettingsFieldId.MinigunClearAfterSeconds, "Clear Delay", 0.05f, s => $"{s.MinigunClearAfterSeconds:0.00}s"),
        new FieldDescriptor(Tab.Lazer, ServerSettingsFieldId.LazerProjectedLength, "Preview Length", 0.5f, s => $"{s.LazerProjectedLength:0.0}"),
        new FieldDescriptor(Tab.Lazer, ServerSettingsFieldId.LazerProjectileSpeed, "Beam Speed", 1f, s => $"{s.LazerProjectileSpeed:0}"),
        new FieldDescriptor(Tab.Lazer, ServerSettingsFieldId.LazerMaxDistance, "Max Distance", 0.5f, s => $"{s.LazerMaxDistance:0.0}"),
        new FieldDescriptor(Tab.HomingMissile, ServerSettingsFieldId.HomingMissileSpeed, "Missile Speed", 0.1f, s => $"{s.HomingMissileSpeed:0.00}"),
        new FieldDescriptor(Tab.HomingMissile, ServerSettingsFieldId.HomingMissileHomingDelaySeconds, "Homing Delay", 0.1f, s => $"{s.HomingMissileHomingDelaySeconds:0.0}s"),
        new FieldDescriptor(Tab.HomingMissile, ServerSettingsFieldId.HomingMissileTurnRateDegreesPerSecond, "Turn Rate", 10f, s => $"{s.HomingMissileTurnRateDegreesPerSecond:0} deg/s"),
        new FieldDescriptor(Tab.HomingMissile, ServerSettingsFieldId.HomingMissileCornerTurnRateMultiplier, "Corner Turn Multiplier", 0.05f, s => $"{s.HomingMissileCornerTurnRateMultiplier:0.00}")
    };

    private readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
    private readonly List<RowView> rowViews = new List<RowView>(RowSlotCount);
    private readonly List<FieldDescriptor> visibleFields = new List<FieldDescriptor>(RowSlotCount);

    private Button launcherButton;
    private TMP_Text launcherButtonLabel;
    private TMP_Text launcherStatusLabel;
    private GameObject modalRoot;
    private TMP_Text modalStatusLabel;
    private Button resetButton;
    private Button closeButton;

    private GameManager gameManager;
    private Tab currentTab = Tab.General;
    private bool waitingForLease;
    private bool waitingForRelease;
    private bool isModalOpen;
    private bool externalHostActive;
    private bool missingReferencesLogged;
    private bool listenersRegistered;
    private float nextHeartbeatTime;
    private string currentLauncherButtonText = "Server Settings";
    private string currentLauncherStatusText = string.Empty;
    private bool currentLauncherInteractable = true;

    public bool IsOpen => isModalOpen;
    public bool ShouldStayAliveWithoutLobbyUi => externalHostActive;
    public string CurrentLauncherButtonText => currentLauncherButtonText;
    public string CurrentLauncherStatusText => currentLauncherStatusText;
    public bool CurrentLauncherInteractable => currentLauncherInteractable;

    private void Awake()
    {
        if (CacheReferences())
        {
            RegisterListeners();
        }
    }

    private void Update()
    {
        if (gameManager == null || !externalHostActive)
        {
            return;
        }

        if (!isModalOpen && !waitingForLease && !waitingForRelease)
        {
            return;
        }

        Refresh(gameManager);
    }

    public void Refresh(GameManager currentGameManager)
    {
        gameManager = currentGameManager;
        if (!CacheReferences())
        {
            return;
        }

        RegisterListeners();

        bool valid = gameManager != null && gameManager.IsClient && gameManager.IsSpawned;
        ulong holderId = valid ? gameManager.ServerSettingsEditorClientId : GameManager.NoServerSettingsEditorClientId;
        bool localHolder = valid && gameManager.IsServerSettingsEditorHeldByLocalClient();
        bool heldByOther = holderId != GameManager.NoServerSettingsEditorClientId && !localHolder;

        if (waitingForLease && localHolder)
        {
            waitingForLease = false;
            waitingForRelease = false;
            isModalOpen = true;
            nextHeartbeatTime = 0f;
        }
        else if (waitingForRelease && !localHolder)
        {
            waitingForRelease = false;
        }
        else if (heldByOther || !valid)
        {
            waitingForLease = false;
            waitingForRelease = false;
            if (!localHolder)
            {
                isModalOpen = false;
            }
        }

        if (localHolder && isModalOpen && Time.unscaledTime >= nextHeartbeatTime)
        {
            gameManager.SendServerSettingsEditorHeartbeat();
            nextHeartbeatTime = Time.unscaledTime + 1f;
        }

        string holderName = ResolveHolderName(holderId);
        launcherButton.gameObject.SetActive(valid);
        if (!valid)
        {
            currentLauncherButtonText = "Server Settings";
            currentLauncherStatusText = string.Empty;
            currentLauncherInteractable = false;
            launcherStatusLabel.gameObject.SetActive(false);
            SetModalActive(false);
            return;
        }

        string launcherStatusText;
        if (heldByOther)
        {
            currentLauncherButtonText = "Settings Locked";
            currentLauncherStatusText = $"{holderName} is editing now.";
            currentLauncherInteractable = false;
        }
        else if (waitingForLease)
        {
            currentLauncherButtonText = "Opening...";
            currentLauncherStatusText = "Requesting editor lock...";
            currentLauncherInteractable = false;
        }
        else if (waitingForRelease)
        {
            currentLauncherButtonText = "Server Settings";
            currentLauncherStatusText = string.Empty;
            currentLauncherInteractable = false;
        }
        else if (localHolder)
        {
            currentLauncherButtonText = isModalOpen ? "Close Settings" : "Open Settings";
            currentLauncherStatusText = "You hold the editor lock.";
            currentLauncherInteractable = true;
        }
        else
        {
            currentLauncherButtonText = "Server Settings";
            currentLauncherStatusText = string.Empty;
            currentLauncherInteractable = true;
        }

        launcherButtonLabel.text = currentLauncherButtonText;
        launcherStatusText = currentLauncherStatusText;
        launcherButton.interactable = currentLauncherInteractable;
        launcherStatusLabel.gameObject.SetActive(!string.IsNullOrEmpty(launcherStatusText));
        if (launcherStatusLabel.gameObject.activeSelf)
        {
            launcherStatusLabel.text = launcherStatusText;
        }

        bool showModal = valid && localHolder && isModalOpen;
        SetModalActive(showModal);
        if (!showModal)
        {
            return;
        }

        ServerGameSettingsState settings = gameManager.GetCurrentServerSettings();
        modalStatusLabel.text = BuildModalStatusText(holderName);

        foreach (KeyValuePair<Tab, Button> entry in tabButtons)
        {
            if (entry.Value != null)
            {
                entry.Value.interactable = entry.Key != currentTab;
            }
        }

        visibleFields.Clear();
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].TabId == currentTab)
            {
                visibleFields.Add(fields[i]);
            }
        }

        for (int i = 0; i < rowViews.Count; i++)
        {
            RowView row = rowViews[i];
            bool visible = i < visibleFields.Count;
            row.Root.SetActive(visible);
            if (!visible)
            {
                row.Descriptor = null;
                continue;
            }

            FieldDescriptor descriptor = visibleFields[i];
            row.Descriptor = descriptor;
            row.Label.text = descriptor.Label;
            row.Value.text = descriptor.Formatter(settings);
            row.MinusButton.interactable = localHolder;
            row.PlusButton.interactable = localHolder;
        }

        resetButton.interactable = localHolder;
        closeButton.interactable = true;
    }

    public void HandleLobbyHidden()
    {
        externalHostActive = false;
        ReleaseEditorLockIfHeld();
        waitingForLease = false;
        waitingForRelease = false;
        isModalOpen = false;
        nextHeartbeatTime = 0f;
        if (launcherStatusLabel != null)
        {
            launcherStatusLabel.gameObject.SetActive(false);
        }
        SetModalActive(false);
    }

    public void SetExternalHostActive(bool active)
    {
        externalHostActive = active;
    }

    public void HandleExternalLauncherClicked()
    {
        HandleLauncherClicked();
    }

    public void HandleExternalHostHidden()
    {
        externalHostActive = false;
        HandleCloseClicked();
    }

    public void Dispose()
    {
    }

    private bool CacheReferences()
    {
        if (launcherButton != null &&
            launcherButtonLabel != null &&
            launcherStatusLabel != null &&
            modalRoot != null &&
            modalStatusLabel != null &&
            resetButton != null &&
            closeButton != null &&
            rowViews.Count == RowSlotCount &&
            tabButtons.Count == 6)
        {
            return true;
        }

        launcherButton = FindComponent<Button>("Overlay/Panel/ServerSettingsButton");
        launcherButtonLabel = FindComponent<TMP_Text>("Overlay/Panel/ServerSettingsButton/Label");
        launcherStatusLabel = FindComponent<TMP_Text>("Overlay/Panel/ServerSettingsStatus");
        modalRoot = FindGameObject("ServerSettingsModal");
        modalStatusLabel = FindComponent<TMP_Text>("ServerSettingsModal/Panel/Status");
        resetButton = FindComponent<Button>("ServerSettingsModal/Panel/ResetButton");
        closeButton = FindComponent<Button>("ServerSettingsModal/Panel/CloseButton");

        tabButtons.Clear();
        AddTabButton(Tab.General, "ServerSettingsModal/Panel/Tabs/GeneralTabButton");
        AddTabButton(Tab.Spawn, "ServerSettingsModal/Panel/Tabs/SpawnTabButton");
        AddTabButton(Tab.Bomb, "ServerSettingsModal/Panel/Tabs/BombTabButton");
        AddTabButton(Tab.Minigun, "ServerSettingsModal/Panel/Tabs/MinigunTabButton");
        AddTabButton(Tab.Lazer, "ServerSettingsModal/Panel/Tabs/LazerTabButton");
        AddTabButton(Tab.HomingMissile, "ServerSettingsModal/Panel/Tabs/HomingMissileTabButton");

        rowViews.Clear();
        for (int i = 0; i < RowSlotCount; i++)
        {
            string rowPath = $"ServerSettingsModal/Panel/Rows/Row{(i + 1):00}";
            GameObject rowObject = FindGameObject(rowPath);
            TMP_Text label = FindComponent<TMP_Text>($"{rowPath}/Label");
            TMP_Text value = FindComponent<TMP_Text>($"{rowPath}/Value");
            Button minusButton = FindComponent<Button>($"{rowPath}/MinusButton");
            Button plusButton = FindComponent<Button>($"{rowPath}/PlusButton");
            if (rowObject == null || label == null || value == null || minusButton == null || plusButton == null)
            {
                return LogMissingReferences();
            }

            rowViews.Add(new RowView
            {
                Root = rowObject,
                Label = label,
                Value = value,
                MinusButton = minusButton,
                PlusButton = plusButton
            });
        }

        if (launcherButton == null ||
            launcherButtonLabel == null ||
            launcherStatusLabel == null ||
            modalRoot == null ||
            modalStatusLabel == null ||
            resetButton == null ||
            closeButton == null ||
            tabButtons.Count != 6)
        {
            return LogMissingReferences();
        }

        return true;
    }

    private void RegisterListeners()
    {
        if (listenersRegistered)
        {
            return;
        }

        launcherButton.onClick.AddListener(HandleLauncherClicked);
        resetButton.onClick.AddListener(HandleResetClicked);
        closeButton.onClick.AddListener(HandleCloseClicked);

        tabButtons[Tab.General].onClick.AddListener(HandleGeneralTabClicked);
        tabButtons[Tab.Spawn].onClick.AddListener(HandleSpawnTabClicked);
        tabButtons[Tab.Bomb].onClick.AddListener(HandleBombTabClicked);
        tabButtons[Tab.Minigun].onClick.AddListener(HandleMinigunTabClicked);
        tabButtons[Tab.Lazer].onClick.AddListener(HandleLazerTabClicked);
        tabButtons[Tab.HomingMissile].onClick.AddListener(HandleHomingMissileTabClicked);

        for (int i = 0; i < rowViews.Count; i++)
        {
            int rowIndex = i;
            rowViews[i].MinusButton.onClick.AddListener(() => HandleAdjust(rowIndex, -1f));
            rowViews[i].PlusButton.onClick.AddListener(() => HandleAdjust(rowIndex, 1f));
        }

        listenersRegistered = true;
    }

    private void AddTabButton(Tab tab, string path)
    {
        Button button = FindComponent<Button>(path);
        if (button != null)
        {
            tabButtons[tab] = button;
        }
    }

    private void HandleGeneralTabClicked()
    {
        SetCurrentTab(Tab.General);
    }

    private void HandleBombTabClicked()
    {
        SetCurrentTab(Tab.Bomb);
    }

    private void HandleSpawnTabClicked()
    {
        SetCurrentTab(Tab.Spawn);
    }

    private void HandleMinigunTabClicked()
    {
        SetCurrentTab(Tab.Minigun);
    }

    private void HandleLazerTabClicked()
    {
        SetCurrentTab(Tab.Lazer);
    }

    private void HandleHomingMissileTabClicked()
    {
        SetCurrentTab(Tab.HomingMissile);
    }

    private void SetCurrentTab(Tab tab)
    {
        currentTab = tab;
    }

    private void HandleLauncherClicked()
    {
        if (gameManager == null)
        {
            return;
        }

        if (gameManager.IsServerSettingsEditorHeldByLocalClient())
        {
            waitingForRelease = false;
            if (isModalOpen)
            {
                HandleCloseClicked();
            }
            else
            {
                isModalOpen = true;
                nextHeartbeatTime = 0f;
            }

            return;
        }

        waitingForLease = true;
        waitingForRelease = false;
        gameManager.RequestAcquireServerSettingsEditor();
    }

    private void HandleAdjust(int rowIndex, float direction)
    {
        if (gameManager == null ||
            !gameManager.IsServerSettingsEditorHeldByLocalClient() ||
            rowIndex < 0 ||
            rowIndex >= rowViews.Count)
        {
            return;
        }

        FieldDescriptor descriptor = rowViews[rowIndex].Descriptor;
        if (descriptor == null)
        {
            return;
        }

        float delta = direction * descriptor.Step;
        gameManager.AdjustServerSettingsField(descriptor.FieldId, descriptor.DeltaToServerDelta(delta));
    }

    private void HandleResetClicked()
    {
        if (gameManager != null && gameManager.IsServerSettingsEditorHeldByLocalClient())
        {
            gameManager.ResetServerSettingsToDefaults();
        }
    }

    private void HandleCloseClicked()
    {
        waitingForRelease = ReleaseEditorLockIfHeld();
        waitingForLease = false;
        isModalOpen = false;
        nextHeartbeatTime = 0f;
        if (launcherStatusLabel != null)
        {
            launcherStatusLabel.gameObject.SetActive(false);
        }
        SetModalActive(false);
    }

    private bool ReleaseEditorLockIfHeld()
    {
        if (gameManager != null && gameManager.IsServerSettingsEditorHeldByLocalClient())
        {
            gameManager.ReleaseLocalServerSettingsEditor();
            return true;
        }

        return false;
    }

    private void SetModalActive(bool shouldBeActive)
    {
        if (modalRoot != null && modalRoot.activeSelf != shouldBeActive)
        {
            modalRoot.SetActive(shouldBeActive);
        }
    }

    private string ResolveHolderName(ulong holderClientId)
    {
        if (holderClientId == GameManager.NoServerSettingsEditorClientId || gameManager == null)
        {
            return "Nobody";
        }

        if (gameManager.IsClient && gameManager.NetworkManager != null && holderClientId == gameManager.NetworkManager.LocalClientId)
        {
            return "You";
        }

        string displayName = gameManager.GetPlayerDisplayName(holderClientId);
        return string.IsNullOrWhiteSpace(displayName) ? "Another player" : displayName;
    }

    private string BuildModalStatusText(string holderName)
    {
        if (currentTab == Tab.Spawn)
        {
            return $"Live server rules. Editor lock: {holderName}.";
        }

        return $"Live server rules. Editor lock: {holderName}.";
    }

    private static string FormatSpawnShare(float configurableSpawnPercent)
    {
        return $"{ServerGameSettingsState.ConfigurableSpawnPercentToSharePercent(configurableSpawnPercent):0.0}%";
    }

    private static float ConvertSpawnShareDeltaToServerDelta(float shareDelta)
    {
        return ServerGameSettingsState.SpawnShareDeltaToConfigurableSpawnPercentDelta(shareDelta);
    }

    private GameObject FindGameObject(string path)
    {
        Transform target = transform.Find(path);
        return target != null ? target.gameObject : null;
    }

    private T FindComponent<T>(string path) where T : Component
    {
        Transform target = transform.Find(path);
        return target != null ? target.GetComponent<T>() : null;
    }

    private bool LogMissingReferences()
    {
        if (!missingReferencesLogged)
        {
            Debug.LogWarning("[LobbyServerSettingsOverlay] Missing one or more authored UI references in LobbyPage.prefab.");
            missingReferencesLogged = true;
        }

        return false;
    }
}
