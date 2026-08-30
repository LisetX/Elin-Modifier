using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steamworks;

internal sealed partial class MoongateModule
{
    internal const string SourceOfficial = "OFFICIAL";
    internal const string SourceEmCloud = "EM_CLOUD";
    internal const string SourceEmHistory = "EM_HISTORY";
    internal const string SourceLocal = "LOCAL";
    private const int EnterTimeoutMilliseconds = 60000;
    internal const string CloudIndexApi = "https://em-hk.m9.pw/moongate-api/v1/maps/index";
    private const string CloudUploadApi = "https://em-hk.m9.pw/moongate-api/v1/maps/upload";
    private const string CloudDownloadApi = "https://em-hk.m9.pw/moongate-api/v1/maps/download?id=";
    private enum CloudLoadState
    {
        NotLoaded,
        Loading,
        Connected,
        Partial,
        Failed
    }
    private sealed class CloudTextResponse
    {
        internal bool Requested;
        internal string Text = "";
        internal string Error = "";
    }
    internal sealed class MapEntry
    {
        internal MapEntry(
            Net.DownloadMeta meta,
            string language,
            bool isOfficial = false,
            string directDownloadUrl = "",
            string source = "")
        {
            Meta = meta;
            Language = language;
            IsOfficial = isOfficial;
            DirectDownloadUrl = (directDownloadUrl ?? "").Trim();
            Source = (source ?? "").Trim();
            IsEmCloudStorage = !isOfficial &&
                               (string.Equals(
                                    Source,
                                    "Elin Modifier user upload",
                                    StringComparison.OrdinalIgnoreCase) ||
                                (meta.id ?? "").StartsWith("EMU_", StringComparison.OrdinalIgnoreCase));
            try
            {
                IsCompatible = meta.IsValidVersion();
            }
            catch
            {
                IsCompatible = false;
            }
            IsAdult = HasAdultTag(meta.tag);
        }

        internal Net.DownloadMeta Meta { get; }
        internal string Language { get; }
        internal string Id => Meta.id ?? "";
        internal string Author => Meta.name ?? "";
        internal string Title => Meta.title ?? "";
        internal string SourceDate => NormalizeSourceDate(Meta.date);
        internal int Version => Meta.version;
        internal bool IsOfficial { get; }
        internal string Source { get; }
        internal bool IsEmCloudStorage { get; }
        internal string SourceKind => IsOfficial
            ? SourceOfficial
            : IsEmCloudStorage
                ? SourceEmCloud
                : SourceEmHistory;
        internal string DirectDownloadUrl { get; }
        internal bool IsCompatible { get; }
        internal bool IsAdult { get; }
    }
    internal sealed class FavoriteEntry
    {
        internal FavoriteEntry(
            string id,
            string title,
            string author,
            string language,
            string sourceDate,
            int version,
            string sourceKind)
        {
            Id = id;
            Title = title;
            Author = author;
            Language = language;
            SourceDate = sourceDate;
            Version = version;
            SourceKind = sourceKind;
        }

        internal string Id { get; }
        internal string Title { get; }
        internal string Author { get; }
        internal string Language { get; }
        internal string SourceDate { get; }
        internal int Version { get; }
        internal string SourceKind { get; }
    }
    internal sealed class LocalEntry
    {
        internal LocalEntry(
            string id,
            string title,
            string author,
            string language,
            string cachedAt,
            int version,
            string filePath,
            bool isPersistent,
            string sourceKind)
        {
            Id = id;
            Title = title;
            Author = author;
            Language = language;
            CachedAt = cachedAt;
            Version = version;
            FilePath = filePath;
            IsPersistent = isPersistent;
            SourceKind = sourceKind;
        }

        internal string Id { get; }
        internal string Title { get; }
        internal string Author { get; }
        internal string Language { get; }
        internal string CachedAt { get; }
        internal int Version { get; }
        internal string FilePath { get; }
        internal bool IsPersistent { get; }
        internal string SourceKind { get; }
        internal string PersistentPath => GetPersistentMapPath(FilePath);
    }
    private static readonly string[] Languages = { "CN", "JP", "EN" };
    private readonly ElinModifierPlugin _host;
    private readonly List<MapEntry> _onlineMaps = new List<MapEntry>();
    private readonly List<LocalEntry> _localMaps = new List<LocalEntry>();
    private readonly Dictionary<string, string> _uploadUpdateKeys =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly HttpClient _cloudClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(120)
    };
    private bool _loaded;
    private bool _localMapsLoaded;
    private bool _loading;
    private bool _entering;
    private bool _uploading;
    private string _updatingLocalMapId = "";
    private bool _shutdown;
    private bool _moongateWorldStateInitialized;
    private bool _lastInsideMoongateWorld;
    private int _generation;
    private string _pendingEnterId = "";
    private string _pendingEnterLanguage = "";
    private CloudLoadState _cloudLoadState;
    private string _cloudLoadError = "";
    private string _cloudGeneratedAt = "";
    private int _cloudMapCount;
    private int _emCloudStorageMapCount;
    private int _cloudReportedTotal;
    private int _officialMapCount;
    internal MoongateModule(ElinModifierPlugin host)
    {
        _host = host;
    }
    internal string SpecifiedMapId { get; set; } = "";
    internal string SearchText { get; set; } = "";
    internal string SearchLanguage { get; set; } = "ALL";
    internal string SearchSource { get; set; } = "ALL";
    internal int SearchPage { get; set; }
    internal int LocalPage { get; set; }
    internal bool LandholderPrivilegesEnabled { get; set; } = true;
    internal string Log { get; private set; } = "Ready";
    internal bool IsLoading => _loading;
    internal bool IsEntering => _entering;
    internal bool IsInsideMoongateWorld
    {
        get
        {
            try
            {
                var zone = GameAccess.World.CurrentZone;
                return zone is Zone_User && zone.instance is ZoneInsstanceMoongate;
            }
            catch
            {
                return false;
            }
        }
    }
    internal bool CanEnterMoongate => !_entering && !IsInsideMoongateWorld;
    internal bool IsUploading => _uploading;
    internal bool IsUpdatingAnyLocalMap => _updatingLocalMapId.Length > 0;
    internal bool IsUpdatingLocalMap(string id) =>
        MapIdsEqual(_updatingLocalMapId, id);
}
