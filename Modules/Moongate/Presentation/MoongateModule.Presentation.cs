using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Steamworks;

internal sealed partial class MoongateModule
{
    private static string ShortError(Exception ex)
    {
        var message = (ex.Message ?? ex.GetType().Name).Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length <= 180 ? message : message.Substring(0, 180) + "...";
    }
    private static bool HasAdultTag(string? tags)
    {
        return (tags ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(tag => string.Equals(tag.Trim(), "adult", StringComparison.OrdinalIgnoreCase));
    }
    private string Text(string zh, string en) => _host.TranslateModuleText(zh, en);
    private async UniTask ClearTransientLogAsync(string expectedLog)
    {
        await UniTask.Delay(5000);
        if (_shutdown || !string.Equals(Log, expectedLog, StringComparison.Ordinal))
            return;
        Log = "";
        RefreshPage();
    }
    private void RefreshPage()
    {
        if (_shutdown)
            return;
        _host.RefreshModuleMoongatePage();
    }
    private static string DisplayName(MapEntry entry)
    {
        return string.IsNullOrWhiteSpace(entry.Title)
            ? entry.Id
            : entry.Title + " (" + entry.Id + ")";
    }
}
