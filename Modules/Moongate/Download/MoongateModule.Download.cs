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
    private async UniTask<FileInfo?> DownloadApiMapAsync(MapEntry entry)
    {
        var address = entry.DirectDownloadUrl;
        if (address.Length == 0 && entry.Id.StartsWith("EMU_", StringComparison.OrdinalIgnoreCase))
            address = CloudDownloadApi + Uri.EscapeDataString(entry.Id);
        if (address.Length == 0)
            return null;

        using var response = await _cloudClient.GetAsync(address);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0)
            throw new InvalidDataException(Text("月门地图下载文件大小异常", "Downloaded moongate map size is invalid"));

        Directory.CreateDirectory(CorePath.ZoneSaveUser);
        var safeId = SanitizeMapFileName(entry.Id);
        var destination = Path.Combine(CorePath.ZoneSaveUser, safeId + ".z");
        var temporary = destination + ".download";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return new FileInfo(destination);
    }
}
