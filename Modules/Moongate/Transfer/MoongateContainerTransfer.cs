using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class MoongateContainerTransfer
{
    private const string PayloadEntryName = "elinmodifier.container-items.json";
    private const string PayloadSchema = "elinmodifier.container-items";
    private const int PayloadVersion = 2;

    internal static void AttachToExport(string archivePath, Map map)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || map?.things == null || !File.Exists(archivePath))
            return;

        var payload = BuildPayload(map);
        if (payload.Containers.Count == 0)
            return;

        var json = SerializePayload(payload);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        archive.GetEntry(PayloadEntryName)?.Delete();
        var entry = archive.CreateEntry(PayloadEntryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(json);
    }

    internal static void ClearExtractedPayload(Zone zone)
    {
        var path = GetExtractedPayloadPath(zone);
        if (path.Length == 0)
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    internal static void RestoreExtractedPayload(Map map, Zone zone)
    {
        var path = GetExtractedPayloadPath(zone);
        if (map?.things == null || path.Length == 0 || !File.Exists(path) || GameAccess.Runtime.Game?.cards == null)
            return;

        var payloadFile = new FileInfo(path);
        if (payloadFile.Length <= 0)
        {
            UnityEngine.Debug.LogWarning(
                "Elin Modifier: skipped empty moongate container payload " +
                Path.GetFileName(path));
            return;
        }

        if (!TryDeserializePayload(
                File.ReadAllText(path, Encoding.UTF8),
                out var payload,
                out var error))
        {
            UnityEngine.Debug.LogWarning(
                "Elin Modifier: skipped moongate container payload " +
                Path.GetFileName(path) + ": " + error);
            return;
        }

        var candidates = map.things
            .Where(IsExportedContainer)
            .GroupBy(BuildContainerKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var saved in payload.Containers)
        {
            if (saved == null || saved.Items == null ||
                !candidates.TryGetValue(saved.Key ?? "", out var matches) ||
                saved.Ordinal < 0 || saved.Ordinal >= matches.Count)
            {
                continue;
            }

            RestoreContainer(matches[saved.Ordinal], saved);
        }
    }

    private static MoongateContainerPayload BuildPayload(Map map)
    {
        var payload = new MoongateContainerPayload { Version = PayloadVersion };
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var container in map.things.Where(IsExportedContainer))
        {
            var key = BuildContainerKey(container);
            occurrences.TryGetValue(key, out var ordinal);
            occurrences[key] = ordinal + 1;

            payload.Containers.Add(new MoongateContainerEntry
            {
                Key = key,
                Ordinal = ordinal,
                LockLevel = MoongateContainerTransferPolicy.RestoredLockLevel,
                Items = container.things == null
                    ? new List<Thing>()
                    : new List<Thing>(container.things)
            });
        }

        return payload;
    }

    private static string SerializePayload(MoongateContainerPayload payload)
    {
        var containers = new JArray();
        foreach (var saved in payload.Containers)
        {
            var serializer = JsonSerializer.Create(GameIO.jsWriteGame);
            var items = JToken.FromObject(saved.Items ?? new List<Thing>(), serializer);
            if (items.Type != JTokenType.Array)
                throw new JsonSerializationException("Moongate container items must serialize as an array.");

            containers.Add(new JObject
            {
                ["key"] = saved.Key ?? "",
                ["ordinal"] = saved.Ordinal,
                ["lockLevel"] = saved.LockLevel,
                ["items"] = items
            });
        }

        return new JObject
        {
            ["schema"] = PayloadSchema,
            ["version"] = PayloadVersion,
            ["containers"] = containers
        }.ToString(Formatting.None);
    }

    private static bool TryDeserializePayload(
        string json,
        out MoongateContainerPayload payload,
        out string error)
    {
        payload = new MoongateContainerPayload { Version = PayloadVersion };
        error = "";

        try
        {
            var root = JObject.Parse(json ?? "");
            if (!string.Equals(root.Value<string>("schema"), PayloadSchema, StringComparison.Ordinal))
            {
                error = "unsupported or missing schema";
                return false;
            }

            if (!TryReadInt(root, "version", out var version) || version != PayloadVersion)
            {
                error = "unsupported payload version";
                return false;
            }

            if (!(root["containers"] is JArray containers) ||
                containers.Count > MoongateContainerLimits.MaxContainerCount)
            {
                error = "invalid container list";
                return false;
            }

            var totalItems = 0;
            foreach (var token in containers)
            {
                if (!(token is JObject container))
                {
                    error = "invalid container entry";
                    return false;
                }

                var keyToken = container["key"];
                var key = keyToken?.Type == JTokenType.String ? keyToken.Value<string>() ?? "" : "";
                if (!IsValidContainerKey(key) ||
                    !TryReadInt(container, "ordinal", out var ordinal) ||
                    ordinal < 0 || ordinal >= MoongateContainerLimits.MaxContainerCount ||
                    !TryReadInt(container, "lockLevel", out var lockLevel) ||
                    lockLevel < 0 ||
                    !(container["items"] is JArray items) ||
                    items.Count > MoongateContainerLimits.MaxItemsPerContainer)
                {
                    error = "invalid container fields";
                    return false;
                }

                totalItems += items.Count;
                if (totalItems > MoongateContainerLimits.MaxTotalItemCount)
                {
                    error = "oversized item list";
                    return false;
                }

                var serializer = JsonSerializer.Create(GameIO.jsReadGame);
                var restoredItems = items.ToObject<List<Thing>>(serializer);
                if (restoredItems == null || restoredItems.Count != items.Count)
                {
                    error = "invalid serialized items";
                    return false;
                }

                payload.Containers.Add(new MoongateContainerEntry
                {
                    Key = key,
                    Ordinal = ordinal,
                    LockLevel = lockLevel,
                    Items = restoredItems
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            payload = new MoongateContainerPayload { Version = PayloadVersion };
            return false;
        }
    }

    private static bool TryReadInt(JObject value, string name, out int result)
    {
        result = 0;
        var token = value[name];
        if (token?.Type != JTokenType.Integer)
            return false;
        try
        {
            result = token.Value<int>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidContainerKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MoongateContainerLimits.MaxContainerKeyLength)
            return false;

        var separators = 0;
        for (var i = 0; i < key.Length; i++)
            if (key[i] == '\u001f')
                separators++;
        return separators == 9;
    }

    private static void RestoreContainer(Thing destination, MoongateContainerEntry saved)
    {
        try
        {
            destination.things.DestroyAll();
            destination.things.SetOwner(destination);

            foreach (var item in saved.Items)
            {
                if (item == null || item.Num <= 0 || item.isDestroyed)
                    continue;

                GameAccess.Runtime.Game.cards.AssignUIDRecursive(item);
                item.parent = destination;
                destination.things.Add(item);
            }

            destination.c_lockLv = MoongateContainerTransferPolicy.RestoredLockLevel;
            destination.c_lockedHard = false;
            destination.c_revealLock = false;
            destination.things.RefreshGridRecursive();
            destination.SetDirtyWeight();
        }
        catch
        {
        }
    }

    private static bool IsExportedContainer(Thing thing)
    {
        return thing != null &&
               MoongateContainerTransferPolicy.ShouldTransfer(thing.IsContainer, thing.c_altName);
    }

    private static string BuildContainerKey(Thing thing)
    {
        const string separator = "\u001f";
        return string.Join(separator, new[]
        {
            thing.id ?? "",
            thing.c_idEditor ?? "",
            thing.c_idTrait ?? "",
            thing.pos?.x.ToString(CultureInfo.InvariantCulture) ?? "0",
            thing.pos?.z.ToString(CultureInfo.InvariantCulture) ?? "0",
            ((int)thing.placeState).ToString(CultureInfo.InvariantCulture),
            thing.dir.ToString(CultureInfo.InvariantCulture),
            thing.idMaterial.ToString(CultureInfo.InvariantCulture),
            thing.refVal.ToString(CultureInfo.InvariantCulture),
            thing.idSkin.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static string GetExtractedPayloadPath(Zone zone)
    {
        if (zone == null || string.IsNullOrWhiteSpace(zone.pathTemp))
            return "";
        return Path.Combine(zone.pathTemp, PayloadEntryName);
    }

    private sealed class MoongateContainerPayload
    {
        public int Version { get; set; }

        public List<MoongateContainerEntry> Containers { get; set; } = new List<MoongateContainerEntry>();
    }

    private sealed class MoongateContainerEntry
    {
        public string Key { get; set; } = "";

        public int Ordinal { get; set; }

        public int LockLevel { get; set; }

        public List<Thing> Items { get; set; } = new List<Thing>();
    }
}

