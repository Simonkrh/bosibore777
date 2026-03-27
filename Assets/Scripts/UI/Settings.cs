using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class MenuDisplaySettings : MonoBehaviour
{
    [Header("Panels")]
    [FormerlySerializedAs("mainMenuPanel")]
    [SerializeField] private GameObject panelToHideWhenOpen;
    [FormerlySerializedAs("settingsPanel")]
    [SerializeField] private GameObject settingsPanelRoot;

    [Header("UI")]
    [SerializeField] private Slider soundSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    [Header("Resolution")]
    [SerializeField] private bool include16By9Resolutions = true;
    [SerializeField] private bool include16By10Resolutions = true;
    [FormerlySerializedAs("resolutionMode")]
    [SerializeField] private FullScreenMode windowedMode = FullScreenMode.Windowed;
    [SerializeField] private FullScreenMode fullscreenMode = FullScreenMode.FullScreenWindow;
    [Tooltip("If no saved resolution exists, keep whatever resolution the game launched with and save it.")]
    [SerializeField] private bool saveLaunchResolutionWhenNoPreference = true;

    private const string FullscreenPrefKey = "settings.fullscreen";
    private const string ResolutionWidthPrefKey = "settings.resolution.width";
    private const string ResolutionHeightPrefKey = "settings.resolution.height";
    private const float Aspect16By9 = 16f / 9f;
    private const float Aspect16By10 = 16f / 10f;
    private const float AspectTolerance = 0.015f;

    private readonly List<Vector2Int> resolutionOptions = new List<Vector2Int>();
    private int pendingResolutionIndex = -1;
    private int appliedResolutionIndex = -1;
    private bool isFullscreenEnabled;

    private void Awake()
    {
        BuildResolutionList();
        LoadFullscreenPreference();
        ApplySavedOrCaptureLaunchResolution();
        ApplySavedVolume();
        SyncResolutionSelectionFromCurrentScreen();
        RefreshUiValues();
    }

    private void OnEnable()
    {
        SubscribeUi();
        RefreshUiValues();
    }

    private void OnDisable()
    {
        UnsubscribeUi();
    }

    public void OpenSettings()
    {
        GameObject targetPanel = settingsPanelRoot != null ? settingsPanelRoot : gameObject;
        if (panelToHideWhenOpen != null && !ContainsTargetPanel(panelToHideWhenOpen, targetPanel))
        {
            panelToHideWhenOpen.SetActive(false);
        }

        SetActiveWithParents(targetPanel, true);
        NormalizePanelRectTransform(targetPanel);
        PromotePanelVisibility(targetPanel);
    }

    public void CloseSettings()
    {
        GameObject targetPanel = settingsPanelRoot != null ? settingsPanelRoot : gameObject;
        targetPanel.SetActive(false);

        if (panelToHideWhenOpen != null && !ContainsTargetPanel(panelToHideWhenOpen, targetPanel))
        {
            panelToHideWhenOpen.SetActive(true);
        }
    }

    public void ToggleSettings()
    {
        GameObject targetPanel = settingsPanelRoot != null ? settingsPanelRoot : gameObject;
        if (targetPanel.activeSelf)
        {
            CloseSettings();
        }
        else
        {
            OpenSettings();
        }
    }

    public void OnSoundSliderChanged(float value)
    {
        AudioSettingsStore.SetSfxVolume(value);
    }

    public void OnMusicSliderChanged(float value)
    {
        AudioSettingsStore.SetMusicVolume(value);
    }

    public void OnResolutionDropdownChanged(int index)
    {
        SetResolutionByIndex(index);
    }

    public void OnFullscreenToggleChanged(bool isOn)
    {
        isFullscreenEnabled = isOn;
        PlayerPrefs.SetInt(FullscreenPrefKey, isOn ? 1 : 0);
        PlayerPrefs.Save();

        ApplyResolution(Screen.width, Screen.height, persistPreference: false);
        RefreshUiValues();
    }

    // Set pending choice only; call ApplySelectedResolution() from your Apply button.
    public void SetResolutionByIndex(int index)
    {
        if (index < 0 || index >= resolutionOptions.Count)
        {
            Debug.LogWarning("[MenuDisplaySettings] Invalid resolution index: " + index);
            return;
        }

        pendingResolutionIndex = index;
    }

    public void ApplySelectedResolution()
    {
        if (pendingResolutionIndex < 0 || pendingResolutionIndex >= resolutionOptions.Count)
        {
            Debug.LogWarning("[MenuDisplaySettings] No valid pending resolution to apply.");
            return;
        }

        Vector2Int selected = resolutionOptions[pendingResolutionIndex];
        ApplyResolution(selected.x, selected.y, persistPreference: true);
        appliedResolutionIndex = pendingResolutionIndex;
        RefreshUiValues();
    }

    public void RebuildResolutionDropdown()
    {
        BuildResolutionList();
        SyncResolutionSelectionFromCurrentScreen();
        RefreshUiValues();
    }

    private void ApplySavedVolume()
    {
        AudioSettingsStore.EnsureInitialized();
    }

    private void LoadFullscreenPreference()
    {
        if (PlayerPrefs.HasKey(FullscreenPrefKey))
        {
            isFullscreenEnabled = PlayerPrefs.GetInt(FullscreenPrefKey, 0) == 1;
            return;
        }

        isFullscreenEnabled = Screen.fullScreen;
    }

    private void ApplySavedOrCaptureLaunchResolution()
    {
        int savedWidth = PlayerPrefs.GetInt(ResolutionWidthPrefKey, -1);
        int savedHeight = PlayerPrefs.GetInt(ResolutionHeightPrefKey, -1);

        if (savedWidth > 0 && savedHeight > 0)
        {
            ApplyResolution(savedWidth, savedHeight, persistPreference: false);
            return;
        }

        if (saveLaunchResolutionWhenNoPreference)
        {
            PlayerPrefs.SetInt(ResolutionWidthPrefKey, Screen.width);
            PlayerPrefs.SetInt(ResolutionHeightPrefKey, Screen.height);
            PlayerPrefs.Save();
        }

        // Keep launch width/height, but still enforce fullscreen/windowed preference.
        ApplyResolution(Screen.width, Screen.height, persistPreference: false);
    }

    private void BuildResolutionList()
    {
        resolutionOptions.Clear();
        HashSet<long> seen = new HashSet<long>();
        Resolution[] allResolutions = Screen.resolutions;

        for (int i = 0; i < allResolutions.Length; i++)
        {
            int width = allResolutions[i].width;
            int height = allResolutions[i].height;
            if (width <= 0 || height <= 0)
            {
                continue;
            }

            if (!IsAllowedAspect(width, height))
            {
                continue;
            }

            long key = ((long)width << 32) | (uint)height;
            if (!seen.Add(key))
            {
                continue;
            }

            resolutionOptions.Add(new Vector2Int(width, height));
        }

        if (resolutionOptions.Count == 0)
        {
            resolutionOptions.Add(new Vector2Int(Screen.width, Screen.height));
        }

        resolutionOptions.Sort((a, b) =>
        {
            int widthCompare = a.x.CompareTo(b.x);
            return widthCompare != 0 ? widthCompare : a.y.CompareTo(b.y);
        });
    }

    private void SyncResolutionSelectionFromCurrentScreen()
    {
        int index = FindResolutionIndex(Screen.width, Screen.height);
        if (index < 0)
        {
            index = 0;
        }

        appliedResolutionIndex = index;
        if (pendingResolutionIndex < 0 || pendingResolutionIndex >= resolutionOptions.Count)
        {
            pendingResolutionIndex = appliedResolutionIndex;
        }
    }

    private int FindResolutionIndex(int width, int height)
    {
        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            Vector2Int option = resolutionOptions[i];
            if (option.x == width && option.y == height)
            {
                return i;
            }
        }

        return -1;
    }

    private void RefreshUiValues()
    {
        if (soundSlider != null)
        {
            soundSlider.SetValueWithoutNotify(AudioSettingsStore.SfxVolume);
        }

        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(AudioSettingsStore.MusicVolume);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(isFullscreenEnabled);
        }

        if (resolutionDropdown == null)
        {
            return;
        }

        List<string> labels = new List<string>(resolutionOptions.Count);
        for (int i = 0; i < resolutionOptions.Count; i++)
        {
            Vector2Int option = resolutionOptions[i];
            labels.Add(option.x + " x " + option.y);
        }

        int shownIndex = pendingResolutionIndex;
        if (shownIndex < 0 || shownIndex >= resolutionOptions.Count)
        {
            shownIndex = appliedResolutionIndex >= 0 ? appliedResolutionIndex : 0;
        }

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(labels);
        resolutionDropdown.SetValueWithoutNotify(shownIndex);
        resolutionDropdown.RefreshShownValue();
    }

    private void ApplyResolution(int width, int height, bool persistPreference)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Screen.SetResolution(width, height, GetActiveResolutionMode());

        if (!persistPreference)
        {
            return;
        }

        PlayerPrefs.SetInt(ResolutionWidthPrefKey, width);
        PlayerPrefs.SetInt(ResolutionHeightPrefKey, height);
        PlayerPrefs.Save();
    }

    private void SubscribeUi()
    {
        if (soundSlider != null)
        {
            soundSlider.onValueChanged.AddListener(OnSoundSliderChanged);
        }

        if (musicSlider != null)
        {
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.AddListener(OnResolutionDropdownChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggleChanged);
        }
    }

    private void UnsubscribeUi()
    {
        if (soundSlider != null)
        {
            soundSlider.onValueChanged.RemoveListener(OnSoundSliderChanged);
        }

        if (musicSlider != null)
        {
            musicSlider.onValueChanged.RemoveListener(OnMusicSliderChanged);
        }

        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.RemoveListener(OnResolutionDropdownChanged);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenToggleChanged);
        }
    }

    private FullScreenMode GetActiveResolutionMode()
    {
        return isFullscreenEnabled ? fullscreenMode : windowedMode;
    }

    private bool IsAllowedAspect(int width, int height)
    {
        // If both toggles are off, allow all resolutions.
        if (!include16By9Resolutions && !include16By10Resolutions)
        {
            return true;
        }

        bool is16By9 = IsAspect(width, height, Aspect16By9);
        bool is16By10 = IsAspect(width, height, Aspect16By10);
        return (include16By9Resolutions && is16By9) || (include16By10Resolutions && is16By10);
    }

    private static bool IsAspect(int width, int height, float targetAspect)
    {
        float aspect = width / (float)height;
        return Mathf.Abs(aspect - targetAspect) <= AspectTolerance;
    }

    private static bool ContainsTargetPanel(GameObject candidateParent, GameObject targetPanel)
    {
        if (candidateParent == null || targetPanel == null)
        {
            return false;
        }

        Transform candidateTransform = candidateParent.transform;
        Transform targetTransform = targetPanel.transform;
        return targetTransform == candidateTransform || targetTransform.IsChildOf(candidateTransform);
    }

    private static void SetActiveWithParents(GameObject target, bool isActive)
    {
        if (target == null)
        {
            return;
        }

        if (!isActive)
        {
            target.SetActive(false);
            return;
        }

        Transform current = target.transform;
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }

            current = current.parent;
        }
    }

    private static void PromotePanelVisibility(GameObject targetPanel)
    {
        if (targetPanel == null)
        {
            return;
        }

        Transform targetTransform = targetPanel.transform;
        if (targetTransform.parent != null)
        {
            targetTransform.SetAsLastSibling();
        }

        Canvas targetCanvas = targetPanel.GetComponent<Canvas>();
        if (targetCanvas != null)
        {
            targetCanvas.overrideSorting = true;
            targetCanvas.sortingOrder = 1000;
        }

        Canvas.ForceUpdateCanvases();
    }

    private static void NormalizePanelRectTransform(GameObject targetPanel)
    {
        if (targetPanel == null)
        {
            return;
        }

        RectTransform rectTransform = targetPanel.transform as RectTransform;
        if (rectTransform == null)
        {
            return;
        }

        bool hasCollapsedScale =
            Mathf.Abs(rectTransform.localScale.x) < 0.01f ||
            Mathf.Abs(rectTransform.localScale.y) < 0.01f ||
            Mathf.Abs(rectTransform.localScale.z) < 0.01f;

        if (rectTransform.parent == null && !hasCollapsedScale)
        {
            return;
        }

        rectTransform.localScale = Vector3.one;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = Vector2.zero;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
