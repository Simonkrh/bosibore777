using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

public class GameManager : NetworkBehaviour
{
    private enum SoundEffectId
    {
        BulletBounce1 = 0,
        BulletBounce2 = 1,
        BulletDespawn = 2,
        BulletShoot = 3,
        PlayerDie = 4,
        AbilitySpawn = 5,
        AbilityPickup = 6,
        BombExplode = 7,
        LazerShoot = 8,
        MissileShoot = 9,
        MissileLock = 10,
        MissileTarget = 11,
        MinigunStart = 12,
        MinigunFirstBullet = 13,
        MinigunCooldown = 14,
        MegaBombActivate = 15
    }

    private struct ClientDisplayState
    {
        public int Score;
        public Color IconColor;
        public bool HasIconColor;
    }

    private sealed class MinigunAudioSequenceState
    {
        public GameObject RootObject;
        public AudioSource ShotSourceA;
        public AudioSource ShotSourceB;
        public AudioSource ActiveShotSource;
        public Coroutine PendingShotPlaybackCoroutine;
        public Coroutine ShotCrossfadeCoroutine;
        public double FirstShotAllowedDspTime;
        public double NextOneShotEndDspTime;
    }

    private sealed class MegaBombMusicState
    {
        public GameObject RootObject;
        public AudioSource LoopSource;
    }

    public GameObject playerPrefab;
    public MazeGenerator mazeGenerator;
    public GameObject projectilesContainer;

    [Header("Projectile Audio")]
    [SerializeField] private AudioClip bulletBounce1Clip;
    [SerializeField] private AudioClip bulletBounce2Clip;
    [SerializeField] private AudioClip bulletDespawnClip;
    [SerializeField] private AudioClip bulletShootClip;

    [Header("Player Audio")]
    [SerializeField] private AudioClip playerDieClip;

    [Header("General Ability Audio")]
    [SerializeField] private AudioClip abilitySpawnClip;
    [SerializeField] private AudioClip abilityPickupClip;

    [Header("Bomb Audio")]
    [SerializeField] private AudioClip bombExplodeClip;

    [Header("Lazer Audio")]
    [SerializeField] private AudioClip lazerShootClip;

    [Header("Minigun Audio")]
    [SerializeField] private AudioClip minigunStartClip;
    [SerializeField] private AudioClip[] minigunShotClips;
    [SerializeField] private AudioClip minigunCooldownClip;

    [Header("Missile Audio")]
    [SerializeField] private AudioClip missileShootClip;
    [SerializeField] private AudioClip missileLockClip;
    [SerializeField] private AudioClip missileTargetClip;

    [Header("Mega Bomb Audio")]
    [SerializeField] private AudioClip megaBombMusicClip;
    [SerializeField] private AudioClip megaBombActivateClip;

    [Header("Camera Shake")]
    [SerializeField] private float playerDeathShakeAmplitude = 0.06f;
    [SerializeField] private float playerDeathShakeDuration = 0.16f;
    [SerializeField] private float playerDeathShakeFrequency = 22f;
    [SerializeField] private float bombExplosionShakeAmplitude = 0.14f;
    [SerializeField] private float bombExplosionShakeDuration = 0.28f;
    [SerializeField] private float bombExplosionShakeFrequency = 28f;
    [SerializeField] private float megaBombShakeAmplitude = 0.22f;
    [SerializeField] private float megaBombShakeDuration = 0.34f;
    [SerializeField] private float megaBombShakeFrequency = 30f;

    [Header("Audio Settings")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
    private PlayerDisplayManager displayManager;
    private CameraShakeController localCameraShakeController;

    private HashSet<ulong> alivePlayers = new HashSet<ulong>();
    private readonly HashSet<ulong> eliminatedPlayersThisRound = new HashSet<ulong>();

    private Dictionary<ulong, int> playerScores = new Dictionary<ulong, int>();

    private Dictionary<ulong, GameObject> clientIdToPlayer = new Dictionary<ulong, GameObject>();

    private List<Vector2Int> availableCells = new List<Vector2Int>();
    private bool startingNewRound = false;
    private readonly List<Color> primaryColors = new List<Color> { Color.green, Color.red, Color.blue };
    private readonly List<Color> availablePrimaryColors = new List<Color>();
    private readonly Dictionary<ulong, Color> playerColors = new Dictionary<ulong, Color>();
    private float nextAutoSpawnCheckTime = 0f;
    private const float AutoSpawnCheckIntervalSeconds = 0.25f;
    private static bool collisionLayersConfigured;
    private readonly Dictionary<ulong, ClientDisplayState> clientDisplayStates = new Dictionary<ulong, ClientDisplayState>();
    private readonly System.Random soundRandom = new System.Random();
    private readonly Dictionary<ulong, MinigunAudioSequenceState> activeMinigunAudioStates = new Dictionary<ulong, MinigunAudioSequenceState>();
    private readonly Dictionary<ulong, MegaBombMusicState> activeMegaBombMusicStates = new Dictionary<ulong, MegaBombMusicState>();
    private readonly NetworkVariable<int> activeMegaBombLockdownCount = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private bool clientDisplayStateDirty;
    private const float MinigunShotCrossfadeSeconds = 0.035f;
    private const float MegaBombMusicVolumeMultiplier = 0.7f;

    public bool IsMegaBombLockdownActive => activeMegaBombLockdownCount.Value > 0;

    private void Awake()
    {
        ConfigureCollisionLayers();
        availablePrimaryColors.AddRange(primaryColors);
        AudioSettingsStore.EnsureInitialized();
        sfxVolume = AudioSettingsStore.SfxVolume;
    }

    private void OnEnable()
    {
        AudioSettingsStore.SfxVolumeChanged += HandleSfxVolumeChanged;
    }

    private void OnDisable()
    {
        AudioSettingsStore.SfxVolumeChanged -= HandleSfxVolumeChanged;
    }

    private void Start()
    {
        displayManager = FindObjectOfType<PlayerDisplayManager>();
        if (displayManager == null)
        {
            Debug.LogError("PlayerDisplayManager not found in the scene!");
        }
    }

    private void OnDestroy()
    {
        AudioSettingsStore.SfxVolumeChanged -= HandleSfxVolumeChanged;
        ResetAllMinigunAudioStatesLocal();
        ResetAllMegaBombMusicStatesLocal();
    }

    private void HandleSfxVolumeChanged(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);

        foreach (KeyValuePair<ulong, MinigunAudioSequenceState> entry in activeMinigunAudioStates)
        {
            MinigunAudioSequenceState state = entry.Value;
            if (state == null)
            {
                continue;
            }

            if (state.ShotSourceA != null)
            {
                state.ShotSourceA.volume = state.ShotSourceA.isPlaying ? sfxVolume : 0f;
            }

            if (state.ShotSourceB != null)
            {
                state.ShotSourceB.volume = state.ShotSourceB.isPlaying ? sfxVolume : 0f;
            }
        }

        foreach (KeyValuePair<ulong, MegaBombMusicState> entry in activeMegaBombMusicStates)
        {
            MegaBombMusicState state = entry.Value;
            if (state?.LoopSource != null)
            {
                state.LoopSource.volume = state.LoopSource.isPlaying
                    ? ResolveMegaBombMusicVolume()
                    : 0f;
            }
        }
    }

    private void ConfigureCollisionLayers()
    {
        if (collisionLayersConfigured)
        {
            return;
        }

        int playerLayer = LayerMask.NameToLayer("Player");
        int bulletLayer = LayerMask.NameToLayer("Bullet");
        int bulletTriggerLayer = LayerMask.NameToLayer("BulletTrigger");

        if (playerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(playerLayer, playerLayer, true);
        }
        else
        {
            Debug.LogWarning("[GameManager] Layer 'Player' was not found.");
        }

        if (bulletLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bulletLayer, bulletLayer, true);
        }
        else
        {
            Debug.LogWarning("[GameManager] Layer 'Bullet' was not found.");
        }

        if (bulletLayer >= 0 && bulletTriggerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bulletLayer, bulletTriggerLayer, true);
        }

        if (bulletTriggerLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(bulletTriggerLayer, bulletTriggerLayer, true);
        }

        collisionLayersConfigured = true;
    }

    public void PlayBulletBounceSoundServer(Vector3 worldPosition)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        if (!TrySelectBounceSoundEffect(out SoundEffectId effectId))
        {
            return;
        }

        PlaySoundEffectClientRpc((int)effectId, worldPosition);
    }

    public void PlayBulletDespawnSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.BulletDespawn, worldPosition);
    }

    public void PlayBulletShootSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.BulletShoot, worldPosition);
    }

    public void PlayPlayerDieSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.PlayerDie, worldPosition);
        PlayPlayerDeathCameraShakeServer();
    }

    public void PlayAbilitySpawnSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.AbilitySpawn, worldPosition);
    }

    public void PlayAbilityPickupSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.AbilityPickup, worldPosition);
    }

    public void PlayBombExplodeSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.BombExplode, worldPosition);
        PlayBombExplosionCameraShakeServer();
    }

    public void PlayLazerShootSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.LazerShoot, worldPosition);
    }

    public void PlayMissileShootSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.MissileShoot, worldPosition);
    }

    public void PlayMissileLockSoundForClientServer(ulong _, Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.MissileLock, worldPosition);
    }

    public void PlayMissileTargetSoundForClientServer(ulong _, Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.MissileTarget, worldPosition);
    }

    public void PlayMegaBombMusicServer(ulong ownerClientId, Vector3 worldPosition)
    {
        if (!IsServer || !IsSpawned || megaBombMusicClip == null)
        {
            return;
        }

        PlayMegaBombMusicClientRpc(ownerClientId, worldPosition);
    }

    public void StopMegaBombMusicServer(ulong ownerClientId)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        StopMegaBombMusicClientRpc(ownerClientId);
    }

    public void PlayMegaBombActivateSoundServer(Vector3 worldPosition)
    {
        PlaySoundEffectServer(SoundEffectId.MegaBombActivate, worldPosition);
    }

    public void PlayMegaBombCameraShakeServer()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        PlayCameraShakeServer(
            megaBombShakeAmplitude,
            megaBombShakeDuration,
            megaBombShakeFrequency);
    }

    public void PlayDirectionalSmokeBurstServer(DirectionalSmokeBurst.Settings settings, string objectName)
    {
        PlayDirectionalSmokeBurstServer(new DirectionalSmokeBurst.NetworkPayload(settings), objectName);
    }

    public void PlayDirectionalSmokeBurstServer(DirectionalSmokeBurst.NetworkPayload payload, string objectName)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        FixedString64Bytes effectObjectName = new FixedString64Bytes(
            string.IsNullOrWhiteSpace(objectName) ? "DirectionalSmokeBurst" : objectName);
        PlayDirectionalSmokeBurstClientRpc(payload, effectObjectName);
    }

    public void PlayMinigunStartSoundServer(ulong ownerClientId, Vector3 worldPosition)
    {
        if (!IsServer || !IsSpawned || minigunStartClip == null)
        {
            return;
        }

        PlayMinigunStartClientRpc(ownerClientId, worldPosition);
    }

    public void PlayMinigunShotSoundServer(ulong ownerClientId, Vector3 worldPosition)
    {
        if (!IsServer || !IsSpawned || !TrySelectMinigunShotClipIndex(out int clipIndex))
        {
            return;
        }

        PlayMinigunShotClientRpc(ownerClientId, worldPosition, clipIndex);
    }

    public void PlayMinigunCooldownSoundServer(ulong ownerClientId, Vector3 worldPosition)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        PlayMinigunCooldownClientRpc(ownerClientId, worldPosition);
    }

    public void StopMinigunAudioSequenceServer(ulong ownerClientId)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        StopMinigunAudioSequenceClientRpc(ownerClientId);
    }

    private void PlaySoundEffectServer(
        SoundEffectId effectId,
        Vector3 worldPosition,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsServer || !IsSpawned || ResolveSoundClip(effectId) == null)
        {
            return;
        }

        PlaySoundEffectClientRpc((int)effectId, worldPosition, clientRpcParams);
    }

    [ClientRpc]
    private void PlaySoundEffectClientRpc(
        int effectIdValue,
        Vector3 worldPosition,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        PlaySoundEffectLocal((SoundEffectId)effectIdValue, worldPosition);
    }

    [ClientRpc]
    private void PlayMinigunStartClientRpc(
        ulong ownerClientId,
        Vector3 worldPosition,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        PlayMinigunStartLocal(ownerClientId, worldPosition);
    }

    [ClientRpc]
    private void PlayMinigunShotClientRpc(
        ulong ownerClientId,
        Vector3 worldPosition,
        int clipIndex,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        PlayMinigunShotLocal(ownerClientId, worldPosition, clipIndex);
    }

    [ClientRpc]
    private void PlayMinigunCooldownClientRpc(
        ulong ownerClientId,
        Vector3 worldPosition,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        PlayMinigunCooldownLocal(ownerClientId, worldPosition);
    }

    [ClientRpc]
    private void StopMinigunAudioSequenceClientRpc(
        ulong ownerClientId,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        ResetMinigunAudioStateLocal(ownerClientId);
    }

    [ClientRpc]
    private void PlayMegaBombMusicClientRpc(
        ulong ownerClientId,
        Vector3 worldPosition,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        PlayMegaBombMusicLocal(ownerClientId, worldPosition);
    }

    [ClientRpc]
    private void StopMegaBombMusicClientRpc(
        ulong ownerClientId,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        ResetMegaBombMusicStateLocal(ownerClientId);
    }

    [ClientRpc]
    private void PlayDirectionalSmokeBurstClientRpc(
        DirectionalSmokeBurst.NetworkPayload payload,
        FixedString64Bytes objectName,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        DirectionalSmokeBurst.TrySpawn(payload, objectName.ToString());
    }

    [ClientRpc]
    private void PlayCameraShakeClientRpc(
        float amplitude,
        float duration,
        float frequency,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        PlayCameraShakeLocal(amplitude, duration, frequency);
    }

    private void PlaySoundEffectLocal(SoundEffectId effectId, Vector3 worldPosition)
    {
        AudioClip clip = ResolveSoundClip(effectId);
        if (clip == null)
        {
            return;
        }

        GameObject audioObject = new GameObject($"OneShotSfx_{effectId}");
        audioObject.transform.position = worldPosition;

        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = Mathf.Clamp01(sfxVolume);
        audioSource.clip = clip;
        audioSource.Play();

        Destroy(audioObject, Mathf.Max(clip.length, 0.01f) + 0.1f);
    }

    private void PlayPlayerDeathCameraShakeServer()
    {
        PlayCameraShakeServer(
            playerDeathShakeAmplitude,
            playerDeathShakeDuration,
            playerDeathShakeFrequency);
    }

    private void PlayBombExplosionCameraShakeServer()
    {
        PlayCameraShakeServer(
            bombExplosionShakeAmplitude,
            bombExplosionShakeDuration,
            bombExplosionShakeFrequency);
    }

    private void PlayCameraShakeServer(
        float amplitude,
        float duration,
        float frequency,
        ClientRpcParams clientRpcParams = default)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        if (amplitude <= 0f || duration <= 0f)
        {
            return;
        }

        PlayCameraShakeClientRpc(
            Mathf.Max(0f, amplitude),
            Mathf.Max(0f, duration),
            Mathf.Max(0.01f, frequency),
            clientRpcParams);
    }

    private void PlayCameraShakeLocal(float amplitude, float duration, float frequency)
    {
        if (amplitude <= 0f || duration <= 0f)
        {
            return;
        }

        CameraShakeController shakeController = ResolveLocalCameraShakeController();
        if (shakeController == null)
        {
            return;
        }

        shakeController.Shake(amplitude, duration, frequency);
    }

    private CameraShakeController ResolveLocalCameraShakeController()
    {
        if (localCameraShakeController != null)
        {
            return localCameraShakeController;
        }

        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate != null && candidate.isActiveAndEnabled)
                {
                    targetCamera = candidate;
                    break;
                }
            }
        }

        if (targetCamera == null)
        {
            return null;
        }

        localCameraShakeController = targetCamera.GetComponent<CameraShakeController>();
        if (localCameraShakeController == null)
        {
            localCameraShakeController = targetCamera.gameObject.AddComponent<CameraShakeController>();
        }

        return localCameraShakeController;
    }

    private void PlayMinigunStartLocal(ulong ownerClientId, Vector3 worldPosition)
    {
        ResetMinigunAudioStateLocal(ownerClientId);
        if (minigunStartClip == null)
        {
            return;
        }

        MinigunAudioSequenceState state = GetOrCreateMinigunAudioStateLocal(ownerClientId, worldPosition);
        double startTime = AudioSettings.dspTime;
        ScheduleMinigunOneShotLocal(state, minigunStartClip, startTime, worldPosition, "Start");
        state.FirstShotAllowedDspTime = startTime + minigunStartClip.length;
        state.NextOneShotEndDspTime = startTime + minigunStartClip.length;
    }

    private void PlayMinigunShotLocal(ulong ownerClientId, Vector3 worldPosition, int clipIndex)
    {
        AudioClip shotClip = ResolveMinigunShotClip(clipIndex);
        if (shotClip == null)
        {
            return;
        }

        MinigunAudioSequenceState state = GetOrCreateMinigunAudioStateLocal(ownerClientId, worldPosition);
        double startTime = Math.Max(AudioSettings.dspTime, state.FirstShotAllowedDspTime);
        state.NextOneShotEndDspTime = startTime + shotClip.length;

        if (state.PendingShotPlaybackCoroutine != null)
        {
            StopCoroutine(state.PendingShotPlaybackCoroutine);
            state.PendingShotPlaybackCoroutine = null;
        }

        double delaySeconds = startTime - AudioSettings.dspTime;
        if (delaySeconds > 0.001d)
        {
            state.PendingShotPlaybackCoroutine = StartCoroutine(
                PlayMinigunShotAfterDelayLocal(state, shotClip, worldPosition, (float)delaySeconds));
            return;
        }

        StartMinigunShotTransitionLocal(state, shotClip, worldPosition);
    }

    private void PlayMinigunCooldownLocal(ulong ownerClientId, Vector3 worldPosition)
    {
        MinigunAudioSequenceState state = GetOrCreateMinigunAudioStateLocal(ownerClientId, worldPosition);
        double cooldownStartTime = Math.Max(AudioSettings.dspTime, state.NextOneShotEndDspTime);

        if (minigunCooldownClip != null)
        {
            ScheduleMinigunOneShotLocal(state, minigunCooldownClip, cooldownStartTime, worldPosition, "Cooldown");
            state.NextOneShotEndDspTime = cooldownStartTime + minigunCooldownClip.length;
        }
        else
        {
            state.NextOneShotEndDspTime = cooldownStartTime;
        }

        StartCoroutine(CleanupMinigunAudioStateAfterDelay(ownerClientId, state, state.NextOneShotEndDspTime));
    }

    private void PlayMegaBombMusicLocal(ulong ownerClientId, Vector3 worldPosition)
    {
        ResetMegaBombMusicStateLocal(ownerClientId);
        if (megaBombMusicClip == null)
        {
            return;
        }

        GameObject rootObject = new GameObject($"MegaBombMusic_{ownerClientId}");
        rootObject.transform.position = worldPosition;

        AudioSource audioSource = rootObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.loop = true;
        audioSource.volume = ResolveMegaBombMusicVolume();
        audioSource.clip = megaBombMusicClip;
        audioSource.Play();

        activeMegaBombMusicStates[ownerClientId] = new MegaBombMusicState
        {
            RootObject = rootObject,
            LoopSource = audioSource
        };
    }

    private MinigunAudioSequenceState GetOrCreateMinigunAudioStateLocal(ulong ownerClientId, Vector3 worldPosition)
    {
        if (activeMinigunAudioStates.TryGetValue(ownerClientId, out MinigunAudioSequenceState existingState) &&
            existingState != null &&
            existingState.RootObject != null)
        {
            existingState.RootObject.transform.position = worldPosition;
            return existingState;
        }

        GameObject rootObject = new GameObject($"MinigunAudio_{ownerClientId}");
        rootObject.transform.position = worldPosition;

        AudioSource shotSourceA = rootObject.AddComponent<AudioSource>();
        ConfigureMinigunShotSourceLocal(shotSourceA);

        AudioSource shotSourceB = rootObject.AddComponent<AudioSource>();
        ConfigureMinigunShotSourceLocal(shotSourceB);

        MinigunAudioSequenceState state = new MinigunAudioSequenceState
        {
            RootObject = rootObject,
            ShotSourceA = shotSourceA,
            ShotSourceB = shotSourceB,
            ActiveShotSource = null,
            PendingShotPlaybackCoroutine = null,
            ShotCrossfadeCoroutine = null,
            FirstShotAllowedDspTime = AudioSettings.dspTime,
            NextOneShotEndDspTime = AudioSettings.dspTime
        };

        activeMinigunAudioStates[ownerClientId] = state;
        return state;
    }

    private void ConfigureMinigunShotSourceLocal(AudioSource audioSource)
    {
        if (audioSource == null)
        {
            return;
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 0f;
        audioSource.loop = false;
    }

    private System.Collections.IEnumerator PlayMinigunShotAfterDelayLocal(
        MinigunAudioSequenceState state,
        AudioClip shotClip,
        Vector3 worldPosition,
        float delaySeconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));

        if (state == null)
        {
            yield break;
        }

        state.PendingShotPlaybackCoroutine = null;

        if (state.RootObject == null || shotClip == null)
        {
            yield break;
        }

        StartMinigunShotTransitionLocal(state, shotClip, worldPosition);
    }

    private void StartMinigunShotTransitionLocal(
        MinigunAudioSequenceState state,
        AudioClip shotClip,
        Vector3 worldPosition)
    {
        if (state == null || state.RootObject == null || shotClip == null)
        {
            return;
        }

        if (state.ShotCrossfadeCoroutine != null)
        {
            StopCoroutine(state.ShotCrossfadeCoroutine);
            state.ShotCrossfadeCoroutine = null;
        }

        AudioSource previousSource = state.ActiveShotSource;
        AudioSource nextSource = previousSource == state.ShotSourceA
            ? state.ShotSourceB
            : state.ShotSourceA;

        if (nextSource == null)
        {
            return;
        }

        nextSource.transform.position = worldPosition;
        nextSource.Stop();
        nextSource.clip = shotClip;
        nextSource.loop = false;
        nextSource.volume = 0f;
        nextSource.Play();

        state.ActiveShotSource = nextSource;
        state.ShotCrossfadeCoroutine = StartCoroutine(
            CrossfadeMinigunShotSourcesLocal(state, previousSource, nextSource));
    }

    private System.Collections.IEnumerator CrossfadeMinigunShotSourcesLocal(
        MinigunAudioSequenceState state,
        AudioSource previousSource,
        AudioSource nextSource)
    {
        float targetVolume = Mathf.Clamp01(sfxVolume);
        float fadeDuration = Mathf.Max(0.005f, MinigunShotCrossfadeSeconds);
        float previousStartVolume = previousSource != null ? previousSource.volume : 0f;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);

            if (nextSource != null)
            {
                nextSource.volume = Mathf.Lerp(0f, targetVolume, t);
            }

            if (previousSource != null)
            {
                previousSource.volume = Mathf.Lerp(previousStartVolume, 0f, t);
            }

            yield return null;
        }

        if (nextSource != null)
        {
            nextSource.volume = targetVolume;
        }

        if (previousSource != null)
        {
            previousSource.Stop();
            previousSource.clip = null;
            previousSource.volume = 0f;
        }

        if (state != null)
        {
            state.ShotCrossfadeCoroutine = null;
        }
    }

    private void ScheduleMinigunOneShotLocal(
        MinigunAudioSequenceState state,
        AudioClip clip,
        double startTime,
        Vector3 worldPosition,
        string clipLabel)
    {
        if (state == null || state.RootObject == null || clip == null)
        {
            return;
        }

        GameObject audioObject = new GameObject($"Minigun_{clipLabel}");
        audioObject.transform.SetParent(state.RootObject.transform, false);
        audioObject.transform.position = worldPosition;

        AudioSource audioSource = audioObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = Mathf.Clamp01(sfxVolume);
        audioSource.clip = clip;
        audioSource.PlayScheduled(startTime);

        float destroyDelaySeconds = Mathf.Max(
            0.1f,
            (float)(startTime - AudioSettings.dspTime) + clip.length + 0.1f);
        Destroy(audioObject, destroyDelaySeconds);
    }

    private System.Collections.IEnumerator CleanupMinigunAudioStateAfterDelay(
        ulong ownerClientId,
        MinigunAudioSequenceState state,
        double sequenceEndDspTime)
    {
        float waitSeconds = Mathf.Max(0.02f, (float)(sequenceEndDspTime - AudioSettings.dspTime) + 0.15f);
        yield return new WaitForSeconds(waitSeconds);

        if (!activeMinigunAudioStates.TryGetValue(ownerClientId, out MinigunAudioSequenceState activeState) ||
            !ReferenceEquals(activeState, state))
        {
            yield break;
        }

        ResetMinigunAudioStateLocal(ownerClientId);
    }

    private void ResetMinigunAudioStateLocal(ulong ownerClientId)
    {
        if (!activeMinigunAudioStates.TryGetValue(ownerClientId, out MinigunAudioSequenceState state))
        {
            return;
        }

        if (state != null)
        {
            if (state.PendingShotPlaybackCoroutine != null)
            {
                StopCoroutine(state.PendingShotPlaybackCoroutine);
                state.PendingShotPlaybackCoroutine = null;
            }

            if (state.ShotCrossfadeCoroutine != null)
            {
                StopCoroutine(state.ShotCrossfadeCoroutine);
                state.ShotCrossfadeCoroutine = null;
            }

            StopMinigunShotSourceLocal(state.ShotSourceA);
            StopMinigunShotSourceLocal(state.ShotSourceB);
        }

        if (state != null && state.RootObject != null)
        {
            Destroy(state.RootObject);
        }

        activeMinigunAudioStates.Remove(ownerClientId);
    }

    private void StopMinigunShotSourceLocal(AudioSource audioSource)
    {
        if (audioSource == null)
        {
            return;
        }

        audioSource.Stop();
        audioSource.clip = null;
        audioSource.volume = 0f;
    }

    private void ResetAllMinigunAudioStatesLocal()
    {
        foreach (KeyValuePair<ulong, MinigunAudioSequenceState> entry in activeMinigunAudioStates)
        {
            MinigunAudioSequenceState state = entry.Value;
            if (state != null && state.RootObject != null)
            {
                Destroy(state.RootObject);
            }
        }

        activeMinigunAudioStates.Clear();
    }

    private float ResolveMegaBombMusicVolume()
    {
        return Mathf.Clamp01(sfxVolume * MegaBombMusicVolumeMultiplier);
    }

    private void ResetMegaBombMusicStateLocal(ulong ownerClientId)
    {
        if (!activeMegaBombMusicStates.TryGetValue(ownerClientId, out MegaBombMusicState state))
        {
            return;
        }

        if (state?.LoopSource != null)
        {
            state.LoopSource.Stop();
            state.LoopSource.clip = null;
            state.LoopSource.volume = 0f;
        }

        if (state?.RootObject != null)
        {
            Destroy(state.RootObject);
        }

        activeMegaBombMusicStates.Remove(ownerClientId);
    }

    private void ResetAllMegaBombMusicStatesLocal()
    {
        foreach (KeyValuePair<ulong, MegaBombMusicState> entry in activeMegaBombMusicStates)
        {
            MegaBombMusicState state = entry.Value;
            if (state?.RootObject != null)
            {
                Destroy(state.RootObject);
            }
        }

        activeMegaBombMusicStates.Clear();
    }

    private bool TrySelectBounceSoundEffect(out SoundEffectId effectId)
    {
        bool hasFirstClip = bulletBounce1Clip != null;
        bool hasSecondClip = bulletBounce2Clip != null;

        effectId = SoundEffectId.BulletBounce1;
        if (!hasFirstClip && !hasSecondClip)
        {
            return false;
        }

        if (hasFirstClip && hasSecondClip)
        {
            effectId = soundRandom.Next(0, 2) == 0
                ? SoundEffectId.BulletBounce1
                : SoundEffectId.BulletBounce2;
            return true;
        }

        effectId = hasFirstClip
            ? SoundEffectId.BulletBounce1
            : SoundEffectId.BulletBounce2;
        return true;
    }

    private bool TrySelectMinigunShotClipIndex(out int clipIndex)
    {
        clipIndex = -1;
        if (minigunShotClips == null || minigunShotClips.Length == 0)
        {
            return false;
        }

        int validClipCount = 0;
        for (int i = 0; i < minigunShotClips.Length; i++)
        {
            if (minigunShotClips[i] != null)
            {
                validClipCount++;
            }
        }

        if (validClipCount == 0)
        {
            return false;
        }

        int selectedValidClip = soundRandom.Next(validClipCount);
        for (int i = 0; i < minigunShotClips.Length; i++)
        {
            if (minigunShotClips[i] == null)
            {
                continue;
            }

            if (selectedValidClip == 0)
            {
                clipIndex = i;
                return true;
            }

            selectedValidClip--;
        }

        return false;
    }

    private AudioClip ResolveMinigunShotClip(int clipIndex)
    {
        if (minigunShotClips == null || clipIndex < 0 || clipIndex >= minigunShotClips.Length)
        {
            return null;
        }

        return minigunShotClips[clipIndex];
    }

    private AudioClip ResolveSoundClip(SoundEffectId effectId)
    {
        switch (effectId)
        {
            case SoundEffectId.BulletBounce1:
                return bulletBounce1Clip;
            case SoundEffectId.BulletBounce2:
                return bulletBounce2Clip;
            case SoundEffectId.BulletDespawn:
                return bulletDespawnClip;
            case SoundEffectId.BulletShoot:
                return bulletShootClip;
            case SoundEffectId.PlayerDie:
                return playerDieClip;
            case SoundEffectId.AbilitySpawn:
                return abilitySpawnClip;
            case SoundEffectId.AbilityPickup:
                return abilityPickupClip;
            case SoundEffectId.BombExplode:
                return bombExplodeClip;
            case SoundEffectId.LazerShoot:
                return lazerShootClip;
            case SoundEffectId.MissileShoot:
                return missileShootClip;
            case SoundEffectId.MissileLock:
                return missileLockClip;
            case SoundEffectId.MissileTarget:
                return missileTargetClip;
            case SoundEffectId.MinigunStart:
                return minigunStartClip;
            case SoundEffectId.MinigunFirstBullet:
                return null;
            case SoundEffectId.MinigunCooldown:
                return minigunCooldownClip;
            case SoundEffectId.MegaBombActivate:
                return megaBombActivateClip;
            default:
                return null;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        activeMegaBombLockdownCount.Value = 0;
        nextAutoSpawnCheckTime = 0f;
        TrySpawnMissingPlayers();
    }

    public void BeginMegaBombLockdownServer(AbilityPickup pickupToKeep = null)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        int nextCount = Mathf.Max(0, activeMegaBombLockdownCount.Value) + 1;
        activeMegaBombLockdownCount.Value = nextCount;
        if (pickupToKeep != null || nextCount == 1)
        {
            DespawnAllAbilityPickups(pickupToKeep);
        }
    }

    public void EndMegaBombLockdownServer()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        activeMegaBombLockdownCount.Value = Mathf.Max(0, activeMegaBombLockdownCount.Value - 1);
    }

    public void ForceClearAllTankAbilitiesServer()
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        TankAbilityController[] controllers = FindObjectsByType<TankAbilityController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            TankAbilityController controller = controllers[i];
            if (controller == null || !controller.IsServer || !controller.IsSpawned)
            {
                continue;
            }

            controller.ForceClearAbilityServer();
        }
    }

    public bool AreAllConnectedClientsSpawned()
    {
        if (NetworkManager == null)
        {
            return false;
        }

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (!HasSpawnForClient(client.ClientId))
            {
                return false;
            }
        }

        return true;
    }

    private bool AreAllActiveRoundClientsSpawned()
    {
        if (NetworkManager == null)
        {
            return false;
        }

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            ulong clientId = client.ClientId;
            if (eliminatedPlayersThisRound.Contains(clientId))
            {
                continue;
            }

            if (!HasSpawnForClient(clientId))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsReadyForAbilitySpawns()
    {
        return IsServer &&
               IsSpawned &&
               !startingNewRound &&
               AreAllActiveRoundClientsSpawned();
    }

    private void Update()
    {
        if (IsClient)
        {
            TryApplyClientDisplayState();
        }

        if (!IsServer || !IsSpawned || NetworkManager == null)
        {
            return;
        }

        if (startingNewRound)
        {
            return;
        }

        if (Time.unscaledTime < nextAutoSpawnCheckTime)
        {
            return;
        }

        nextAutoSpawnCheckTime = Time.unscaledTime + AutoSpawnCheckIntervalSeconds;
        TrySpawnMissingPlayers();
    }

    private void StageScoreForClientUi(ulong clientId, int score)
    {
        if (!clientDisplayStates.TryGetValue(clientId, out ClientDisplayState state))
        {
            state = new ClientDisplayState();
        }

        state.Score = score;
        clientDisplayStates[clientId] = state;
        clientDisplayStateDirty = true;
    }

    private void StageColorForClientUi(ulong clientId, Color color)
    {
        if (!clientDisplayStates.TryGetValue(clientId, out ClientDisplayState state))
        {
            state = new ClientDisplayState();
        }

        state.IconColor = color;
        state.HasIconColor = true;
        clientDisplayStates[clientId] = state;
        clientDisplayStateDirty = true;
    }

    private void RemoveClientFromUiState(ulong clientId)
    {
        clientDisplayStates.Remove(clientId);
        clientDisplayStateDirty = true;
    }

    private void TryApplyClientDisplayState()
    {
        if (!clientDisplayStateDirty)
        {
            return;
        }

        PlayerDisplayManager manager = PlayerDisplayManager.Instance;
        if (manager == null || !manager.IsReady())
        {
            return;
        }

        List<ulong> displayedClientIds = manager.GetDisplayedClientIds();
        for (int i = 0; i < displayedClientIds.Count; i++)
        {
            ulong displayedClientId = displayedClientIds[i];
            if (!clientDisplayStates.ContainsKey(displayedClientId))
            {
                manager.RemovePlayerDisplay(displayedClientId);
            }
        }

        foreach (var kvp in clientDisplayStates)
        {
            ulong clientId = kvp.Key;
            ClientDisplayState state = kvp.Value;

            if (manager.HasPlayerDisplay(clientId))
            {
                manager.UpdatePlayerScore(clientId, state.Score);
            }
            else
            {
                manager.CreatePlayerDisplay(clientId, state.Score);
            }

            if (state.HasIconColor)
            {
                manager.SetIconColor(clientId, state.IconColor);
            }
        }

        clientDisplayStateDirty = false;
    }

    private void TrySpawnMissingPlayers()
    {
        if (NetworkManager == null)
        {
            return;
        }

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            ulong clientId = client.ClientId;
            if (HasSpawnForClient(clientId))
            {
                continue;
            }

            // Dead players must wait for the next round before they can spawn again.
            if (eliminatedPlayersThisRound.Contains(clientId))
            {
                continue;
            }

            if (!CanSpawnPlayerNow())
            {
                Debug.Log($"[GameManager] Auto-spawn delayed for client {clientId}: {GetSpawnReadinessReason()}.");
                continue;
            }

            if (SpawnPlayerOnConnect(clientId))
            {
                Debug.Log($"[GameManager] Auto-spawned player for client {clientId}.");
            }
        }
    }

    private Color AssignColor(ulong clientId)
    {

        if (playerColors.TryGetValue(clientId, out Color existingColor))
        {
            return existingColor;
        }

        Color assignedColor;

        if (availablePrimaryColors.Count > 0)
        {
            // Assign the first available primary color
            assignedColor = availablePrimaryColors[0];
            availablePrimaryColors.RemoveAt(0);
        }
        else
        {
            // Assign a random color
            assignedColor = UnityEngine.Random.ColorHSV();
        }

        playerColors[clientId] = assignedColor;

        return assignedColor;
    }

    private void ReleaseColor(ulong clientId)
    {
        if (playerColors.TryGetValue(clientId, out Color color))
        {
            if (primaryColors.Contains(color))
            {
                availablePrimaryColors.Add(color);
            }

            playerColors.Remove(clientId);
        }
    }

    public bool SpawnPlayerOnConnect(ulong clientId)
    {
        if (HasSpawnForClient(clientId))
        {
            return true;
        }

        if (!CanSpawnPlayerNow())
        {
            return false;
        }

        Color playerColor = AssignColor(clientId);

        if (!spawnPlayer(clientId))
        {
            return false;
        }

        InitializePlayerDisplayAndColors(clientId);
        AssignIconColorClientRpc(clientId, playerColor);
        AssignTankColor(clientId, playerColor);
        return true;
    }

    public bool SpawnPlayerOnNewRound(ulong clientId)
    {
        if (HasSpawnForClient(clientId))
        {
            return true;
        }

        if (!CanSpawnPlayerNow())
        {
            return false;
        }

        if (!playerColors.TryGetValue(clientId, out Color existingColor))
        {
            existingColor = AssignColor(clientId);
        }

        if (!spawnPlayer(clientId))
        {
            return false;
        }

        AssignIconColorClientRpc(clientId, existingColor);
        AssignTankColor(clientId, existingColor);
        return true;
    }

    private bool spawnPlayer(ulong clientId)
    {
        if (availableCells == null || availableCells.Count == 0)
        {
            Debug.LogWarning("No available cells for spawning players.");
            return false;
        }


        int cellIndex = UnityEngine.Random.Range(0, availableCells.Count);
        Vector2Int cell = availableCells[cellIndex];

        Vector3 spawnPosition = mazeGenerator.CellToWorldPosition(cell);
        float spawnYawDegrees = UnityEngine.Random.Range(0, 4) * 90f;
        Quaternion spawnRotation = Quaternion.Euler(0f, 0f, spawnYawDegrees);

        try
        {
            NetworkObject spawnedPlayerNetworkObject = NetworkObject.InstantiateAndSpawn(
                playerPrefab,
                NetworkManager,
                ownerClientId: clientId,
                destroyWithScene: false,
                isPlayerObject: false,
                forceOverride: false,
                position: spawnPosition,
                rotation: spawnRotation
            );

            if (spawnedPlayerNetworkObject == null)
            {
                Debug.LogError($"[GameManager] InstantiateAndSpawn returned null for client {clientId}.");
                return false;
            }

            availableCells.RemoveAt(cellIndex);

            GameObject player = spawnedPlayerNetworkObject.gameObject;
            ApplyPlayerCollisionSettings(player, clientId);
            eliminatedPlayersThisRound.Remove(clientId);
            alivePlayers.Add(clientId);
            clientIdToPlayer[clientId] = player;

            Debug.Log($"[Server] Spawned player {clientId} at cell {cell} (world position {spawnPosition}, rotation {spawnYawDegrees} deg)");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[GameManager] InstantiateAndSpawn failed for client {clientId}: {ex.Message}");
            return false;
        }
    }

    public bool HasSpawnForClient(ulong clientId)
    {
        return clientIdToPlayer.TryGetValue(clientId, out GameObject player) && player != null;
    }

    public bool TryGetPlayerObject(ulong clientId, out GameObject playerObject)
    {
        if (clientIdToPlayer.TryGetValue(clientId, out GameObject player) && player != null)
        {
            playerObject = player;
            return true;
        }

        playerObject = null;
        return false;
    }

    private void ApplyPlayerCollisionSettings(GameObject player, ulong ownerClientId)
    {
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
        {
            SetLayerRecursively(player, playerLayer);
        }

        Collider2D[] newPlayerColliders = player.GetComponentsInChildren<Collider2D>(true);
        foreach (var kvp in clientIdToPlayer)
        {
            if (kvp.Key == ownerClientId || kvp.Value == null)
            {
                continue;
            }

            Collider2D[] existingPlayerColliders = kvp.Value.GetComponentsInChildren<Collider2D>(true);
            IgnoreColliderPairs(newPlayerColliders, existingPlayerColliders);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].gameObject.layer = layer;
        }
    }

    private static void IgnoreColliderPairs(Collider2D[] first, Collider2D[] second)
    {
        for (int i = 0; i < first.Length; i++)
        {
            Collider2D firstCollider = first[i];
            if (firstCollider == null)
            {
                continue;
            }

            for (int j = 0; j < second.Length; j++)
            {
                Collider2D secondCollider = second[j];
                if (secondCollider == null)
                {
                    continue;
                }

                Physics2D.IgnoreCollision(firstCollider, secondCollider, true);
            }
        }
    }

    public bool CanSpawnPlayerNow()
    {
        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        }

        return playerPrefab != null &&
               mazeGenerator != null &&
               availableCells != null &&
               availableCells.Count > 0;
    }

    public string GetSpawnReadinessReason()
    {
        if (playerPrefab == null)
        {
            return "playerPrefab is null";
        }

        if (mazeGenerator == null)
        {
            return "mazeGenerator is null";
        }

        if (availableCells == null)
        {
            return "availableCells is null";
        }

        if (availableCells.Count <= 0)
        {
            return "availableCells is empty";
        }

        return "ready";
    }

    [ClientRpc]
    private void AssignIconColorClientRpc(ulong clientId, Color color, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        StageColorForClientUi(clientId, color);
        TryApplyClientDisplayState();
    }

    private void AssignTankColor(ulong clientId, Color color)
    {
        if (clientIdToPlayer.TryGetValue(clientId, out GameObject player))
        {
            TankController tank = player.GetComponent<TankController>();
            if (tank != null)
            {
                tank.ServerSetColor(color);
            }
            else
            {
                Debug.LogWarning($"TankController not found on player {clientId}");
            }
        }
    }

    public void InitializePlayerDisplayAndColors(ulong clientId)
    {
        if (!playerScores.ContainsKey(clientId))
        {
            playerScores[clientId] = 0; // Initialize score if not present
        }

        CreatePlayerDisplayClientRpc(clientId, playerScores[clientId]);

        List<ulong> existingClientIds = new List<ulong>();
        List<int> existingScores = new List<int>();
        List<Color> existingColors = new List<Color>();

        foreach (var kvp in playerScores)
        {
            if (kvp.Key != clientId) // Exclude the new client
            {
                existingClientIds.Add(kvp.Key);
                existingScores.Add(kvp.Value);

                if (playerColors.TryGetValue(kvp.Key, out Color color))
                {
                    existingColors.Add(color);
                }
                else
                {
                    existingColors.Add(Color.white); // Default color if not found
                }
            }
        }

        // Convert lists to arrays for serialization
        ulong[] existingClientIdsArray = existingClientIds.ToArray();
        int[] existingScoresArray = existingScores.ToArray();
        Color[] existingColorsArray = existingColors.ToArray();

        if (existingClientIdsArray.Length > 0)
        {
            // Define ClientRpcParams to target only the new client
            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { clientId }
                }
            };

            // Send existing players' data to the new client
            SendExistingPlayerDisplaysClientRpc(existingClientIdsArray, existingScoresArray, clientRpcParams);
            SendExistingPlayerIconColorsClientRpc(existingClientIdsArray, existingColorsArray, clientRpcParams);
        }
    }

    [ClientRpc]
    private void SendExistingPlayerDisplaysClientRpc(ulong[] clientIds, int[] scores, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        int count = Mathf.Min(clientIds.Length, scores.Length);
        for (int i = 0; i < count; i++)
        {
            StageScoreForClientUi(clientIds[i], scores[i]);
        }

        TryApplyClientDisplayState();
    }

    [ClientRpc]
    private void SendExistingPlayerIconColorsClientRpc(ulong[] clientIds, Color[] colors, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        int count = Mathf.Min(clientIds.Length, colors.Length);
        for (int i = 0; i < count; i++)
        {
            StageColorForClientUi(clientIds[i], colors[i]);
        }

        TryApplyClientDisplayState();
    }

    /*
    [ClientRpc]
    private void SendExistingTankColorsClientRpc(ulong[] clientIds, Color[] colors, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient) return;

        for (int i = 0; i < clientIds.Length; i++)
        {
            if (clientIdToPlayer.TryGetValue(clientIds[i], out GameObject player))
            {
                TankController tank = player.GetComponent<TankController>();
                if (tank != null)
                {
                    Debug.Log(colors[i]);
                    tank.SetColor(colors[i]);
                }
            }
        }
    }
    */
    public void SetAvailableCells(List<Vector2Int> cells)
    {
        availableCells = cells;
        Debug.Log($"[GameManager] Received available cells: {availableCells.Count}");
    }

    [ClientRpc]
    private void CreatePlayerDisplayClientRpc(ulong clientId, int initialScore)
    {
        if (!IsClient) return;

        StageScoreForClientUi(clientId, initialScore);
        TryApplyClientDisplayState();
    }

    [ClientRpc]
    private void UpdatePlayerScoreClientRpc(ulong clientId, int newScore)
    {
        if (!IsClient) return; // Server doesn't need to handle client-side UI

        StageScoreForClientUi(clientId, newScore);
        TryApplyClientDisplayState();
    }

    [ClientRpc]
    public void RemovePlayerDisplayClientRpc(ulong clientId)
    {
        if (!IsClient) return;

        RemoveClientFromUiState(clientId);
        TryApplyClientDisplayState();
    }
    private void RemovePlayerOnNewRound(ulong clientId)
    {
        if (!IsServer) return;

        if (alivePlayers.Contains(clientId))
        {
            alivePlayers.Remove(clientId);
        }

        if (clientIdToPlayer.ContainsKey(clientId))
        {
            clientIdToPlayer.Remove(clientId);
        }

        eliminatedPlayersThisRound.Remove(clientId);

    }

    public void DespawnPlayer(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        if (!clientIdToPlayer.ContainsKey(clientId))
        {
            Debug.LogWarning($"[Server] Attempted to despawn player {clientId}, but no such player was found.");
            return;
        }

        DespawnTrackedPlayerObject(clientId);
        clientIdToPlayer.Remove(clientId);
        alivePlayers.Remove(clientId);
        Debug.Log($"[Server] Despawned player {clientId}");
    }

    public void RemovePlayerOnDisconnect(ulong clientId)
    {
        if (!IsServer) return;

        DespawnTrackedPlayerObject(clientId);

        if (alivePlayers.Contains(clientId))
        {
            alivePlayers.Remove(clientId);
        }

        if (playerScores.ContainsKey(clientId))
        {
            playerScores.Remove(clientId);
        }

        if (clientIdToPlayer.ContainsKey(clientId))
        {
            clientIdToPlayer.Remove(clientId);
        }

        eliminatedPlayersThisRound.Remove(clientId);

        ReleaseColor(clientId);

        RemovePlayerDisplayClientRpc(clientId);
    }

    private void DespawnTrackedPlayerObject(ulong clientId)
    {
        if (!clientIdToPlayer.TryGetValue(clientId, out GameObject player) || player == null)
        {
            return;
        }

        NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();
        if (playerNetworkObject != null && playerNetworkObject.IsSpawned)
        {
            playerNetworkObject.Despawn(true);
            return;
        }

        if (playerNetworkObject == null)
        {
            // Safety fallback: this should not happen for the player prefab.
            Destroy(player);
        }
    }

    public void DespawnAllProjectiles()
    {
        if (!IsServer)
        {
            return;
        }

        HashSet<GameObject> projectileObjectsToDespawn = new HashSet<GameObject>();

        Projectile[] allProjectiles = FindObjectsByType<Projectile>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allProjectiles.Length; i++)
        {
            if (allProjectiles[i] != null)
            {
                projectileObjectsToDespawn.Add(allProjectiles[i].gameObject);
            }
        }

        if (projectilesContainer != null)
        {
            Transform containerTransform = projectilesContainer.transform;
            int childCount = containerTransform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = containerTransform.GetChild(i);
                if (child != null)
                {
                    projectileObjectsToDespawn.Add(child.gameObject);
                }
            }
        }
        else
        {
            Debug.LogWarning("[GameManager] ProjectilesContainer is not assigned. Falling back to global projectile cleanup.");
        }

        int bulletLayer = LayerMask.NameToLayer("Bullet");
        if (bulletLayer >= 0)
        {
            NetworkObject[] networkObjects = FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < networkObjects.Length; i++)
            {
                NetworkObject networkObject = networkObjects[i];
                if (networkObject != null && networkObject.gameObject.layer == bulletLayer)
                {
                    projectileObjectsToDespawn.Add(networkObject.gameObject);
                }
            }
        }

        foreach (GameObject projectileObject in projectileObjectsToDespawn)
        {
            if (projectileObject == null)
            {
                continue;
            }

            Projectile projectile = projectileObject.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.ForceDestroy();
                continue;
            }

            NetworkObject projectileNetObj = projectileObject.GetComponent<NetworkObject>();
            if (projectileNetObj != null && projectileNetObj.IsSpawned)
            {
                projectileNetObj.Despawn(true);
            }
            else
            {
                Destroy(projectileObject);
            }
        }
    }

    private void DespawnAllRoundRuntimeObjects()
    {
        if (!IsServer)
        {
            return;
        }

        DespawnAllAbilityPickups();
        DespawnAllProjectiles();

        NetworkObject[] networkObjects = FindObjectsByType<NetworkObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < networkObjects.Length; i++)
        {
            NetworkObject networkObject = networkObjects[i];
            if (networkObject == null || networkObject.IsSceneObject == true)
            {
                continue;
            }

            if (networkObject.GetComponent<PlayerController>() != null)
            {
                continue;
            }

            Projectile projectile = networkObject.GetComponent<Projectile>();
            if (projectile != null)
            {
                projectile.ForceDestroy();
                continue;
            }

            if (networkObject.IsSpawned)
            {
                networkObject.Despawn(true);
            }
            else
            {
                Destroy(networkObject.gameObject);
            }
        }
    }

    private void StartNewRound()
    {
        if (!IsServer) return;

        StartCoroutine(StartNewRoundCoroutine());
    }

    private IEnumerator StartNewRoundCoroutine()
    {
        Debug.Log("[Server] Starting new round...");

        DespawnAllRoundRuntimeObjects();
        DespawnAllPlayersForNewRound();

        if (mazeGenerator == null)
        {
            mazeGenerator = FindFirstObjectByType<MazeGenerator>();
        }

        if (mazeGenerator == null)
        {
            Debug.LogError("[GameManager] Cannot start a new round because MazeGenerator is missing.");
            startingNewRound = false;
            yield break;
        }

        // Regenerate and sync the maze
        mazeGenerator.RegenerateMaze();

        // Wait for the maze to sync
        yield return new WaitForSeconds(1f); // Adjust based on synchronization speed

        // Safety pass: remove any late or inactive runtime objects before the next round spawns.
        DespawnAllRoundRuntimeObjects();
        activeMegaBombLockdownCount.Value = 0;

        // Spawn players after the maze has been regenerated and synced
        if (NetworkManager == null)
        {
            Debug.LogError("[GameManager] NetworkManager is null while starting a new round.");
            startingNewRound = false;
            yield break;
        }

        foreach (var client in NetworkManager.ConnectedClientsList)
        {
            if (!SpawnPlayerOnNewRound(client.ClientId))
            {
                Debug.LogWarning($"[GameManager] SpawnPlayerOnNewRound failed for client {client.ClientId}. Auto-spawn retry will continue.");
            }
        }

        startingNewRound = false;
        Debug.Log("[Server] Round started. Players are now alive.");
    }

    private void DespawnAllAbilityPickups(AbilityPickup pickupToKeep = null)
    {
        if (!IsServer)
        {
            return;
        }

        AbilityPickup[] pickups = FindObjectsByType<AbilityPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
        {
            AbilityPickup pickup = pickups[i];
            if (pickup == null || pickup == pickupToKeep)
            {
                continue;
            }

            NetworkObject pickupNetworkObject = pickup.GetComponent<NetworkObject>();
            if (pickupNetworkObject != null && pickupNetworkObject.IsSpawned)
            {
                pickupNetworkObject.Despawn(true);
            }
            else
            {
                Destroy(pickup.gameObject);
            }
        }
    }

    public void PlayerDied(ulong victimId, ulong killerId)
    {
        if (!IsServer) return;

        // Remove victim from alive list
        alivePlayers.Remove(victimId);
        clientIdToPlayer.Remove(victimId);
        eliminatedPlayersThisRound.Add(victimId);

        Debug.Log($"[Server] Player {victimId} died. Killer: {killerId}");

        // Check how many are still alive
        if (alivePlayers.Count <= 1 && startingNewRound == false)
        {
            startingNewRound = true;
            StartCoroutine(RoundEndRoutine());
        }
    }

    private IEnumerator RoundEndRoutine()
    {
        Debug.Log("[Server] RoundEndRoutine waiting 5 seconds...");
        yield return new WaitForSeconds(5f);

        // Award a point only if exactly one player is still alive when the round ends.
        if (alivePlayers.Count == 1)
        {
            ulong winnerId = alivePlayers.First();
            Debug.Log($"[Server] Round has ended. Winner: {winnerId}");

            if (!playerScores.ContainsKey(winnerId))
            {
                playerScores[winnerId] = 0;
            }

            playerScores[winnerId]++;
            UpdatePlayerScoreClientRpc(winnerId, playerScores[winnerId]);
        }

        StartNewRound();
    }

    private void DespawnAllPlayersForNewRound()
    {
        if (!IsServer)
        {
            return;
        }

        foreach (var kvp in clientIdToPlayer)
        {
            GameObject player = kvp.Value;
            if (player == null)
            {
                continue;
            }

            NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();
            if (playerNetworkObject != null && playerNetworkObject.IsSpawned)
            {
                playerNetworkObject.Despawn(true);
            }
        }

        clientIdToPlayer.Clear();
        alivePlayers.Clear();
        eliminatedPlayersThisRound.Clear();
    }

}
