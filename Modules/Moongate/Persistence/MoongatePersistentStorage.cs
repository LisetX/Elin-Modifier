using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

internal static class MoongatePersistentStorage
{
    private const string PersistentSuffix = ".z.save";
    private const string PersistentMarker = "(持久化)";

    [ThreadStatic]
    private static bool _saving;

    private static readonly HashSet<string> SuppressedPaths =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    internal static void SuppressCurrentSave(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !(GameAccess.World.CurrentZone is Zone_User activeUserZone) ||
            !string.Equals(activeUserZone.path, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (SuppressedPaths)
            SuppressedPaths.Add(NormalizePath(path));
    }

    internal static void AllowCurrentSave(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        lock (SuppressedPaths)
            SuppressedPaths.Remove(NormalizePath(path));
    }

    internal static bool IsPersistenceEnabled(string originalPath)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
            return false;
        if (originalPath.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase))
            originalPath = originalPath.Substring(0, originalPath.Length - ".save".Length);
        var persistentPath = originalPath + ".save";
        return File.Exists(persistentPath) && !File.Exists(persistentPath + ".disabled");
    }

    internal static void MarkPersistentMaps(List<MapMetaData> maps)
    {
        if (maps == null)
            return;
        for (var i = 0; i < maps.Count; i++)
        {
            var map = maps[i];
            if (map == null || string.IsNullOrWhiteSpace(map.path) ||
                map.path.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase) ||
                !IsPersistenceEnabled(map.path))
            {
                continue;
            }
            map.name = AddPersistentMarker(map.name);
        }
    }

    internal static MapMetaData PrepareMapForLoad(MapMetaData map)
    {
        if (map == null || string.IsNullOrWhiteSpace(map.path))
            return map;

        var originalId = map.id;
        var originalName = RemovePersistentMarker(map.name);
        var originalDate = map.date;
        var originalPath = map.path.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase)
            ? map.path.Substring(0, map.path.Length - ".save".Length)
            : map.path;
        var persistentPath = originalPath + ".save";
        if (IsPersistenceEnabled(originalPath))
        {
            try
            {
                var persistentMap = Map.GetMetaData(persistentPath);
                if (persistentMap != null && persistentMap.IsValidVersion())
                {
                    persistentMap.path = persistentPath;
                    persistentMap.id = originalId;
                    persistentMap.date = originalDate;
                    persistentMap.name = AddPersistentMarker(
                        string.IsNullOrWhiteSpace(persistentMap.name) ? originalName : persistentMap.name);
                    map = persistentMap;
                }
            }
            catch
            {
            }
        }
        else if (map.path.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase) && File.Exists(originalPath))
        {
            try
            {
                var originalMap = Map.GetMetaData(originalPath);
                if (originalMap != null && originalMap.IsValidVersion())
                {
                    originalMap.path = originalPath;
                    originalMap.id = originalId;
                    originalMap.date = originalDate;
                    originalMap.name = originalName;
                    map = originalMap;
                }
            }
            catch
            {
            }
        }

        RefreshRegisteredZone(map.id, map.path, map.name,
            map.path.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase));
        return map;
    }

    internal static void PrepareZoneForMove(Zone zone)
    {
        if (!(zone is Zone_User userZone) || string.IsNullOrWhiteSpace(userZone.path))
            return;

        if (userZone.path.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var originalPath = userZone.path.Substring(0, userZone.path.Length - ".save".Length);
            if (IsPersistenceEnabled(originalPath))
            {
                userZone.name = AddPersistentMarker(userZone.name);
                return;
            }

            userZone.path = originalPath;
            userZone.name = RemovePersistentMarker(userZone.name);
            return;
        }

        var persistentPath = userZone.path + ".save";
        if (!IsPersistenceEnabled(userZone.path))
        {
            userZone.name = RemovePersistentMarker(userZone.name);
            return;
        }

        userZone.path = persistentPath;
        userZone.name = AddPersistentMarker(userZone.name);
    }

    internal static void RefreshRegisteredZone(
        string id,
        string originalPath,
        string displayName,
        bool persistent)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(originalPath) ||
            GameAccess.Runtime.Game?.spatials == null)
        {
            return;
        }

        var zone = GameAccess.Runtime.Game.spatials.Find((Zone_User candidate) => candidate.idUser == id);
        if (zone == null)
            return;
        zone.path = persistent
            ? (originalPath.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase)
                ? originalPath
                : originalPath + ".save")
            : (originalPath.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase)
                ? originalPath.Substring(0, originalPath.Length - ".save".Length)
                : originalPath);
        zone.name = persistent
            ? AddPersistentMarker(displayName)
            : RemovePersistentMarker(displayName);
    }

    internal static void SaveBeforeLeaving(Zone zone)
    {
        if (_saving || zone == null || zone.map == null ||
            !(zone is Zone_User userZone) ||
            !(zone.instance is ZoneInsstanceMoongate) ||
            string.IsNullOrWhiteSpace(userZone.path) ||
            !userZone.path.EndsWith(PersistentSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var destination = userZone.path;
        var originalPath = destination.Substring(0, destination.Length - ".save".Length);
        if (!IsPersistenceEnabled(originalPath))
            return;
        lock (SuppressedPaths)
        {
            if (SuppressedPaths.Remove(NormalizePath(destination)))
                return;
        }
        var temporary = destination + ".writing";
        try
        {
            _saving = true;
            DeleteIfPresent(temporary);
            zone.Export(temporary, null, true);
            MoongateContainerTransfer.AttachToExport(temporary, zone.map);
            if (!File.Exists(temporary) || !Zone.IsImportValid(temporary))
                throw new InvalidDataException("Persistent moongate export validation failed");

            if (File.Exists(destination))
            {
                try
                {
                    File.Replace(temporary, destination, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporary, destination, true);
                    DeleteIfPresent(temporary);
                }
                catch (IOException)
                {
                    File.Copy(temporary, destination, true);
                    DeleteIfPresent(temporary);
                }
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Elin Modifier: failed to persist moongate map: " + ex.Message);
        }
        finally
        {
            DeleteIfPresent(temporary);
            _saving = false;
        }
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path ?? "";
        }
    }

    private static string AddPersistentMarker(string value)
    {
        var name = RemovePersistentMarker(value);
        return name + PersistentMarker;
    }

    private static string RemovePersistentMarker(string value)
    {
        var name = (value ?? "").TrimEnd();
        while (name.EndsWith(PersistentMarker, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - PersistentMarker.Length).TrimEnd();
        return name;
    }
}

