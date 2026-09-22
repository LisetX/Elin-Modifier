using System;
using System.Collections.Generic;

internal sealed partial class WorldMapModule
{
    internal bool EnsureTerrain() => HasTerrain || TryRefreshTerrain();

    internal List<WorldMapSearchHit> RunDetachedSearch(string? query, int limit)
    {
        var result = new List<WorldMapSearchHit>();
        var normalized = (query ?? "").Trim();
        if (normalized.Length == 0 || !EnsureTerrain())
            return result;

        var savedQuery = _searchQuery;
        var savedTerms = _searchTerms;
        var savedCells = _searchCells;
        var savedHits = _searchHits;
        var savedVersion = _searchVersion;
        try
        {
            _searchQuery = normalized;
            _searchTerms = normalized.Split(
                new[] { ' ', '\t', '　' },
                StringSplitOptions.RemoveEmptyEntries);
            RebuildSearchResults();
            var hits = _searchHits;
            if (hits != null)
            {
                var count = limit > 0 ? Math.Min(limit, hits.Count) : hits.Count;
                for (var i = 0; i < count; i++)
                    result.Add(hits[i]);
            }
        }
        catch
        {
        }
        finally
        {
            _searchQuery = savedQuery;
            _searchTerms = savedTerms;
            _searchCells = savedCells;
            _searchHits = savedHits;
            _searchVersion = savedVersion;
        }
        return result;
    }

    internal bool TryPlanRoutePreview(
        int gridX,
        int gridY,
        out int steps,
        out int roadCells,
        out int landCells,
        out int shoreCells,
        out int seaCells)
    {
        steps = 0;
        roadCells = 0;
        landCells = 0;
        shoreCells = 0;
        seaCells = 0;
        if (!EnsureTerrain())
            return false;
        var terrain = _terrain!;
        if (!TryGetPlayerGrid(out var playerX, out var playerY))
            return false;

        var startX = playerX - terrain.MinX;
        var startY = playerY - terrain.MinY;
        var goalX = gridX - terrain.MinX;
        var goalY = gridY - terrain.MinY;
        if (!terrain.Contains(startX, startY) || !terrain.Contains(goalX, goalY))
            return false;
        if ((long)terrain.Width * terrain.Height > WorldMapMaxCells)
            return false;

        var path = FindNavigationPath(terrain, startX, startY, goalX, goalY);
        if (path == null || path.Count == 0)
            return false;

        steps = path.Count - 1;
        for (var i = 0; i < path.Count; i++)
        {
            var cell = path[i];
            switch (terrain.GetKind(cell % terrain.Width, cell / terrain.Width))
            {
                case WorldMapTerrain.KindRoad: roadCells++; break;
                case WorldMapTerrain.KindLand: landCells++; break;
                case WorldMapTerrain.KindShore: shoreCells++; break;
                case WorldMapTerrain.KindSea: seaCells++; break;
            }
        }
        return true;
    }
}
