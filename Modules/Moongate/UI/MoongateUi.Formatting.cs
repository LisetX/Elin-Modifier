using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private static string FormatMoongateVersion(int version)
    {
        return version > 0 ? version.ToString(CultureInfo.InvariantCulture) : "";
    }
    private string FormatMoongateSource(string sourceKind)
    {
        switch (sourceKind)
        {
            case MoongateModule.SourceOfficial:
                return T("官方", "Official");
            case MoongateModule.SourceEmCloud:
                return T("EM云存储", "EM cloud storage");
            case MoongateModule.SourceEmHistory:
                return T("EM索引历史", "EM index history");
            case MoongateModule.SourceLocal:
                return T("本地缓存", "Local cache");
            default:
                return "-";
        }
    }
}
