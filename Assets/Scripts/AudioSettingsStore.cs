using System;
using UnityEngine;

public static class AudioSettingsStore
{
    private const string LegacyVolumePrefKey = "settings.volume";
    private const string SfxVolumePrefKey = "settings.volume.sfx";
    private const string MusicVolumePrefKey = "settings.volume.music";

    private static bool initialized;
    private static float sfxVolume = 1f;
    private static float musicVolume = 1f;

    public static event Action<float> SfxVolumeChanged;
    public static event Action<float> MusicVolumeChanged;

    public static float SfxVolume
    {
        get
        {
            EnsureInitialized();
            return sfxVolume;
        }
    }

    public static float MusicVolume
    {
        get
        {
            EnsureInitialized();
            return musicVolume;
        }
    }

    public static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }

        float defaultVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(LegacyVolumePrefKey, 1f));
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumePrefKey, defaultVolume));
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumePrefKey, defaultVolume));
        initialized = true;
    }

    public static void SetSfxVolume(float value)
    {
        EnsureInitialized();

        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(sfxVolume, clamped))
        {
            return;
        }

        sfxVolume = clamped;
        PlayerPrefs.SetFloat(SfxVolumePrefKey, sfxVolume);
        PlayerPrefs.Save();
        SfxVolumeChanged?.Invoke(sfxVolume);
    }

    public static void SetMusicVolume(float value)
    {
        EnsureInitialized();

        float clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(musicVolume, clamped))
        {
            return;
        }

        musicVolume = clamped;
        PlayerPrefs.SetFloat(MusicVolumePrefKey, musicVolume);
        PlayerPrefs.Save();
        MusicVolumeChanged?.Invoke(musicVolume);
    }
}
