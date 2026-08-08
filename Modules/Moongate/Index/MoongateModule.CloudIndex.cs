using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steamworks;

internal sealed partial class MoongateModule
{
    internal string CloudStatusText
    {
        get
        {
            switch (_cloudLoadState)
            {
                case CloudLoadState.Loading:
                    return Text("状态: 正在连接EM云月门索引...", "Status: Connecting to the EM cloud moongate index...");
                case CloudLoadState.Connected:
                case CloudLoadState.Partial:
                    var status = Text("状态: ", "Status: ") +
                                 (_cloudLoadState == CloudLoadState.Connected
                                     ? Text("已连接", "Connected")
                                     : Text("部分可用", "Partially available")) +
                                 Text(" | EM API索引: ", " | EM API index: ") + _cloudMapCount +
                                 Text(" | 官方当前: ", " | official current: ") + _officialMapCount;
                    if (_cloudReportedTotal > 0)
                        status += Text(" | 云端记录: ", " | cloud records: ") + _cloudReportedTotal;
                    if (!string.IsNullOrWhiteSpace(_cloudGeneratedAt))
                        status += Text(" | 更新时间: ", " | updated: ") + _cloudGeneratedAt;
                    if (!string.IsNullOrWhiteSpace(_cloudLoadError))
                        status += Text("\n部分接口异常: ", "\nPartial endpoint errors: ") + _cloudLoadError;
                    return status;
                case CloudLoadState.Failed:
                    return Text("状态: EM云月门索引不可用", "Status: EM cloud moongate index unavailable") +
                           (string.IsNullOrWhiteSpace(_cloudLoadError) ? "" : "\n" + _cloudLoadError);
                default:
                    return Text("状态: 尚未加载", "Status: Not loaded");
            }
        }
    }
    internal void ResetCloudApiSettings()
    {
        ResetCloudLoadState();
    }
    internal void LoadUploadUpdateKeys(string rawJson)
    {
        _uploadUpdateKeys.Clear();
        if (string.IsNullOrWhiteSpace(rawJson))
            return;
        try
        {
            var root = JObject.Parse(rawJson);
            foreach (var property in root.Properties().Take(512))
            {
                var key = (property.Name ?? "").Trim();
                var value = property.Value.Type == JTokenType.String
                    ? (property.Value.Value<string>() ?? "").Trim()
                    : "";
                if (key.Length == 64 && value.Length >= 8 && value.Length <= 256)
                    _uploadUpdateKeys[key] = value;
            }
        }
        catch (JsonException)
        {
        }
    }
    internal string BuildUploadUpdateKeysJson()
    {
        var root = new JObject();
        foreach (var pair in _uploadUpdateKeys.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            root[pair.Key] = pair.Value;
        return root.ToString(Formatting.None);
    }
    internal void EnsureOnlineMapsLoaded()
    {
        if (_loaded || _loading || _shutdown)
            return;
        BeginLoadOnlineMaps(force: false);
    }
    internal void RefreshOnlineMaps()
    {
        if (_loading || _shutdown)
            return;

        if (_entering)
        {
            _generation++;
            _entering = false;
            _pendingEnterId = "";
            _pendingEnterLanguage = "";
        }

        InvalidateLocalMaps();
        BeginLoadOnlineMaps(force: true);
        RefreshPage();
    }
    private void RestartIndexLoad()
    {
        if (_shutdown)
            return;

        _generation++;
        _loading = false;
        _entering = false;
        _loaded = false;
        _pendingEnterId = "";
        _pendingEnterLanguage = "";
        ResetCloudLoadState();
        BeginLoadOnlineMaps(force: true);
        RefreshPage();
    }
    internal IReadOnlyList<MapEntry> GetSearchResults()
    {
        IEnumerable<MapEntry> query = _onlineMaps;
        var language = NormalizeSearchLanguage(SearchLanguage);
        if (language != "ALL")
        {
            query = query.Where(entry =>
                string.Equals(entry.Language, language, StringComparison.Ordinal));
        }
        switch (NormalizeSearchSource(SearchSource))
        {
            case "OFFICIAL":
                query = query.Where(entry => entry.IsOfficial);
                break;
            case "EM":
                query = query.Where(entry => entry.IsEmCloudStorage);
                break;
        }
        var filter = (SearchText ?? "").Trim();
        if (filter.Length > 0)
        {
            var tokens = filter
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(entry =>
            {
                var searchable = string.Concat(
                    entry.Id, "\n",
                    entry.Author, "\n",
                    entry.Title);
                for (var i = 0; i < tokens.Length; i++)
                {
                    if (searchable.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
                return true;
            });
        }

        var preferredLanguage = NormalizeLanguage(Lang.langCode);
        return query
            .OrderByDescending(entry =>
                filter.Length > 0 &&
                MapIdsEqual(entry.Id, filter))
            .ThenByDescending(entry => string.Equals(entry.Language, preferredLanguage, StringComparison.Ordinal))
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private void BeginLoadOnlineMaps(bool force)
    {
        if (_loading || _shutdown)
            return;
        if (force)
            _loaded = false;
        _loading = true;
        _cloudLoadState = CloudLoadState.Loading;
        _cloudLoadError = "";
        Log = Text("正在加载 CN / JP / EN 月门地图索引...", "Loading CN / JP / EN moongate indexes...");
        LoadOnlineMapsAsync(++_generation).Forget();
    }
    private async UniTask LoadOnlineMapsAsync(int generation)
    {
        var officialMaps = new List<MapEntry>();
        var officialFailures = new List<string>();
        var cloudMaps = new List<MapEntry>();
        var cloudFailures = new List<string>();
        try
        {
            for (var i = 0; i < Languages.Length; i++)
            {
                var language = Languages[i];
                List<Net.DownloadMeta>? maps;
                try
                {
                    maps = await Net.GetFileList(language);
                }
                catch (Exception ex)
                {
                    maps = null;
                    officialFailures.Add(language + ": " + ex.Message);
                }

                if (_shutdown || generation != _generation)
                    return;
                if (maps == null)
                {
                    if (!officialFailures.Any(value => value.StartsWith(language + ":", StringComparison.Ordinal)))
                        officialFailures.Add(language);
                    continue;
                }

                for (var mapIndex = 0; mapIndex < maps.Count; mapIndex++)
                {
                    var meta = maps[mapIndex];
                    if (meta == null || string.IsNullOrWhiteSpace(meta.id))
                        continue;
                    officialMaps.Add(new MapEntry(meta, language, isOfficial: true, source: "Official"));
                }
            }

            if (_shutdown || generation != _generation)
                return;

            _officialMapCount = CountDistinctMaps(officialMaps);
            await LoadCloudMetadataAsync(cloudMaps, cloudFailures, generation);
            if (_shutdown || generation != _generation)
                return;

            var merged = MergeMaps(cloudMaps, officialMaps);
            _cloudMapCount = CountDistinctMaps(cloudMaps);
            _emCloudStorageMapCount = CountDistinctMaps(cloudMaps.Where(entry => entry.IsEmCloudStorage));
            _cloudLoadError = string.Join(" | ", cloudFailures.Distinct());
            _cloudLoadState = _cloudMapCount > 0
                ? (cloudFailures.Count == 0 ? CloudLoadState.Connected : CloudLoadState.Partial)
                : CloudLoadState.Failed;

            _onlineMaps.Clear();
            _onlineMaps.AddRange(merged);
            InvalidateLocalMaps();
            _loaded = true;
            SearchPage = 0;
            Log = Text("月门地图索引已加载，共 ", "Moongate indexes loaded: ") +
                  _onlineMaps.Count.ToString() +
                  Text(" 个地图（EM 索引历史: ", " maps (EM index history: ") +
                  _cloudMapCount.ToString() +
                  Text("，EM 云存储: ", ", EM cloud storage: ") +
                  _emCloudStorageMapCount.ToString() +
                  Text("，官方当前: ", ", official current: ") +
                  _officialMapCount.ToString() + ")";
            if (officialFailures.Count > 0)
            {
                Log += Text("；官方接口失败: ", "; official endpoint failures: ") +
                       string.Join(", ", officialFailures);
            }
        }
        catch (Exception ex)
        {
            if (_shutdown || generation != _generation)
                return;
            Log = Text("加载月门地图索引失败: ", "Failed to load moongate indexes: ") + ex.Message;
            _cloudLoadState = CloudLoadState.Failed;
            _cloudLoadError = ex.Message;
        }
        finally
        {
            if (!_shutdown && generation == _generation)
            {
                _loading = false;
                RefreshPage();
                ContinuePendingEnter();
            }
        }
    }
    private async UniTask LoadCloudMetadataAsync(
        List<MapEntry> cloudMaps,
        List<string> failures,
        int generation)
    {
        var indexResponse = await TryDownloadCloudTextAsync(CloudIndexApi);
        if (_shutdown || generation != _generation)
            return;

        if (!string.IsNullOrWhiteSpace(indexResponse.Error))
            failures.Add(Text("Elin Modifier Moongate API 索引", "Elin Modifier Moongate API index") + ": " + indexResponse.Error);
        else if (indexResponse.Requested)
        {
            try
            {
                cloudMaps.AddRange(ParseCloudIndex(indexResponse.Text));
            }
            catch (Exception ex)
            {
                failures.Add(Text("Elin Modifier Moongate API 索引", "Elin Modifier Moongate API index") + ": " + ShortError(ex));
            }
        }
    }
    private async UniTask<CloudTextResponse> TryDownloadCloudTextAsync(string address)
    {
        var response = new CloudTextResponse
        {
            Requested = !string.IsNullOrWhiteSpace(address)
        };
        if (!response.Requested)
            return response;
        try
        {
            response.Text = await DownloadCloudTextAsync(address);
        }
        catch (Exception ex)
        {
            response.Error = ShortError(ex);
        }
        return response;
    }
    private async UniTask<string> DownloadCloudTextAsync(string address)
    {
        using var response = await _cloudClient.GetAsync(address);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 16 * 1024 * 1024)
            throw new InvalidDataException(Text("接口响应超过16MB限制", "Endpoint response exceeds the 16 MB limit"));
        return Encoding.UTF8.GetString(bytes);
    }
    private List<MapEntry> ParseCloudIndex(string json)
    {
        var root = ParseJsonObjectPreservingDates(json);
        _cloudGeneratedAt = NormalizeSourceDate(root.Value<string>("generated_at") ?? _cloudGeneratedAt);
        _cloudReportedTotal = root.Value<int?>("total") ?? _cloudReportedTotal;
        var maps = root["maps"] as JArray;
        if (maps == null)
            throw new InvalidDataException(Text("API索引缺少maps数组", "API index is missing the maps array"));

        var result = new List<MapEntry>();
        foreach (var token in maps)
        {
            if (token is JObject map && TryCreateCloudMapEntry(map, out var entry))
                result.Add(entry);
        }
        return result;
    }
    private static JObject ParseJsonObjectPreservingDates(string json)
    {
        using var textReader = new StringReader(json ?? "");
        using var jsonReader = new JsonTextReader(textReader)
        {
            DateParseHandling = DateParseHandling.None
        };
        return JObject.Load(jsonReader);
    }
    private static bool TryCreateCloudMapEntry(JObject map, out MapEntry entry)
    {
        entry = null!;
        var language = (map.Value<string>("language") ?? "").Trim().ToUpperInvariant();
        if (!Languages.Contains(language))
            return false;

        var id = WebUtility.HtmlDecode(map.Value<string>("id") ?? "").Trim();
        var path = (map.Value<string>("path") ?? "").Trim();
        var downloadUrl = (map.Value<string>("download_url") ?? "").Trim();
        if (id.Length == 0 || (path.Length == 0 && downloadUrl.Length == 0))
            return false;

        var source = (map.Value<string>("source") ?? "").Trim();

        var meta = new Net.DownloadMeta
        {
            path = path,
            id = id,
            name = WebUtility.HtmlDecode(map.Value<string>("author") ?? ""),
            title = WebUtility.HtmlDecode(map.Value<string>("title") ?? ""),
            cat = map.Value<string>("category") ?? "",
            date = map.Value<string>("source_date") ?? "",
            version = map.Value<int?>("version") ?? 0,
            tag = map.Value<string>("tags") ?? ""
        };
        entry = new MapEntry(
            meta,
            language,
            isOfficial: string.Equals(source, "Offical", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(source, "Official", StringComparison.OrdinalIgnoreCase),
            directDownloadUrl: downloadUrl,
            source: source);
        return true;
    }
    private static List<MapEntry> MergeMaps(IEnumerable<MapEntry> cloudMaps, IEnumerable<MapEntry> officialMaps)
    {
        var merged = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in cloudMaps)
            AddOrReplaceLatestMap(merged, entry);
        foreach (var entry in officialMaps)
            AddOrReplaceLatestMap(merged, entry);
        return merged.Values.ToList();
    }
    private static int CountDistinctMaps(IEnumerable<MapEntry> maps)
    {
        return maps.Select(BuildMapKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    }
    private static void AddOrReplaceLatestMap(Dictionary<string, MapEntry> maps, MapEntry candidate)
    {
        var key = BuildMapKey(candidate);
        if (!maps.TryGetValue(key, out var current) || IsNewerMap(candidate, current))
            maps[key] = candidate;
    }
    private static bool IsNewerMap(MapEntry candidate, MapEntry current)
    {
        var candidateHasDate = TryParseSourceDate(candidate.SourceDate, out var candidateDate);
        var currentHasDate = TryParseSourceDate(current.SourceDate, out var currentDate);
        if (candidateHasDate && currentHasDate && candidateDate != currentDate)
            return candidateDate > currentDate;
        if (candidateHasDate != currentHasDate)
            return candidateHasDate;
        if (candidate.Version != current.Version)
            return candidate.Version > current.Version;
        if (candidate.IsOfficial != current.IsOfficial)
            return candidate.IsOfficial;
        return false;
    }
    private static string BuildMapKey(MapEntry entry)
    {
        return (entry.Language ?? "").Trim().ToUpperInvariant() +
               "\u001f" +
               CanonicalizeMapId(entry.Id);
    }
    private static string CanonicalizeMapId(string id)
    {
        return WebUtility.HtmlDecode(id ?? "").Trim().Normalize(NormalizationForm.FormKC);
    }
    private static bool MapIdsEqual(string first, string second)
    {
        return string.Equals(
            CanonicalizeMapId(first),
            CanonicalizeMapId(second),
            StringComparison.OrdinalIgnoreCase);
    }
    private static bool MapMetadataMatchesId(MapMetaData metadata, string id)
    {
        if (metadata == null)
            return false;
        if (MapIdsEqual(metadata.id, id))
            return true;
        try
        {
            return MapIdsEqual(Path.GetFileNameWithoutExtension(metadata.path ?? ""), id);
        }
        catch
        {
            return false;
        }
    }
    private static string NormalizeSourceDate(string value)
    {
        value = (value ?? "").Trim().Trim('"');
        if (value.Length == 0)
            return "";

        if (HasExplicitTimeZone(value) && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var absoluteTime))
        {
            return absoluteTime.ToOffset(TimeSpan.FromHours(8))
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var beijingLocalTime))
        {
            return DateTime.SpecifyKind(beijingLocalTime, DateTimeKind.Unspecified)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return value;
    }
    private static bool HasExplicitTimeZone(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            return true;
        var length = value.Length;
        return (length >= 6 &&
                (value[length - 6] == '+' || value[length - 6] == '-') &&
                value[length - 3] == ':') ||
               (length >= 5 &&
                (value[length - 5] == '+' || value[length - 5] == '-'));
    }
    private static bool TryParseSourceDate(string value, out DateTime date)
    {
        return DateTime.TryParse(
            NormalizeSourceDate(value),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
            out date);
    }
    private static string NormalizeLanguage(string language)
    {
        language = (language ?? "").Trim().ToUpperInvariant();
        return Languages.Contains(language) ? language : "EN";
    }
    private static string NormalizeSearchLanguage(string language)
    {
        language = (language ?? "").Trim().ToUpperInvariant();
        return language == "CN" || language == "JP" || language == "EN"
            ? language
            : "ALL";
    }
    private static string NormalizeSearchSource(string source)
    {
        source = (source ?? "").Trim().ToUpperInvariant();
        return source == "OFFICIAL" || source == "EM" ? source : "ALL";
    }
    private void ResetCloudLoadState()
    {
        _cloudLoadState = CloudLoadState.NotLoaded;
        _cloudLoadError = "";
        _cloudGeneratedAt = "";
        _cloudMapCount = 0;
        _emCloudStorageMapCount = 0;
        _cloudReportedTotal = 0;
        _officialMapCount = 0;
    }
}
