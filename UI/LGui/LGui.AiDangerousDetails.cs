using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private float AddLGuiAiDangerousDetails(RectTransform content, float y)
    {
        if (_aiPendingDangerousActions.Count == 0 && _aiRuntimePatches.Count == 0)
            return y;
        y = AddLGuiSectionTitle(content, T("高危运行时操作", "High-risk runtime actions"), y + 8f);
        for (var i = 0; i < _aiPendingDangerousActions.Count; i++)
        {
            var action = _aiPendingDangerousActions[i];
            y = AddLGuiReadOnlyRow(content, "#" + action.Id + " " + action.ToolName, action.Summary, y, 300f);
        }
        foreach (var pair in _aiRuntimePatches)
            y = AddLGuiReadOnlyRow(content, pair.Key, pair.Value.TargetDescription + " [" + pair.Value.Mode + "]", y, 420f);
        return y;
    }
}
