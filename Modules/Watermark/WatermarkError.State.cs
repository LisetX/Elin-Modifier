using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal sealed partial class WatermarkModule
{
    private const float WatermarkErrorTimeoutSeconds = 10f;
    private const float WatermarkErrorNormalHeight = 42f;
    private bool _watermarkGameErrorNotification = true;
    private bool _watermarkSuppressWarningNotification = true;
    private bool _watermarkErrorCaptureInstalled;
    private WatermarkBepInExErrorListener? _watermarkBepInExErrorListener;
    private GameObject? _watermarkErrorRoot;
    private RectTransform? _watermarkErrorBar;
    private Image? _watermarkErrorBackground;
    private Texture2D? _watermarkErrorCapsuleTexture;
    private Sprite? _watermarkErrorCapsuleSprite;
    private CanvasGroup? _watermarkErrorCanvasGroup;
    private Text? _watermarkErrorSummaryText;
    private CanvasGroup? _watermarkErrorSummaryCanvasGroup;
    private RectTransform? _watermarkErrorViewport;
    private CanvasGroup? _watermarkErrorDetailCanvasGroup;
    private RectTransform? _watermarkErrorContent;
    private Text? _watermarkErrorDetailText;
    private ScrollRect? _watermarkErrorScrollRect;
    private bool _watermarkErrorActive;
    private bool _watermarkErrorDismissing;
    private bool _watermarkErrorExpanded;
    private bool _watermarkErrorHovered;
    private bool _watermarkErrorIsWarning;
    private bool _watermarkErrorBelow = true;
    private float _watermarkErrorRemainingSeconds;
    private float _watermarkErrorLastTickAt;
    private float _watermarkErrorCurrentX;
    private float _watermarkErrorCurrentY;
    private float _watermarkErrorCurrentWidth = 360f;
    private float _watermarkErrorCurrentHeight = WatermarkErrorNormalHeight;
    private float _watermarkErrorCurrentAlpha;
    private float _watermarkErrorVelocityX;
    private float _watermarkErrorVelocityY;
    private float _watermarkErrorVelocityWidth;
    private float _watermarkErrorVelocityHeight;
    private float _watermarkErrorVelocityAlpha;
    private float _watermarkErrorNormalWidth = 520f;
    private float _watermarkErrorExpandedHeight = 300f;
    private float _watermarkErrorLastDetailLayoutWidth = -1f;
    private int _watermarkErrorLastSummarySeconds = -1;
    private bool _watermarkErrorDetailLayoutDirty = true;
    private string _watermarkErrorDetails = "";
    private readonly Vector3[] _watermarkErrorScreenCorners = new Vector3[4];
    private readonly object _watermarkPendingErrorLock = new object();
    private readonly Queue<WatermarkPendingError> _watermarkPendingErrors = new Queue<WatermarkPendingError>();
    private readonly Dictionary<string, WatermarkRecentErrorChannel> _watermarkRecentErrorChannels = new Dictionary<string, WatermarkRecentErrorChannel>(StringComparer.Ordinal);
    private sealed class WatermarkPendingError
    {
        public readonly string Source;
        public readonly string Level;
        public readonly string Message;
        public readonly string StackTrace;

        public WatermarkPendingError(string source, string level, string message, string stackTrace)
        {
            Source = source ?? "";
            Level = level ?? "";
            Message = message ?? "";
            StackTrace = stackTrace ?? "";
        }
    }
    private sealed class WatermarkRecentErrorChannel
    {
        public readonly string Channel;
        public readonly string Level;
        public readonly long Ticks;

        public WatermarkRecentErrorChannel(string channel, string level, long ticks)
        {
            Channel = channel ?? "";
            Level = level ?? "";
            Ticks = ticks;
        }
    }
}
