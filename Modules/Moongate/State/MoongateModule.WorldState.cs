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
    internal int OnlineMapCount => _onlineMaps.Count;
    internal void TickWorldState()
    {
        if (_shutdown)
            return;

        var isInside = IsInsideMoongateWorld;
        if (!_moongateWorldStateInitialized)
        {
            _moongateWorldStateInitialized = true;
            _lastInsideMoongateWorld = isInside;
            return;
        }

        if (_lastInsideMoongateWorld == isInside)
            return;

        if (_lastInsideMoongateWorld && !isInside && _entering)
        {
            _generation++;
            _entering = false;
            _pendingEnterId = "";
            _pendingEnterLanguage = "";
        }

        _lastInsideMoongateWorld = isInside;
        RefreshPage();
    }
    internal void SetLandholderPrivilegesEnabled(bool enabled)
    {
        if (LandholderPrivilegesEnabled == enabled)
            return;

        LandholderPrivilegesEnabled = enabled;
        _host.SaveConfigFromModule(false, false);
        RefreshLandholderPermissionUi();
    }
    internal bool HasLandholderPrivileges(Zone? zone)
    {
        if (!LandholderPrivilegesEnabled || zone == null)
            return false;

        try
        {
            return ReferenceEquals(GameAccess.World.CurrentZone, zone) &&
                   zone is Zone_User &&
                   zone.instance is ZoneInsstanceMoongate;
        }
        catch
        {
            return false;
        }
    }
    private static void RefreshLandholderPermissionUi()
    {
        try
        {
            WidgetMenuPanel.OnChangeMode();
            var hotbars = GameAccess.Runtime.Player?.hotbars;
            if (hotbars == null || GameAccess.Runtime.Core?.IsGameStarted != true)
                return;

            hotbars.ResetHotbar(2);
            hotbars.ResetHotbar(3);
            hotbars.ResetHotbar(4);
            if (hotbars.bars[2] != null)
                hotbars.bars[2].dirty = true;
            if (hotbars.bars[3] != null)
                hotbars.bars[3].dirty = true;
            if (hotbars.bars[4] != null)
                hotbars.bars[4].dirty = true;
        }
        catch
        {
        }
    }
}
