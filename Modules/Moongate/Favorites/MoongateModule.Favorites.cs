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
    internal IReadOnlyList<FavoriteEntry> GetFavorites()
    {
        var result = new List<FavoriteEntry>();
        var player = GameAccess.Runtime.Player;
        if (player?.favMoongate == null)
            return result;

        List<MapMetaData>? localMaps = null;
        try
        {
            localMaps = new TraitMoongate().ListSavedUserMap();
        }
        catch
        {
            localMaps = null;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < player.favMoongate.Count; i++)
        {
            var rawId = player.favMoongate[i];
            var id = (rawId ?? "").Trim();
            if (id.Length == 0 || !seen.Add(id))
                continue;

            var online = FindOnlineMap(id, "");
            if (online != null)
            {
                result.Add(new FavoriteEntry(
                    online.Id,
                    online.Title,
                    online.Author,
                    online.Language,
                    online.SourceDate,
                    online.Version,
                    online.SourceKind));
                continue;
            }

            var local = localMaps?.FirstOrDefault(map => MapMetadataMatchesId(map, id));
            result.Add(new FavoriteEntry(
                id,
                local?.name ?? "",
                "",
                "",
                "",
                local?.version ?? 0,
                local != null ? SourceLocal : ""));
        }
        return result;
    }
    internal bool IsFavorite(string id)
    {
        var favorites = GameAccess.Runtime.Player?.favMoongate;
        if (favorites == null)
            return false;
        return favorites.Any(value => MapIdsEqual(value, id));
    }
    internal void AddSpecifiedFavorite()
    {
        AddFavorite(SpecifiedMapId);
    }
    internal void AddFavorite(string id)
    {
        id = ResolveCanonicalId(id);
        if (!TryGetFavorites(out var favorites) || id.Length == 0)
        {
            Log = Text("请输入有效的地图ID", "Enter a valid map ID");
            RefreshPage();
            return;
        }

        if (favorites.Any(value => MapIdsEqual(value, id)))
        {
            Log = Text("该月门已收藏: ", "Moongate already favorited: ") + id;
            RefreshPage();
            return;
        }

        favorites.Add(id);
        SpecifiedMapId = id;
        Log = Text("已收藏月门: ", "Moongate favorited: ") + id;
        RefreshPage();
    }
    internal void RemoveFavorite(string id)
    {
        if (!TryGetFavorites(out var favorites))
            return;

        var index = favorites.FindIndex(value => MapIdsEqual(value, id));
        if (index < 0)
            return;

        favorites.RemoveAt(index);
        Log = Text("已取消收藏月门: ", "Moongate removed from favorites: ") + id;
        RefreshPage();
    }
    private static bool TryGetFavorites(out List<string> favorites)
    {
        favorites = GameAccess.Runtime.Player?.favMoongate!;
        return favorites != null;
    }
}
