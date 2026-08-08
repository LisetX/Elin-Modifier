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
    internal void EnterSpecifiedMap()
    {
        BeginEnterMap(SpecifiedMapId, "");
    }
    internal void BeginEnterMap(string id, string preferredLanguage)
    {
        if (_shutdown || _entering)
            return;

        if (IsInsideMoongateWorld)
        {
            Log = Text("当前已处于月门世界，无法再次进入月门", "Already inside a moongate world; another moongate cannot be entered");
            RefreshPage();
            return;
        }

        id = ResolveCanonicalId(id);
        if (id.Length == 0)
        {
            Log = Text("请输入有效的地图ID", "Enter a valid map ID");
            RefreshPage();
            return;
        }

        SpecifiedMapId = id;
        if (!ElinModifierPlugin.HasModuleCharacterData())
        {
            Log = Text("请先进入存档", "Enter a save first");
            RefreshPage();
            return;
        }

        if (TryEnterSavedMap(id))
            return;

        if (!_loaded)
        {
            _pendingEnterId = id;
            _pendingEnterLanguage = preferredLanguage ?? "";
            Log = Text("正在加载月门地图索引...", "Loading moongate map index...");
            EnsureOnlineMapsLoaded();
            RefreshPage();
            return;
        }

        var entry = FindOnlineMap(id, preferredLanguage);
        if (entry == null)
        {
            Log = Text("未找到地图ID: ", "Map ID not found: ") + id;
            RefreshPage();
            return;
        }

        var generation = ++_generation;
        EnterOnlineMapAsync(entry, generation).Forget();
        RecoverTimedOutEnterAsync(generation).Forget();
    }
    internal void Shutdown()
    {
        _shutdown = true;
        _generation++;
        _pendingEnterId = "";
        _pendingEnterLanguage = "";
        _onlineMaps.Clear();
        _localMaps.Clear();
        _localMapsLoaded = false;
        _loaded = false;
        _loading = false;
        _entering = false;
        _moongateWorldStateInitialized = false;
        _cloudClient.Dispose();
    }
    private async UniTask EnterOnlineMapAsync(MapEntry entry, int generation)
    {
        _entering = true;
        Log = Text("正在下载并进入月门: ", "Downloading and entering moongate: ") +
              DisplayName(entry);
        RefreshPage();
        try
        {
            if (entry.IsAdult && GameAccess.Runtime.Core.config.net.noAdult)
            {
                Log = Text(
                    "该地图受当前游戏成人内容过滤设置限制: ",
                    "This map is blocked by the current adult-content filter: ") + entry.Id;
                return;
            }
            var file = entry.DirectDownloadUrl.Length > 0
                ? await DownloadApiMapAsync(entry)
                : await Net.DownloadFile(entry.Meta, CorePath.ZoneSaveUser, entry.Language);
            if (_shutdown || generation != _generation)
                return;
            if (file == null || !file.Exists)
            {
                Log = Text("月门地图下载失败: ", "Moongate map download failed: ") + entry.Id;
                return;
            }
            InvalidateLocalMaps();
            if (!Zone.IsImportValid(file.FullName))
            {
                Log = Text("月门地图校验失败: ", "Moongate map validation failed: ") + entry.Id;
                return;
            }

            var map = Map.GetMetaData(file.FullName);
            if (map == null)
            {
                Log = Text("无法读取月门地图数据: ", "Unable to read moongate map data: ") + entry.Id;
                return;
            }
            map.path = file.FullName;
            map.id = entry.Id;
            if (GameAccess.Characters.PlayerCharacter?.burden?.GetPhase() == 4)
            {
                Log = Text("负重过高，无法进入月门", "Overburdened; unable to enter the moongate");
                return;
            }

            _entering = false;
            Log = Text("已发出月门进入请求: ", "Moongate enter request issued: ") +
                  DisplayName(entry);
            var transientLog = Log;
            RefreshPage();
            ClearTransientLogAsync(transientLog).Forget();
            PrepareExistingUserZone(map);
            new TraitMoongate().LoadMap(map);
        }
        catch (Exception ex)
        {
            if (!_shutdown && generation == _generation)
                Log = Text("进入月门失败: ", "Failed to enter moongate: ") + ex.Message;
        }
        finally
        {
            if (!_shutdown && generation == _generation)
            {
                _entering = false;
                RefreshPage();
            }
        }
    }
    private async UniTask RecoverTimedOutEnterAsync(int generation)
    {
        await UniTask.Delay(EnterTimeoutMilliseconds);
        if (_shutdown || generation != _generation || !_entering)
            return;

        _generation++;
        _entering = false;
        _pendingEnterId = "";
        _pendingEnterLanguage = "";
        Log = Text(
            "进入月门超时，已解除锁定；可重新进入或刷新页面",
            "Moongate entry timed out and was unlocked; retry or refresh maps");
        RefreshPage();
    }
    private bool TryEnterSavedMap(string id)
    {
        try
        {
            var gate = new TraitMoongate();
            var originalMap = gate.ListSavedUserMap().FirstOrDefault(candidate => MapMetadataMatchesId(candidate, id));
            if (originalMap == null)
                return false;
            var map = originalMap;
            var persistentPath = GetPersistentMapPath(originalMap.path);
            if (MoongatePersistentStorage.IsPersistenceEnabled(originalMap.path))
            {
                map = Map.GetMetaData(persistentPath) ??
                      throw new InvalidDataException(Text(
                          "无法读取持久化月门存档",
                          "Unable to read the persistent moongate save"));
                if (!map.IsValidVersion())
                    throw new InvalidDataException(Text(
                        "持久化月门存档版本无效",
                        "The persistent moongate save version is invalid"));
                map.path = persistentPath;
            }
            else
            {
                map.path = originalMap.path;
            }
            map.id = id;
            if (GameAccess.Runtime.Core.config.net.noAdult && HasAdultTag(map.tag))
            {
                Log = Text(
                    "该地图受当前游戏成人内容过滤设置限制: ",
                    "This map is blocked by the current adult-content filter: ") + id;
                RefreshPage();
                return true;
            }
            if (GameAccess.Characters.PlayerCharacter?.burden?.GetPhase() == 4)
            {
                Log = Text("负重过高，无法进入月门", "Overburdened; unable to enter the moongate");
                RefreshPage();
                return true;
            }

            _entering = false;
            Log = Text("已发出进入请求", "Enter request issued");
            var transientLog = Log;
            RefreshPage();
            ClearTransientLogAsync(transientLog).Forget();
            PrepareExistingUserZone(map);
            gate.LoadMap(map);
            return true;
        }
        catch (Exception ex)
        {
            Log = Text("读取本地月门地图失败: ", "Failed to read local moongate map: ") + ex.Message;
            RefreshPage();
            return true;
        }
    }
    private static void PrepareExistingUserZone(MapMetaData map)
    {
        if (map == null || GameAccess.Runtime.Game?.spatials == null)
            return;
        var existing = GameAccess.Runtime.Game.spatials.Find((Zone_User zone) => zone.idUser == map.id);
        if (existing == null)
            return;
        existing.path = map.path;
        existing.name = map.name;
    }
    private MapEntry? FindOnlineMap(string id, string preferredLanguage)
    {
        var matches = _onlineMaps.Where(entry => MapIdsEqual(entry.Id, id)).ToList();
        if (matches.Count == 0)
            return null;

        MapEntry? match = null;
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            var preferred = NormalizeLanguage(preferredLanguage);
            match = matches.FirstOrDefault(entry =>
                string.Equals(entry.Language, preferred, StringComparison.Ordinal));
            if (match != null)
                return match;
        }

        var current = NormalizeLanguage(Lang.langCode);
        match = matches.FirstOrDefault(entry =>
            string.Equals(entry.Language, current, StringComparison.Ordinal));
        return match ?? matches[0];
    }
    private string ResolveCanonicalId(string id)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0)
            return "";
        var online = FindOnlineMap(id, "");
        return online?.Id ?? id;
    }
    private void ContinuePendingEnter()
    {
        if (_pendingEnterId.Length == 0)
            return;
        var id = _pendingEnterId;
        var language = _pendingEnterLanguage;
        _pendingEnterId = "";
        _pendingEnterLanguage = "";
        BeginEnterMap(id, language);
    }
}
