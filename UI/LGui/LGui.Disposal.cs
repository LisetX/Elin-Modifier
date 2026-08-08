using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void DisposeLGuiVirtualLists()
    {
        _modules.LGuiFocus.Clear();
        _lGuiFeatureList?.Dispose();
        _lGuiCharacterList?.Dispose();
        _lGuiItemList?.Dispose();
        _lGuiNpcList?.Dispose();
        _lGuiHomeList?.Dispose();
        _modules.Probability.DisposeUi();
        _lGuiDebugList?.Dispose();
        _lGuiEmpList?.Dispose();
        _lGuiFeatureList = null;
        _lGuiCharacterList = null;
        _lGuiItemList = null;
        _lGuiNpcList = null;
        _lGuiNpcIdInput = null;
        _lGuiNpcRelationshipLabels.Clear();
        _lGuiHomeList = null;
        _lGuiDebugList = null;
        _lGuiEmpList = null;
        _lGuiCharacterTargetText = null;
        _lGuiHomeSelectionText = null;
        _lGuiPlayerInfoStatusText = null;
        _lGuiAiStatusText = null;
        _lGuiEmpStatusText = null;
        _lGuiAiResponseInput = null;
        _lGuiAiPromptInput = null;
        _lGuiAiLastRequestInput = null;
        _lGuiAiLastResponseInput = null;
        _lGuiAiModelInput = null;
        _lGuiDebugTargetText = null;
    }
}
