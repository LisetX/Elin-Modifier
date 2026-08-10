using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class ThreatOverlayModule
{
    private readonly ElinModifierPlugin _host;
    private sealed class CapturedThreatAction
    {
        public int Serial;
        public int Frame;
        public float Time;
        public string Name = string.Empty;
        public Point? MovePoint;
        public List<string>? LockedSequence;
    }
    private sealed class ThreatActionCandidate
    {
        public string Name = string.Empty;
        public int Weight;
        public bool IsMove;
    }
    private sealed class ThreatMarker
    {
        public int Uid;
        public Chara? Chara;
        public GameObject Root = null!;
        public RectTransform Rect = null!;
        public Text Name = null!;
        public Text Level = null!;
        public Text Prediction = null!;
        public RectTransform HpFill = null!;
        public Image HpImage = null!;
        public GameObject MoveCellRoot = null!;
        public ProjectedCellGraphic MoveCell = null!;
        public bool Active;
        public bool HasScreenCache;
        public bool CachedScreenVisible;
        public bool CachedRendererMissing;
        public bool CachedRendererSkip;
        public bool HasObservedWorldPosition;
        public Vector3 ObservedWorldPosition;
        public bool CachedUsesBounds;
        public Vector3 CachedBoundsCenter;
        public Vector3 CachedBoundsSize;
        public Vector3 CachedApproxCenter;
        public Vector3 CachedApproxBase;
        public Rect CachedScreenRect;
        public bool HasAppliedScreenRect;
        public Rect LastAppliedScreenRect;
        public float LastAppliedCanvasScale = -1f;
        public Vector2 LastAppliedCanvasSize;
        public float LastHpRatio = -1f;
        public string LastPrediction = string.Empty;
        public Point? PredictedMovePoint;
        public bool PredictionVisualDirty;
        public AIAct? PredictionAi;
        public AIAct? PredictionCurrent;
        public AIAct.Status PredictionStatus;
        public int PredictionTurn = int.MinValue;
        public int PredictionOwnerX = int.MinValue;
        public int PredictionOwnerZ = int.MinValue;
        public int PredictionTargetX = int.MinValue;
        public int PredictionTargetZ = int.MinValue;
        public int PredictionActionCount = -1;
        public int PredictionDecisionVersion = -1;
        public int PredictionCaptureSerial = -1;
        public int PredictionLockSerial = -1;
        public bool PredictionCaptureActive;
    }
    private GameObject? _threatRoot;
    private Canvas? _threatCanvas;
    private RectTransform? _threatCanvasRect;
    private readonly List<ThreatMarker> _threatMarkers = new List<ThreatMarker>();
    private readonly Dictionary<int, ThreatMarker> _threatByUid = new Dictionary<int, ThreatMarker>();
    private readonly Stack<ThreatMarker> _threatPool = new Stack<ThreatMarker>();
    private readonly HashSet<int> _threatSeen = new HashSet<int>();
    private readonly Dictionary<int, CapturedThreatAction> _capturedThreatActions =
        new Dictionary<int, CapturedThreatAction>();
    private readonly Dictionary<int, int> _threatDecisionVersions = new Dictionary<int, int>();
    private int _nextThreatActionSerial;
    private bool _threatOverlayInitialized;
    private bool _threatOverlayDirty = true;
    private int _threatPositionFrame = -1;
    private float _threatLastPositionRefreshTime = -9999f;
    private Camera? _threatCachedCamera;
    private Matrix4x4 _threatCachedViewProjection;
    private int _threatCachedScreenWidth = -1;
    private int _threatCachedScreenHeight = -1;
    private Rect _threatCachedCameraPixelRect;
    private bool _threatCameraCacheValid;
    private Canvas? _threatGameUiCanvas;
    private RenderMode _threatGameUiRenderMode;
    private Camera? _threatGameUiCamera;
    private int _threatGameUiSortingLayerId = int.MinValue;
    private int _threatGameUiTargetDisplay = -1;
    internal ThreatOverlayModule(ElinModifierPlugin host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }
    private bool _hostileThreatMarker => _host.ModuleHostileThreatMarker;
    private bool _hostileThreatBehaviorPrediction => _host.ModuleHostileThreatBehaviorPrediction;
    private bool _lowPerformanceMode => _host.ModuleLowPerformanceMode;
    private Font? _lGuiFont => _host.ModuleLGuiFont;
    private string _log
    {
        set => _host.ModuleLog = value;
    }
    private float SchedulerNow => _host.ModuleSchedulerNow;
    private bool ShouldScanThreats() => _host.ShouldModuleScanThreats();
    private bool ShouldRefreshThreatVitals() => _host.ShouldModuleRefreshThreatVitals();
    private void MarkThreatScanClean() => _host.MarkModuleThreatScanClean();
    private void InvalidateThreatVitals() => _host.InvalidateModuleThreatVitals();
    private static Camera? GetSceneCamera() => ElinModifierPlugin.GetModuleSceneCamera();
    private static bool IsHostileThreat(Chara? chara, Chara pc) => ElinModifierPlugin.IsModuleHostileThreat(chara, pc);
    private static bool CanPlayerCurrentlySee(Chara pc, Chara chara) => ElinModifierPlugin.CanModulePlayerSee(pc, chara);
    private static string SafeName(Chara chara) => ElinModifierPlugin.GetModuleSafeCharaName(chara);
    private static float GetThreatHpRatio(Chara chara) => ElinModifierPlugin.GetModuleThreatHpRatio(chara);
    private static string GetThreatLevelText(Chara chara) => ElinModifierPlugin.GetModuleThreatLevelText(chara);
    private string T(string zh, string en) => _host.TranslateModuleText(zh, en);
    private static bool NormalizeMarkerRect(ref Rect rect) => ElinModifierPlugin.NormalizeModuleMarkerRect(ref rect);
    private static void TightenMarkerRect(ref Rect rect, float widthScale, float heightScale) =>
        ElinModifierPlugin.TightenModuleMarkerRect(ref rect, widthScale, heightScale);
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
}
