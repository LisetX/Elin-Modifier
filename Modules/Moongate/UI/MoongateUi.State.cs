using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private const int MoongateSearchRowsPerPage = 12;
    private const int MoongateLocalRowsPerPage = 6;
    private ScrollRect? _lGuiMoongateScroll;
    internal void RefreshModuleMoongatePage()
    {
        if (_lGuiPage == LGuiPage.Moongate && IsLGuiVisible())
            SwitchLGuiPage(LGuiPage.Moongate);
        else
            NotifyLGuiDataDirty();
    }
    internal void ShutdownModuleMoongate() => _modules.Moongate.Shutdown();
}
