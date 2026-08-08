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
    internal IReadOnlyList<LocalEntry> GetLocalMaps()
    {
        if (_localMapsLoaded)
            return _localMaps;

        _localMapsLoaded = true;
        _localMaps.Clear();
        try
        {
            var directory = new DirectoryInfo(CorePath.ZoneSaveUser);
            if (!directory.Exists)
                return _localMaps;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = directory.GetFiles("*.z")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();
            for (var i = 0; i < files.Length; i++)
            {
                var file = files[i];
                try
                {
                    var metadata = Map.GetMetaData(file.FullName);
                    if (metadata == null || !metadata.IsValidVersion())
                        continue;

                    var id = ResolveLocalMapId(metadata, file);
                    if (id.Length == 0 || !seen.Add(id))
                        continue;

                    var online = FindOnlineMap(id, "");
                    var title = (metadata.name ?? "").Trim();
                    if (title.Length == 0)
                        title = online?.Title ?? id;
                    _localMaps.Add(new LocalEntry(
                        id,
                        title,
                        online?.Author ?? "",
                        online?.Language ?? "",
                        NormalizeLocalFileDate(file.LastWriteTime),
                        metadata.version,
                        file.FullName,
                        MoongatePersistentStorage.IsPersistenceEnabled(file.FullName),
                        online?.SourceKind ?? SourceLocal));
                }
                catch
                {
                    // Ignore individual broken cache files and continue listing usable maps.
                }
            }
        }
        catch
        {
            // The local cache may be unavailable while the game is moving map files.
        }
        return _localMaps;
    }
    internal void UpdateLocalMap(LocalEntry entry)
    {
        if (_shutdown || entry == null || _updatingLocalMapId.Length > 0)
            return;
        UpdateLocalMapAsync(entry).Forget();
    }
    internal void SetLocalMapPersistence(LocalEntry entry, bool enabled)
    {
        if (_shutdown || entry == null || _updatingLocalMapId.Length > 0)
            return;
        try
        {
            if (enabled)
            {
                MoongatePersistentStorage.AllowCurrentSave(entry.PersistentPath);
                if (!File.Exists(entry.FilePath))
                    throw new FileNotFoundException(
                        Text("本地月门缓存不存在", "Local moongate cache does not exist"),
                        entry.FilePath);
                if (!File.Exists(entry.PersistentPath))
                {
                    File.Copy(entry.FilePath, entry.PersistentPath, true);
                    File.SetLastWriteTimeUtc(entry.PersistentPath, File.GetLastWriteTimeUtc(entry.FilePath));
                }
                DeleteFileIfPresent(GetPersistenceDisabledMarkerPath(entry.FilePath));
                MoongatePersistentStorage.RefreshRegisteredZone(
                    entry.Id,
                    entry.FilePath,
                    entry.Title,
                    persistent: true);
                Log = Text("已开启本地月门持久化: ", "Local moongate persistence enabled: ") + entry.Id;
            }
            else
            {
                MoongatePersistentStorage.SuppressCurrentSave(entry.PersistentPath);
                File.WriteAllText(GetPersistenceDisabledMarkerPath(entry.FilePath), "disabled");
                MoongatePersistentStorage.RefreshRegisteredZone(
                    entry.Id,
                    entry.FilePath,
                    entry.Title,
                    persistent: false);
                Log = Text("已关闭本地月门持久化: ", "Local moongate persistence disabled: ") + entry.Id;
            }
            InvalidateLocalMaps(preservePage: true);
        }
        catch (Exception ex)
        {
            Log = Text("修改本地月门持久化失败: ", "Failed to change local moongate persistence: ") +
                  ShortError(ex);
        }
        RefreshPage();
    }
    internal void RestorePersistentLocalMap(LocalEntry entry)
    {
        if (_shutdown || entry == null || _updatingLocalMapId.Length > 0 || !entry.IsPersistent)
            return;
        try
        {
            if (!File.Exists(entry.FilePath))
                throw new FileNotFoundException(
                    Text("本地月门原始缓存不存在", "The original local moongate cache does not exist"),
                    entry.FilePath);
            MoongatePersistentStorage.SuppressCurrentSave(entry.PersistentPath);
            File.Copy(entry.FilePath, entry.PersistentPath, true);
            File.SetLastWriteTimeUtc(entry.PersistentPath, File.GetLastWriteTimeUtc(entry.FilePath));
            InvalidateLocalMaps(preservePage: true);
            Log = Text("已用原始月门重新生成持久化存档: ", "Persistent moongate save recreated from the original: ") +
                  entry.Id;
        }
        catch (Exception ex)
        {
            Log = Text("恢复本地月门失败: ", "Failed to restore the local moongate: ") + ShortError(ex);
        }
        RefreshPage();
    }
    internal void DeleteLocalMap(LocalEntry entry)
    {
        if (_shutdown || entry == null || _updatingLocalMapId.Length > 0)
            return;
        try
        {
            MoongatePersistentStorage.SuppressCurrentSave(entry.PersistentPath);
            DeleteFileIfPresent(entry.FilePath);
            DeleteFileIfPresent(entry.PersistentPath);
            DeleteFileIfPresent(GetPersistenceDisabledMarkerPath(entry.FilePath));
            DeleteFileIfPresent(entry.FilePath + ".download");
            DeleteFileIfPresent(entry.FilePath + ".update-backup");
            var canonicalPath = Path.Combine(CorePath.ZoneSaveUser, SanitizeMapFileName(entry.Id) + ".z");
            if (!string.Equals(canonicalPath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                DeleteFileIfPresent(canonicalPath);
                DeleteFileIfPresent(GetPersistentMapPath(canonicalPath));
                DeleteFileIfPresent(GetPersistenceDisabledMarkerPath(canonicalPath));
                DeleteFileIfPresent(canonicalPath + ".download");
            }
            DeleteFileIfPresent(GetLegacyLocalBackupPath(entry.Id));
            RemoveOfficialDownloadCacheEntry(entry.Id);
            DeleteLegacyBackupDirectoryIfEmpty();
            InvalidateLocalMaps();
            Log = Text("已删除本地月门及其持久化存档: ", "Local moongate and its persistent save deleted: ") + entry.Id;
        }
        catch (Exception ex)
        {
            Log = Text("删除本地月门失败: ", "Failed to delete local moongate: ") + ShortError(ex);
        }
        RefreshPage();
    }
    private async UniTask UpdateLocalMapAsync(LocalEntry entry)
    {
        _updatingLocalMapId = entry.Id;
        Log = Text("正在检查本地月门更新: ", "Checking local moongate update: ") + entry.Id;
        RefreshPage();
        var backupPath = entry.FilePath + ".update-backup";
        var backupCreated = false;
        try
        {
            var online = FindOnlineMap(entry.Id, entry.Language);
            if (online == null)
                throw new InvalidOperationException(Text("在线索引中未找到该地图", "Map not found in the online index"));

            var localFile = new FileInfo(entry.FilePath);
            if (!localFile.Exists)
                throw new FileNotFoundException(Text("本地月门缓存不存在", "Local moongate cache does not exist"), entry.FilePath);

            if (IsLocalMapCurrent(entry, online))
            {
                Log = Text("本地月门缓存已是最新: ", "Local moongate cache is already current: ") + entry.Id;
                return;
            }

            DeleteFileIfPresent(backupPath);
            File.Copy(entry.FilePath, backupPath, true);
            File.SetLastWriteTimeUtc(backupPath, localFile.LastWriteTimeUtc);
            backupCreated = true;

            FileInfo? downloaded;
            if (online.DirectDownloadUrl.Length > 0 || online.Id.StartsWith("EMU_", StringComparison.OrdinalIgnoreCase))
            {
                downloaded = await DownloadApiMapAsync(online);
            }
            else
            {
                DeleteFileIfPresent(entry.FilePath);
                downloaded = await Net.DownloadFile(online.Meta, CorePath.ZoneSaveUser, online.Language);
            }

            if (downloaded == null || !downloaded.Exists || !Zone.IsImportValid(downloaded.FullName))
                throw new InvalidDataException(Text("下载后的月门地图校验失败", "Downloaded moongate map validation failed"));

            ApplyOnlineSourceDate(downloaded.FullName, online.SourceDate);
            if (!string.Equals(downloaded.FullName, entry.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                var oldPersistentPath = entry.PersistentPath;
                var newPersistentPath = GetPersistentMapPath(downloaded.FullName);
                var oldDisabledMarkerPath = GetPersistenceDisabledMarkerPath(entry.FilePath);
                var newDisabledMarkerPath = GetPersistenceDisabledMarkerPath(downloaded.FullName);
                if (File.Exists(oldPersistentPath) &&
                    !string.Equals(oldPersistentPath, newPersistentPath, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteFileIfPresent(newPersistentPath);
                    File.Move(oldPersistentPath, newPersistentPath);
                }
                if (File.Exists(oldDisabledMarkerPath) &&
                    !string.Equals(oldDisabledMarkerPath, newDisabledMarkerPath, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteFileIfPresent(newDisabledMarkerPath);
                    File.Move(oldDisabledMarkerPath, newDisabledMarkerPath);
                }
                DeleteFileIfPresent(entry.FilePath);
            }

            InvalidateLocalMaps();
            Log = Text("本地月门缓存已更新: ", "Local moongate cache updated: ") + entry.Id;
        }
        catch (Exception ex)
        {
            if (backupCreated && File.Exists(backupPath))
            {
                try
                {
                    File.Copy(backupPath, entry.FilePath, true);
                    File.SetLastWriteTimeUtc(entry.FilePath, File.GetLastWriteTimeUtc(backupPath));
                }
                catch
                {
                }
            }
            Log = Text("更新本地月门失败: ", "Failed to update local moongate: ") + ShortError(ex);
        }
        finally
        {
            DeleteFileIfPresent(backupPath);
            _updatingLocalMapId = "";
            InvalidateLocalMaps();
            RefreshPage();
        }
    }
    private static string SanitizeMapFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var c in (value ?? "").Trim())
        {
            if (Array.IndexOf(invalid, c) < 0)
                builder.Append(c);
        }
        return builder.Length == 0 ? "moongate" : builder.ToString();
    }
    private static string ResolveLocalMapId(MapMetaData metadata, FileInfo file)
    {
        var fileId = Path.GetFileNameWithoutExtension(file.Name) ?? "";
        if (fileId.StartsWith("EMU_", StringComparison.OrdinalIgnoreCase))
            return CanonicalizeMapId(fileId);
        var metadataId = CanonicalizeMapId(metadata.id ?? "");
        return metadataId.Length > 0 ? metadataId : CanonicalizeMapId(fileId);
    }
    private static string NormalizeLocalFileDate(DateTime value)
    {
        if (value == default)
            return "";
        DateTimeOffset absoluteTime;
        if (value.Kind == DateTimeKind.Utc)
        {
            absoluteTime = new DateTimeOffset(value, TimeSpan.Zero);
        }
        else if (value.Kind == DateTimeKind.Local)
        {
            absoluteTime = new DateTimeOffset(value);
        }
        else
        {
            absoluteTime = new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value));
        }
        return absoluteTime.ToOffset(TimeSpan.FromHours(8))
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
    private static bool IsLocalMapCurrent(LocalEntry local, MapEntry online)
    {
        var localTime = (local.CachedAt ?? "").Trim();
        var onlineTime = (online.SourceDate ?? "").Trim();
        if (localTime.Length > 0 && onlineTime.Length > 0)
            return string.Equals(localTime, onlineTime, StringComparison.Ordinal);
        return local.Version == online.Version;
    }
    private static void ApplyOnlineSourceDate(string filePath, string sourceDate)
    {
        if (!DateTime.TryParseExact(
                sourceDate ?? "",
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var beijingTime))
        {
            return;
        }

        var beijingOffset = new DateTimeOffset(
            DateTime.SpecifyKind(beijingTime, DateTimeKind.Unspecified),
            TimeSpan.FromHours(8));
        File.SetLastWriteTimeUtc(filePath, beijingOffset.UtcDateTime);
    }
    private static string GetPersistentMapPath(string originalPath)
    {
        return (originalPath ?? "").Trim() + ".save";
    }
    private static string GetPersistenceDisabledMarkerPath(string originalPath)
    {
        return GetPersistentMapPath(originalPath) + ".disabled";
    }
    private static string GetLegacyLocalBackupDirectory()
    {
        return Path.Combine(CorePath.ZoneSaveUser, "ElinModifierBackup");
    }
    private static string GetLegacyLocalBackupPath(string id)
    {
        return Path.Combine(GetLegacyLocalBackupDirectory(), SanitizeMapFileName(id) + ".z");
    }
    private static void DeleteFileIfPresent(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            File.Delete(path);
    }
    private static void DeleteLegacyBackupDirectoryIfEmpty()
    {
        var directory = GetLegacyLocalBackupDirectory();
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory, false);
    }
    private static void RemoveOfficialDownloadCacheEntry(string id)
    {
        try
        {
            var path = Path.Combine(CorePath.ZoneSaveUser, "cache.txt");
            var cache = IO.LoadFile<Net.DownloadCahce>(path);
            if (cache?.items == null || !cache.items.Remove(id))
                return;
            IO.SaveFile(path, cache);
        }
        catch
        {
        }
    }
    private void InvalidateLocalMaps(bool preservePage = false)
    {
        _localMapsLoaded = false;
        _localMaps.Clear();
        if (!preservePage)
            LocalPage = 0;
    }
}
