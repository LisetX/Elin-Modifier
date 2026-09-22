using System;
using System.Collections.Generic;

internal sealed partial class WorldMapModule
{
    private const int WorldMapNavCostRoad = 6;
    private const int WorldMapNavCostLand = 24;
    private const int WorldMapNavCostShore = 96;
    private const int WorldMapNavCostSea = 260;
    private const int WorldMapNavMaxVisited = 400_000;

    private static readonly int[] WorldMapNavDx = { 0, 0, -1, 1, -1, 1, -1, 1 };
    private static readonly int[] WorldMapNavDy = { -1, 1, 0, 0, -1, -1, 1, 1 };

    private bool _navigationEnabled;
    private int _navTargetX = int.MinValue;
    private int _navTargetY = int.MinValue;
    private int _navOriginX = int.MinValue;
    private int _navOriginY = int.MinValue;
    private int _navVersion;
    private bool _navDirty = true;
    private List<int>? _navPath;
    private HashSet<int>? _navCells;

    internal bool NavigationEnabled
    {
        get => _navigationEnabled;
        set
        {
            if (_navigationEnabled == value)
                return;
            _navigationEnabled = value;
            _navDirty = true;
            if (!value)
                HideNavigationOverlay();
        }
    }

    internal bool HasNavigationTarget => _navTargetX != int.MinValue && _navTargetY != int.MinValue;

    internal int NavigationTargetX => _navTargetX;

    internal int NavigationTargetY => _navTargetY;

    internal int NavigationVersion => _navVersion;

    internal void RefreshNavigation() => RefreshNavigationPath();

    internal int NavigationSteps => _navPath == null ? 0 : Math.Max(0, _navPath.Count - 1);

    internal void SetNavigationTarget(int gridX, int gridY)
    {
        if (_navTargetX == gridX && _navTargetY == gridY)
            return;
        _navTargetX = gridX;
        _navTargetY = gridY;
        _navDirty = true;
    }

    internal void ClearNavigationTarget()
    {
        if (!HasNavigationTarget)
            return;
        _navTargetX = int.MinValue;
        _navTargetY = int.MinValue;
        _navPath = null;
        _navCells = null;
        _navDirty = true;
        _navVersion++;
        HideNavigationOverlay();
    }

    internal bool IsNavigationCell(int cellIndex) =>
        _navCells != null && _navCells.Contains(cellIndex);

    internal bool TryGetNavigationTargetCell(out int cellX, out int cellY)
    {
        cellX = 0;
        cellY = 0;
        var terrain = _terrain;
        if (terrain == null || !HasNavigationTarget)
            return false;
        cellX = _navTargetX - terrain.MinX;
        cellY = _navTargetY - terrain.MinY;
        return terrain.Contains(cellX, cellY);
    }

    private void RefreshNavigationPath()
    {
        if (!_navigationEnabled || !HasNavigationTarget)
        {
            _navDirty = false;
            _navOriginX = int.MinValue;
            _navOriginY = int.MinValue;
            if (_navPath == null && _navCells == null)
                return;
            _navPath = null;
            _navCells = null;
            _navVersion++;
            return;
        }

        if (!TryGetPlayerGrid(out var playerX, out var playerY))
            return;
        if (playerX != _navOriginX || playerY != _navOriginY)
            _navDirty = true;
        if (!_navDirty)
            return;
        _navDirty = false;
        _navOriginX = playerX;
        _navOriginY = playerY;
        _navPath = null;
        _navCells = null;
        _navVersion++;

        var terrain = _terrain;
        if (terrain == null)
            return;

        var startX = playerX - terrain.MinX;
        var startY = playerY - terrain.MinY;
        var goalX = _navTargetX - terrain.MinX;
        var goalY = _navTargetY - terrain.MinY;
        if (!terrain.Contains(startX, startY) || !terrain.Contains(goalX, goalY))
            return;
        if ((long)terrain.Width * terrain.Height > WorldMapMaxCells)
            return;

        var path = FindNavigationPath(terrain, startX, startY, goalX, goalY);
        if (path == null || path.Count == 0)
            return;
        _navPath = path;
        _navCells = new HashSet<int>(path);
    }

    private static List<int>? FindNavigationPath(
        WorldMapTerrain terrain,
        int startX,
        int startY,
        int goalX,
        int goalY)
    {
        var width = terrain.Width;
        var height = terrain.Height;
        var total = width * height;
        if (total <= 0)
            return null;

        var start = startY * width + startX;
        var goal = goalY * width + goalX;
        if (start == goal)
            return new List<int> { start };

        var scores = new int[total];
        var previous = new int[total];
        var states = new byte[total];
        for (var i = 0; i < total; i++)
        {
            scores[i] = int.MaxValue;
            previous[i] = -1;
        }

        var heap = new WorldMapNavHeap(Math.Min(total, 4096));
        scores[start] = 0;
        states[start] = 1;
        heap.Push(start, NavHeuristic(startX, startY, goalX, goalY));

        var visited = 0;
        while (heap.Count > 0)
        {
            var current = heap.Pop();
            if (current == goal)
                return BuildNavigationPath(previous, current);
            if (states[current] == 2)
                continue;
            states[current] = 2;
            if (++visited > WorldMapNavMaxVisited)
                return null;

            var cx = current % width;
            var cy = current / width;
            for (var d = 0; d < 8; d++)
            {
                var nx = cx + WorldMapNavDx[d];
                var ny = cy + WorldMapNavDy[d];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    continue;
                var next = ny * width + nx;
                if (states[next] == 2)
                    continue;

                var step = NavStepCost(terrain.GetKind(nx, ny));
                if (step <= 0)
                {
                    if (next != goal)
                        continue;
                    step = WorldMapNavCostSea;
                }
                if (d >= 4)
                {
                    if (NavStepCost(terrain.GetKind(cx, ny)) <= 0 ||
                        NavStepCost(terrain.GetKind(nx, cy)) <= 0)
                        continue;
                    step = step * 7 / 5;
                }

                var tentative = scores[current] + step;
                if (tentative >= scores[next])
                    continue;
                scores[next] = tentative;
                previous[next] = current;
                states[next] = 1;
                heap.Push(next, tentative + NavHeuristic(nx, ny, goalX, goalY));
            }
        }
        return null;
    }

    private static List<int> BuildNavigationPath(int[] previous, int goal)
    {
        var path = new List<int>();
        var cursor = goal;
        while (cursor >= 0 && path.Count <= previous.Length)
        {
            path.Add(cursor);
            cursor = previous[cursor];
        }
        path.Reverse();
        return path;
    }

    private static int NavHeuristic(int x, int y, int goalX, int goalY)
    {
        var dx = Math.Abs(x - goalX);
        var dy = Math.Abs(y - goalY);
        var diagonal = Math.Min(dx, dy);
        var straight = Math.Max(dx, dy) - diagonal;
        return (straight + diagonal * 7 / 5) * WorldMapNavCostRoad;
    }

    private static int NavStepCost(byte kind)
    {
        switch (kind)
        {
            case WorldMapTerrain.KindRoad: return WorldMapNavCostRoad;
            case WorldMapTerrain.KindLand: return WorldMapNavCostLand;
            case WorldMapTerrain.KindShore: return WorldMapNavCostShore;
            case WorldMapTerrain.KindSea: return WorldMapNavCostSea;
            default: return 0;
        }
    }

    private sealed class WorldMapNavHeap
    {
        private int[] _nodes;
        private int[] _keys;
        private int _count;

        internal WorldMapNavHeap(int capacity)
        {
            var size = Math.Max(16, capacity);
            _nodes = new int[size];
            _keys = new int[size];
        }

        internal int Count => _count;

        internal void Push(int node, int key)
        {
            if (_count == _nodes.Length)
            {
                Array.Resize(ref _nodes, _count * 2);
                Array.Resize(ref _keys, _count * 2);
            }
            var index = _count++;
            _nodes[index] = node;
            _keys[index] = key;
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (_keys[parent] <= _keys[index])
                    break;
                Swap(parent, index);
                index = parent;
            }
        }

        internal int Pop()
        {
            var result = _nodes[0];
            _count--;
            _nodes[0] = _nodes[_count];
            _keys[0] = _keys[_count];
            var index = 0;
            while (true)
            {
                var left = index * 2 + 1;
                if (left >= _count)
                    break;
                var best = left;
                var right = left + 1;
                if (right < _count && _keys[right] < _keys[left])
                    best = right;
                if (_keys[index] <= _keys[best])
                    break;
                Swap(index, best);
                index = best;
            }
            return result;
        }

        private void Swap(int a, int b)
        {
            var node = _nodes[a];
            _nodes[a] = _nodes[b];
            _nodes[b] = node;
            var key = _keys[a];
            _keys[a] = _keys[b];
            _keys[b] = key;
        }
    }
}
