using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class AllPurposeWorkbenchModule
{
    internal bool Enabled { get; private set; }
    internal bool DefaultByWorkbench { get; private set; }
    internal string DefaultTabTypeConfigValue => DefaultByWorkbench ? "workbench" : "itemCategory";

    internal void Load(bool enabled, string defaultTabType)
    {
        Enabled = enabled;
        DefaultByWorkbench = string.Equals(
            (defaultTabType ?? "").Trim(),
            "workbench",
            StringComparison.OrdinalIgnoreCase);
    }

    internal void Reset()
    {
        Enabled = false;
        DefaultByWorkbench = false;
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        Enabled = enabled;
        return true;
    }

    internal void SetDefaultByWorkbench(bool byWorkbench)
    {
        DefaultByWorkbench = byWorkbench;
    }
}

