using UnityEngine;

[DisallowMultipleComponent]
public class MainMenuMusicPlayer : MonoBehaviour
{
    [SerializeField] private AudioClip themeSong;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool playOnStart = true;
    [Tooltip("Extra global reduction applied to music before the settings slider is applied.")]
    [SerializeField, Range(0f, 1f)] private float baseMusicVolume = 0.35f;

    private AudioSource audioSource;
    private Coroutine playRoutine;

    private void Awake()
    {
        AudioSettingsStore.EnsureInitialized();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.loop = loop;
        audioSource.clip = themeSong;
        ApplyVolume(AudioSettingsStore.MusicVolume);
    }

    private void OnEnable()
    {
        AudioSettingsStore.MusicVolumeChanged += HandleMusicVolumeChanged;
    }

    private void Start()
    {
        if (playOnStart)
        {
            PlayThemeSong();
        }
    }

    private void OnDisable()
    {
        AudioSettingsStore.MusicVolumeChanged -= HandleMusicVolumeChanged;

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
    }

    public void PlayThemeSong()
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[MainMenuMusicPlayer] AudioSource is missing.");
            return;
        }

        if (themeSong == null)
        {
            Debug.LogWarning("[MainMenuMusicPlayer] No theme song is assigned.");
            return;
        }

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
        }

        playRoutine = StartCoroutine(PlayThemeSongRoutine());
    }

    private void HandleMusicVolumeChanged(float volume)
    {
        if (audioSource == null)
        {
            return;
        }

        ApplyVolume(volume);
    }

    private System.Collections.IEnumerator PlayThemeSongRoutine()
    {
        audioSource.loop = loop;
        audioSource.clip = themeSong;
        ApplyVolume(AudioSettingsStore.MusicVolume);

        if (Mathf.Approximately(audioSource.volume, 0f))
        {
            Debug.Log("[MainMenuMusicPlayer] Music volume is 0, so the theme song is muted.");
        }

        if (themeSong.loadState == AudioDataLoadState.Unloaded)
        {
            themeSong.LoadAudioData();
        }

        float timeoutAt = Time.realtimeSinceStartup + 5f;
        while (themeSong.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
        }

        if (themeSong.loadState == AudioDataLoadState.Failed)
        {
            Debug.LogWarning("[MainMenuMusicPlayer] Theme song audio data failed to load.");
            playRoutine = null;
            yield break;
        }

        if (!audioSource.isPlaying)
        {
            audioSource.Play();
        }

        playRoutine = null;
    }

    private void ApplyVolume(float settingsVolume)
    {
        if (audioSource == null)
        {
            return;
        }

        audioSource.volume = Mathf.Clamp01(settingsVolume) * Mathf.Clamp01(baseMusicVolume);
    }

    private void OnValidate()
    {
        baseMusicVolume = Mathf.Clamp01(baseMusicVolume);

        if (audioSource != null)
        {
            ApplyVolume(AudioSettingsStore.MusicVolume);
        }
    }
}
