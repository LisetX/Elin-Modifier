using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Bootstrap;
using HarmonyLib;
using UnityEngine;

public sealed partial class ElinModifierPlugin
{
    private static void RefreshGodArtifactFaithRestrictionState()
    {
        try
        {
            var playerCharacter = GameAccess.Characters.PlayerCharacter;
            var faction = playerCharacter?.faction;
            if (faction == null)
                return;
            faction.charaElements = new ElementContainerFaction();
            faction.charaElements.OnJoinFaith();
            GameAccess.Characters.Refresh(playerCharacter, false);
        }
        catch
        {
        }
    }
}
