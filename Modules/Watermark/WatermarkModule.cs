using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private const float WatermarkUiScaleOffset = -2f;
    private const int BaseUiFontSize = 13;
    private readonly ElinModifierPlugin _host;
    private bool _showElinModifierWatermark = true;
    private bool _watermarkPositionLocked;
    private float _watermarkPositionX;
    private float _watermarkPositionY = -10f;
    private GameObject? _watermarkRoot;
    private Canvas? _watermarkCanvas;
    private CanvasScaler? _watermarkCanvasScaler;
    private RectTransform? _watermarkBar;
    private RectTransform? _watermarkAccentRect;
    private Image? _watermarkBackground;
    private Image? _watermarkAccent;
    private Text? _watermarkText;
    private Texture2D? _watermarkCapsuleTexture;
    private Sprite? _watermarkCapsuleSprite;
    private float _watermarkCapsuleHeight;
    private float _watermarkTargetWidth;
    private float _watermarkTargetHeight;
    private float _watermarkCurrentWidth;
    private float _watermarkCurrentHeight;
    private float _watermarkWidthVelocity;
    private float _watermarkHeightVelocity;
    private float _watermarkLayoutLastTickAt;
    private float _watermarkNextRefreshAt;
    private float _watermarkNextPositionCheckAt;
    private float _watermarkFpsSampleAt;
    private int _watermarkFpsSampleFrame;
    private int _watermarkFps;
    private bool _watermarkConfigDirty;

    internal WatermarkModule(ElinModifierPlugin host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    private bool _adaptiveUiScale => _host.ModuleAdaptiveUiScale;
    private Font? _lGuiFont
    {
        get => _host.ModuleLGuiFont;
        set => _host.ModuleLGuiFont = value;
    }
    private bool _lGuiModalRestoreMainOnClose
    {
        get => _host.ModuleLGuiModalRestoreMainOnClose;
        set => _host.ModuleLGuiModalRestoreMainOnClose = value;
    }
    private GameObject? _lGuiRoot => _host.ModuleLGuiRoot;
    private bool _lGuiVisible
    {
        get => _host.ModuleLGuiVisible;
        set => _host.ModuleLGuiVisible = value;
    }
    private float _uiAlpha => _host.ModuleUiAlpha;
    private bool _uiRoundedCorners => _host.ModuleUiRoundedCorners;
    private int _uiStyleIndex => _host.ModuleUiStyleIndex;

    internal bool Enabled => _showElinModifierWatermark;
    internal bool PositionLocked => _watermarkPositionLocked;
    internal bool GameErrorNotificationEnabled => _watermarkGameErrorNotification;
    internal bool SuppressWarningNotificationEnabled => _watermarkSuppressWarningNotification;
    internal float PositionX => _watermarkPositionX;
    internal float PositionY => _watermarkPositionY;
    internal bool ConfigDirty => _watermarkConfigDirty;

    internal void LoadSettings(
        bool enabled,
        bool positionLocked,
        bool gameErrorNotification,
        bool suppressWarningNotification,
        float positionX,
        float positionY)
    {
        _watermarkPositionLocked = positionLocked;
        _watermarkGameErrorNotification = gameErrorNotification;
        _watermarkSuppressWarningNotification = suppressWarningNotification;
        _watermarkPositionX = positionX;
        _watermarkPositionY = positionY;
        _watermarkConfigDirty = false;
        SetEnabled(enabled);
    }

    internal void ResetSettings()
    {
        _watermarkPositionLocked = false;
        _watermarkGameErrorNotification = true;
        _watermarkSuppressWarningNotification = true;
        _watermarkPositionX = 0f;
        _watermarkPositionY = -10f;
        _watermarkConfigDirty = false;
        SetEnabled(true);
    }

    internal void SetPositionLocked(bool value)
    {
        if (_watermarkPositionLocked == value)
            return;
        _watermarkPositionLocked = value;
        _watermarkConfigDirty = true;
    }

    private string T(string zh, string en) => _host.TranslateModuleText(zh, en);
    private void EnsureLGuiEventSystem() => _host.EnsureModuleLGuiEventSystem();
    private Font FindLGuiFont() => _host.FindModuleLGuiFont();
    private RectTransform CreateLGuiRect(Transform parent, string name) => _host.CreateModuleLGuiRect(parent, name);
    private Text CreateLGuiText(Transform parent, string name, string value, int size, TextAnchor anchor, FontStyle style) =>
        _host.CreateModuleLGuiText(parent, name, value, size, anchor, style);
    private static void StretchLGuiRect(RectTransform rect, float left, float bottom, float right, float top) =>
        ElinModifierPlugin.StretchModuleLGuiRect(rect, left, bottom, right, top);
    private float GetCustomUiScaleFactor() => _host.GetModuleCustomUiScaleFactor();
    private Color GetActiveUiTextColor() => _host.ModuleActiveUiTextColor;
    private int GetEffectiveUiFontSize() => _host.ModuleEffectiveUiFontSize;
    private void ApplyLGuiRegisteredCornerStyles(GameObject? root) => _host.ApplyModuleLGuiRegisteredCornerStyles(root);
    private bool IsLGuiInitialized() => _host.IsModuleLGuiInitialized();
    private void InitializeLGui() => _host.InitializeModuleLGui();
    private bool IsLGuiVisible() => _host.IsModuleLGuiVisible();
    private RectTransform CreateLGuiCompleteModal(
        string name,
        string title,
        out RectTransform content,
        float width = 1540f,
        float height = 980f) =>
        _host.CreateModuleLGuiCompleteModal(name, title, out content, width, height);
    private void CreateLGuiToggleControl(Transform parent, string label, bool value, float y, Action<bool> changed) =>
        _host.CreateModuleLGuiToggleControl(parent, label, value, y, changed);
    private Button CreateLGuiButton(
        Transform parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        float height,
        Action? action) =>
        _host.CreateModuleLGuiButton(parent, name, label, x, y, width, height, action);
    private void ApplyLGuiVisualSettings() => _host.ApplyModuleLGuiVisualSettings();
    private void SaveConfig(bool updateLog) => _host.SaveConfigFromModule(updateLog, false);
    private string ExtractDebugStackTraceFromLogText(string text) => _host.ExtractModuleDebugStackTrace(text);
    private Sprite? GetStandardRoundedSprite() => _host.GetModuleStandardRoundedSprite();
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

    internal void SetEnabled(bool value)
    {
        _showElinModifierWatermark = value;
        RefreshWatermarkErrorCapture();
        if (!value)
        {
            DismissWatermarkErrorNotification(true);
            Shutdown();
            return;
        }

        EnsureWatermark();
        if (_watermarkRoot != null)
            _watermarkRoot.SetActive(true);
        _watermarkNextRefreshAt = 0f;
        _watermarkNextPositionCheckAt = Time.realtimeSinceStartup + 5f;
        _watermarkLayoutLastTickAt = Time.realtimeSinceStartup;
        _watermarkFpsSampleAt = Time.realtimeSinceStartup;
        _watermarkFpsSampleFrame = Time.frameCount;
        if (Time.unscaledDeltaTime > 0.00001f)
            _watermarkFps = Mathf.Max(0, Mathf.RoundToInt(1f / Time.unscaledDeltaTime));
        RefreshText();
        ApplyVisualSettings();
    }

    internal void Initialize()
    {
        if (_showElinModifierWatermark)
            SetEnabled(true);
    }

    internal void Tick()
    {
        var now = Time.realtimeSinceStartup;
        if (now >= _watermarkNextPositionCheckAt)
        {
            _watermarkNextPositionCheckAt = now + 5f;
            PersistIfChanged(false);
        }
        if (!_showElinModifierWatermark)
            return;

        EnsureWatermark();
        if (_watermarkRoot == null)
            return;
        if (!_watermarkRoot.activeSelf)
            _watermarkRoot.SetActive(true);
        if (EventSystem.current == null)
            EnsureLGuiEventSystem();

        if (now >= _watermarkNextRefreshAt)
        {
            UpdateWatermarkFps(now);
            _watermarkNextRefreshAt = now + 1f;
            RefreshText();
        }
        TickWatermarkLayout(now);
        TickWatermarkErrorNotification(now);
    }

    private void UpdateWatermarkFps(float now)
    {
        var elapsed = now - _watermarkFpsSampleAt;
        if (elapsed > 0.05f)
            _watermarkFps = Mathf.Max(0, Mathf.RoundToInt((Time.frameCount - _watermarkFpsSampleFrame) / elapsed));
        else if (Time.unscaledDeltaTime > 0.00001f)
            _watermarkFps = Mathf.Max(0, Mathf.RoundToInt(1f / Time.unscaledDeltaTime));
        _watermarkFpsSampleAt = now;
        _watermarkFpsSampleFrame = Time.frameCount;
    }

    private void EnsureWatermark()
    {
        if (_watermarkRoot != null)
            return;

        try
        {
            if (_lGuiFont == null)
                _lGuiFont = FindLGuiFont();

            _watermarkRoot = new GameObject(
                "ElinModifier.RuntimeWatermark",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_watermarkRoot);

            _watermarkCanvas = _watermarkRoot.GetComponent<Canvas>();
            _watermarkCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _watermarkCanvas.sortingOrder = 31990;

            _watermarkCanvasScaler = _watermarkRoot.GetComponent<CanvasScaler>();
            _watermarkCanvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _watermarkCanvasScaler.referenceResolution = new Vector2(2560f, 1440f);
            _watermarkCanvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _watermarkBar = CreateLGuiRect(_watermarkRoot.transform, "WatermarkBar");
            _watermarkBar.anchorMin = new Vector2(0.5f, 1f);
            _watermarkBar.anchorMax = new Vector2(0.5f, 1f);
            _watermarkBar.pivot = new Vector2(0.5f, 1f);
            _watermarkBar.sizeDelta = new Vector2(660f, 42f);
            _watermarkBar.anchoredPosition = new Vector2(_watermarkPositionX, _watermarkPositionY);

            _watermarkBackground = _watermarkBar.gameObject.AddComponent<Image>();
            _watermarkBackground.raycastTarget = true;
            _watermarkBackground.type = Image.Type.Sliced;
            _watermarkBackground.fillCenter = true;
            _watermarkBar.gameObject.AddComponent<RectMask2D>();
            var drag = _watermarkBar.gameObject.AddComponent<WatermarkDragHandle>();
            drag.Initialize(
                _watermarkBar,
                _watermarkCanvas,
                () => _watermarkPositionLocked,
                () => _watermarkConfigDirty = true);

            _watermarkAccentRect = CreateLGuiRect(_watermarkBar, "Accent");
            _watermarkAccentRect.anchorMin = new Vector2(0f, 0f);
            _watermarkAccentRect.anchorMax = new Vector2(1f, 0f);
            _watermarkAccentRect.pivot = new Vector2(0.5f, 0f);
            _watermarkAccentRect.offsetMin = new Vector2(21f, 0f);
            _watermarkAccentRect.offsetMax = new Vector2(-21f, 2f);
            _watermarkAccent = _watermarkAccentRect.gameObject.AddComponent<Image>();
            _watermarkAccent.raycastTarget = false;

            _watermarkText = CreateLGuiText(
                _watermarkBar,
                "WatermarkText",
                "",
                18,
                TextAnchor.MiddleCenter,
                FontStyle.Normal);
            StretchLGuiRect(_watermarkText.rectTransform, 18f, 2f, 18f, 3f);
            _watermarkText.raycastTarget = false;
            _watermarkText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _watermarkText.verticalOverflow = VerticalWrapMode.Truncate;

            RefreshText();
            ApplyVisualSettings();
        }
        catch
        {
            Shutdown();
        }
    }

    internal void RefreshText()
    {
        if (_watermarkText == null)
            return;

        var version = ModMetadata.Version;
        try
        {
            var detected = _host.ModulePluginVersion;
            if (!string.IsNullOrWhiteSpace(detected))
                version = detected.Trim().TrimStart('v', 'V');
        }
        catch { }

        _watermarkText.text = "Elin Modifier v" + version +
                              " | " + T("帧数", "FPS") + ": " + _watermarkFps.ToString(CultureInfo.InvariantCulture) +
                              " | " + T("游戏时间", "Game time") + ": " + GetWatermarkGameTime() +
                              " | " + T("现实时间", "Real time") + ": " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        UpdateWatermarkLayout();
    }

    private static string GetWatermarkGameTime()
    {
        try
        {
            if (!GameAccess.IsInitialized || !GameAccess.Clock.TryGetCurrent(out var value))
                return "--";

            return value.Year.ToString(CultureInfo.InvariantCulture) + "/" +
                   value.Month.ToString(CultureInfo.InvariantCulture) + "/" +
                   value.Day.ToString(CultureInfo.InvariantCulture) + " " +
                   value.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" +
                   value.Minute.ToString("00", CultureInfo.InvariantCulture);
        }
        catch
        {
            return "--";
        }
    }

    internal void ApplyVisualSettings()
    {
        if (_watermarkRoot == null)
            return;

        if (_watermarkCanvasScaler != null)
        {
            _watermarkCanvasScaler.uiScaleMode = _adaptiveUiScale
                ? CanvasScaler.ScaleMode.ScaleWithScreenSize
                : CanvasScaler.ScaleMode.ConstantPixelSize;
            _watermarkCanvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            _watermarkCanvasScaler.scaleFactor = _adaptiveUiScale ? 1f : GetCustomUiScaleFactor();
        }
        if (_watermarkBar != null)
            _watermarkBar.localScale = Vector3.one * GetWatermarkRelativeScale();

        var lightTheme = _uiStyleIndex == 5;
        var accent = _host.ModuleUiStyleColor;
        var alpha = Clamp(_uiAlpha * 0.92f, 0.45f, 0.92f);
        if (_watermarkBackground != null)
        {
            _watermarkBackground.color = lightTheme
                ? new Color(0.94f, 0.94f, 0.91f, alpha)
                : new Color(0.035f, 0.04f, 0.05f, alpha);
        }
        if (_watermarkAccent != null)
            _watermarkAccent.color = new Color(accent.r, accent.g, accent.b, 0.92f);
        if (_watermarkText != null)
        {
            _watermarkText.color = GetActiveUiTextColor();
            var fontScale = GetEffectiveUiFontSize() / (float)BaseUiFontSize;
            _watermarkText.fontSize = Clamp(Mathf.RoundToInt(18f * fontScale), 1, 60);
        }
        UpdateWatermarkLayout();
        ApplyWatermarkErrorVisualSettings();
        ApplyLGuiRegisteredCornerStyles(_watermarkRoot);
    }

    internal void RefreshFont(Font font)
    {
        if (_watermarkRoot == null || font == null)
            return;

        var texts = _watermarkRoot.GetComponentsInChildren<Text>(true);
        for (var i = 0; i < texts.Length; i++)
            if (texts[i] != null)
                texts[i].font = font;
        ApplyVisualSettings();
    }

    private void UpdateWatermarkLayout()
    {
        if (_watermarkBar == null || _watermarkText == null)
            return;

        var textHeight = Mathf.Max(1f, _watermarkText.preferredHeight);
        var barHeight = Mathf.Max(42f, Mathf.Ceil(textHeight + 14f));
        barHeight = Mathf.Ceil(barHeight * 0.5f) * 2f;
        var leftPadding = Mathf.Max(24f, barHeight * 0.62f);
        var rightPadding = leftPadding;
        var barWidth = Mathf.Max(520f, Mathf.Ceil(_watermarkText.preferredWidth + leftPadding + rightPadding));

        _watermarkTargetWidth = barWidth;
        _watermarkTargetHeight = barHeight;
        if (_watermarkCurrentWidth <= 0f || _watermarkCurrentHeight <= 0f)
        {
            _watermarkCurrentWidth = barWidth;
            _watermarkCurrentHeight = barHeight;
            _watermarkWidthVelocity = 0f;
            _watermarkHeightVelocity = 0f;
        }
        ApplyWatermarkLayoutGeometry(_watermarkCurrentWidth, _watermarkCurrentHeight);
    }

    private void TickWatermarkLayout(float now)
    {
        if (_watermarkBar == null || _watermarkTargetWidth <= 0f || _watermarkTargetHeight <= 0f)
            return;

        var deltaTime = _watermarkLayoutLastTickAt <= 0f
            ? Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f)
            : Mathf.Clamp(now - _watermarkLayoutLastTickAt, 0f, 0.1f);
        _watermarkLayoutLastTickAt = now;
        if (deltaTime <= 0f)
            return;

        var previousWidth = _watermarkCurrentWidth;
        var previousHeight = _watermarkCurrentHeight;
        _watermarkCurrentWidth = Mathf.SmoothDamp(
            _watermarkCurrentWidth,
            _watermarkTargetWidth,
            ref _watermarkWidthVelocity,
            0.18f,
            Mathf.Infinity,
            deltaTime);
        _watermarkCurrentHeight = Mathf.SmoothDamp(
            _watermarkCurrentHeight,
            _watermarkTargetHeight,
            ref _watermarkHeightVelocity,
            0.18f,
            Mathf.Infinity,
            deltaTime);

        if (Mathf.Abs(_watermarkCurrentWidth - _watermarkTargetWidth) < 0.05f)
            _watermarkCurrentWidth = _watermarkTargetWidth;
        if (Mathf.Abs(_watermarkCurrentHeight - _watermarkTargetHeight) < 0.05f)
            _watermarkCurrentHeight = _watermarkTargetHeight;
        if (Mathf.Abs(previousWidth - _watermarkCurrentWidth) > 0.001f ||
            Mathf.Abs(previousHeight - _watermarkCurrentHeight) > 0.001f)
            ApplyWatermarkLayoutGeometry(_watermarkCurrentWidth, _watermarkCurrentHeight);
    }

    private void ApplyWatermarkLayoutGeometry(float barWidth, float barHeight)
    {
        if (_watermarkBar == null || _watermarkText == null)
            return;

        barWidth = Mathf.Max(1f, barWidth);
        barHeight = Mathf.Max(1f, barHeight);
        _watermarkBar.sizeDelta = new Vector2(barWidth, barHeight);
        var leftPadding = Mathf.Max(24f, barHeight * 0.62f);
        var rightPadding = leftPadding;

        StretchLGuiRect(_watermarkText.rectTransform, leftPadding, 3f, rightPadding, 4f);
        if (_watermarkAccentRect != null)
        {
            var capRadius = barHeight * 0.5f;
            _watermarkAccentRect.offsetMin = new Vector2(capRadius, 0f);
            _watermarkAccentRect.offsetMax = new Vector2(-capRadius, 2f);
        }

        if (_watermarkCapsuleSprite == null ||
            Mathf.Abs(_watermarkCurrentHeight - _watermarkTargetHeight) <= 0.5f)
            EnsureWatermarkCapsuleSprite(barHeight);
        if (_watermarkBackground != null && _watermarkCapsuleSprite != null)
        {
            _watermarkBackground.sprite = _watermarkCapsuleSprite;
            _watermarkBackground.type = Image.Type.Sliced;
            _watermarkBackground.fillCenter = true;
        }
    }

    private void EnsureWatermarkCapsuleSprite(float barHeight)
    {
        if (_watermarkCapsuleSprite != null && Mathf.Abs(_watermarkCapsuleHeight - barHeight) < 0.5f)
            return;

        if (_watermarkCapsuleSprite != null)
            UnityEngine.Object.Destroy(_watermarkCapsuleSprite);
        if (_watermarkCapsuleTexture != null)
            UnityEngine.Object.Destroy(_watermarkCapsuleTexture);

        var diameter = Clamp(Mathf.RoundToInt(barHeight), 24, 120);
        if ((diameter & 1) != 0)
            diameter++;
        var radius = diameter * 0.5f;
        var width = diameter + 4;
        var height = diameter + 2;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = "ElinModifier.WatermarkCapsule";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.DontSave;

        var pixels = new Color[width * height];
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var px = Mathf.Abs(x + 0.5f - halfWidth) - (halfWidth - radius);
                var py = Mathf.Abs(y + 0.5f - halfHeight) - (halfHeight - radius);
                var outsideX = Mathf.Max(px, 0f);
                var outsideY = Mathf.Max(py, 0f);
                var distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) +
                               Mathf.Min(Mathf.Max(px, py), 0f) - radius;
                pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);

        var border = new Vector4(radius, radius, radius, radius);
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            border);
        sprite.name = "ElinModifier.WatermarkCapsule";
        sprite.hideFlags = HideFlags.DontSave;
        _watermarkCapsuleTexture = texture;
        _watermarkCapsuleSprite = sprite;
        _watermarkCapsuleHeight = diameter;
    }

    internal void OpenSettings()
    {
        if (!IsLGuiInitialized())
            InitializeLGui();
        if (!IsLGuiInitialized() || _lGuiRoot == null)
            return;

        var restoreMain = IsLGuiVisible();
        if (!restoreMain)
        {
            _lGuiVisible = true;
            _lGuiRoot.SetActive(true);
            EnsureLGuiEventSystem();
            _host.ShowModuleLGuiRootImmediate();
        }

        var modal = CreateLGuiCompleteModal(
            "RuntimeWatermarkSettings",
            T("水印设置", "Watermark settings"),
            out var content,
            760f,
            420f);
        if (modal == null)
            return;
        _lGuiModalRestoreMainOnClose = restoreMain;

        CreateLGuiToggleControl(
            content,
            T("锁定UI位置", "Lock UI position"),
            _watermarkPositionLocked,
            10f,
            value =>
            {
                _watermarkPositionLocked = value;
                _watermarkConfigDirty = true;
            });

        CreateLGuiToggleControl(
            content,
            T("游戏报错提醒", "Game error notifications"),
            _watermarkGameErrorNotification,
            68f,
            SetGameErrorNotification);

        CreateLGuiToggleControl(
            content,
            T("屏蔽Warning级报错提醒", "Suppress Warning notifications"),
            _watermarkSuppressWarningNotification,
            126f,
            SetSuppressWarningNotification);

        CreateLGuiButton(
            content,
            "ResetWatermarkPosition",
            T("重置UI位置", "Reset UI position"),
            0f,
            194f,
            190f,
            48f,
            ResetPosition);
        content.sizeDelta = new Vector2(0f, 268f);
        ApplyLGuiVisualSettings();
    }

    internal void ResetPosition()
    {
        _watermarkPositionX = 0f;
        _watermarkPositionY = -10f;
        if (_watermarkBar != null)
            _watermarkBar.anchoredPosition = new Vector2(_watermarkPositionX, _watermarkPositionY);
        _watermarkConfigDirty = true;
        PersistIfChanged(true);
    }

    internal void PersistIfChanged(bool force)
    {
        var changed = _watermarkConfigDirty;
        if (_watermarkBar != null)
        {
            var position = _watermarkBar.anchoredPosition;
            if (Mathf.Abs(position.x - _watermarkPositionX) > 0.01f ||
                Mathf.Abs(position.y - _watermarkPositionY) > 0.01f)
            {
                _watermarkPositionX = position.x;
                _watermarkPositionY = position.y;
                changed = true;
            }
        }

        if (!force && !changed)
            return;
        _watermarkConfigDirty = false;
        SaveConfig(false);
    }

    internal void Shutdown()
    {
        ShutdownWatermarkErrorNotification();
        if (_watermarkCapsuleSprite != null)
            UnityEngine.Object.Destroy(_watermarkCapsuleSprite);
        if (_watermarkCapsuleTexture != null)
            UnityEngine.Object.Destroy(_watermarkCapsuleTexture);
        if (_watermarkRoot != null)
            UnityEngine.Object.Destroy(_watermarkRoot);
        _watermarkRoot = null;
        _watermarkCanvas = null;
        _watermarkCanvasScaler = null;
        _watermarkBar = null;
        _watermarkAccentRect = null;
        _watermarkBackground = null;
        _watermarkAccent = null;
        _watermarkText = null;
        _watermarkCapsuleSprite = null;
        _watermarkCapsuleTexture = null;
        _watermarkCapsuleHeight = 0f;
        _watermarkTargetWidth = 0f;
        _watermarkTargetHeight = 0f;
        _watermarkCurrentWidth = 0f;
        _watermarkCurrentHeight = 0f;
        _watermarkWidthVelocity = 0f;
        _watermarkHeightVelocity = 0f;
        _watermarkLayoutLastTickAt = 0f;
        _watermarkNextRefreshAt = 0f;
        _watermarkNextPositionCheckAt = 0f;
        _watermarkFpsSampleAt = 0f;
        _watermarkFpsSampleFrame = 0;
        _watermarkFps = 0;
    }
}

internal sealed class WatermarkDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler
{
    private RectTransform? _target;
    private Canvas? _canvas;
    private Func<bool>? _isLocked;
    private Action? _moved;
    private Vector2 _offset;

    public void Initialize(RectTransform target, Canvas canvas, Func<bool> isLocked, Action moved)
    {
        _target = target;
        _canvas = canvas;
        _isLocked = isLocked;
        _moved = moved;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_target == null || _canvas == null || (_isLocked?.Invoke() ?? false))
            return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_target.parent,
                eventData.position,
                _canvas.worldCamera,
                out var local))
            _offset = _target.anchoredPosition - local;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_target == null || _canvas == null || (_isLocked?.Invoke() ?? false))
            return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_target.parent,
                eventData.position,
                _canvas.worldCamera,
                out var local))
            return;

        _target.anchoredPosition = ClampToParent(local + _offset);
        _moved?.Invoke();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_isLocked?.Invoke() ?? false)
            return;
        _moved?.Invoke();
    }

    private Vector2 ClampToParent(Vector2 position)
    {
        if (_target?.parent is not RectTransform parent)
            return position;
        var scale = Mathf.Max(0.01f, _target.localScale.x);
        var halfWidth = _target.rect.width * scale * 0.5f;
        var height = _target.rect.height * scale;
        var maxX = Mathf.Max(0f, parent.rect.width * 0.5f - halfWidth);
        position.x = Mathf.Clamp(position.x, -maxX, maxX);
        position.y = Mathf.Clamp(position.y, -(parent.rect.height - height), 0f);
        return position;
    }
}
