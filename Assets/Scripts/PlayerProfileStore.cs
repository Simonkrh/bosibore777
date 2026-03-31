using System;
using UnityEngine;

public static class PlayerProfileStore
{
    private const string SavedProfileIdPrefKey = "PlayerProfile.PersistentId";
    private const string SavedNamePrefKey = "PlayerProfile.DisplayName";
    private const string SavedColorRedPrefKey = "PlayerProfile.Color.R";
    private const string SavedColorGreenPrefKey = "PlayerProfile.Color.G";
    private const string SavedColorBluePrefKey = "PlayerProfile.Color.B";
    private const string SavedColorAlphaPrefKey = "PlayerProfile.Color.A";
    private const string HasInitializedProfilePrefKey = "PlayerProfile.Initialized";
    private const int MaxPlayerIdLength = 64;
    private const int MaxPlayerNameLength = 18;

    private static readonly System.Random random = new System.Random(
        Environment.TickCount ^ DateTime.UtcNow.Ticks.GetHashCode());

    public const string DefaultPlayerName = "Player";

    public static string LoadOrCreatePlayerId()
    {
        EnsureProfileInitialized();
        return SanitizePlayerId(PlayerPrefs.GetString(SavedProfileIdPrefKey, GeneratePlayerId()));
    }

    public static string LoadOrCreatePlayerName()
    {
        EnsureProfileInitialized();
        return SanitizePlayerName(PlayerPrefs.GetString(SavedNamePrefKey, DefaultPlayerName));
    }

    public static Color LoadOrCreatePlayerColor()
    {
        EnsureProfileInitialized();

        return new Color(
            Mathf.Clamp01(PlayerPrefs.GetFloat(SavedColorRedPrefKey, 1f)),
            Mathf.Clamp01(PlayerPrefs.GetFloat(SavedColorGreenPrefKey, 1f)),
            Mathf.Clamp01(PlayerPrefs.GetFloat(SavedColorBluePrefKey, 1f)),
            Mathf.Clamp01(PlayerPrefs.GetFloat(SavedColorAlphaPrefKey, 1f)));
    }

    public static void SavePlayerName(string rawName)
    {
        string sanitizedName = SanitizePlayerName(rawName);
        PlayerPrefs.SetString(SavedNamePrefKey, sanitizedName);
        PlayerPrefs.SetInt(HasInitializedProfilePrefKey, 1);
        PlayerPrefs.Save();
    }

    public static void SavePlayerColor(Color color)
    {
        Color sanitizedColor = SanitizePlayerColor(color);
        PlayerPrefs.SetFloat(SavedColorRedPrefKey, sanitizedColor.r);
        PlayerPrefs.SetFloat(SavedColorGreenPrefKey, sanitizedColor.g);
        PlayerPrefs.SetFloat(SavedColorBluePrefKey, sanitizedColor.b);
        PlayerPrefs.SetFloat(SavedColorAlphaPrefKey, sanitizedColor.a);
        PlayerPrefs.SetInt(HasInitializedProfilePrefKey, 1);
        PlayerPrefs.Save();
    }

    public static string SanitizePlayerName(string rawName)
    {
        string trimmedName = string.IsNullOrWhiteSpace(rawName)
            ? DefaultPlayerName
            : rawName.Trim();

        if (trimmedName.Length > MaxPlayerNameLength)
        {
            trimmedName = trimmedName.Substring(0, MaxPlayerNameLength).Trim();
        }

        return string.IsNullOrWhiteSpace(trimmedName) ? DefaultPlayerName : trimmedName;
    }

    public static string SanitizePlayerId(string rawId)
    {
        string trimmedId = string.IsNullOrWhiteSpace(rawId)
            ? string.Empty
            : rawId.Trim();

        if (trimmedId.Length > MaxPlayerIdLength)
        {
            trimmedId = trimmedId.Substring(0, MaxPlayerIdLength).Trim();
        }

        return string.IsNullOrWhiteSpace(trimmedId) ? GeneratePlayerId() : trimmedId;
    }

    public static Color SanitizePlayerColor(Color color)
    {
        return new Color(
            Mathf.Clamp01(color.r),
            Mathf.Clamp01(color.g),
            Mathf.Clamp01(color.b),
            1f);
    }

    public static Color GenerateRandomColor()
    {
        double hue = random.NextDouble();
        double saturation = 0.55d + random.NextDouble() * 0.35d;
        double value = 0.72d + random.NextDouble() * 0.23d;
        Color color = Color.HSVToRGB((float)hue, (float)saturation, (float)value);
        color.a = 1f;
        return color;
    }

    private static void EnsureProfileInitialized()
    {
        bool changed = false;
        if (PlayerPrefs.GetInt(HasInitializedProfilePrefKey, 0) != 1)
        {
            PlayerPrefs.SetInt(HasInitializedProfilePrefKey, 1);
            PlayerPrefs.SetString(SavedNamePrefKey, DefaultPlayerName);

            Color defaultColor = GenerateRandomColor();
            PlayerPrefs.SetFloat(SavedColorRedPrefKey, defaultColor.r);
            PlayerPrefs.SetFloat(SavedColorGreenPrefKey, defaultColor.g);
            PlayerPrefs.SetFloat(SavedColorBluePrefKey, defaultColor.b);
            PlayerPrefs.SetFloat(SavedColorAlphaPrefKey, defaultColor.a);
            changed = true;
        }

        if (!PlayerPrefs.HasKey(SavedProfileIdPrefKey))
        {
            PlayerPrefs.SetString(SavedProfileIdPrefKey, GeneratePlayerId());
            changed = true;
        }

        if (changed)
        {
            PlayerPrefs.Save();
        }
    }

    private static string GeneratePlayerId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
