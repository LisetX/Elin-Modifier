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
    internal void UploadCurrentMap()
    {
        if (_shutdown || _uploading)
            return;
        if (!ElinModifierPlugin.HasModuleCharacterData() || GameAccess.World.CurrentZone == null || GameAccess.World.CurrentMap == null || GameAccess.Characters.PlayerCharacter == null)
        {
            Log = Text("请先进入要上传的地图", "Enter the map to upload first");
            RefreshPage();
            return;
        }

        UploadCurrentMapAsync().Forget();
    }
    private async UniTask UploadCurrentMapAsync()
    {
        _uploading = true;
        Log = Text("正在导出并上传当前地图...", "Exporting and uploading the current map...");
        RefreshPage();

        string temporaryPath = "";
        try
        {
            var zone = GameAccess.World.CurrentZone;
            var map = GameAccess.World.CurrentMap;
            var pc = GameAccess.Characters.PlayerCharacter;
            if (zone == null || map == null || pc == null)
                throw new InvalidOperationException(Text("当前地图数据不可用", "Current map data is unavailable"));
            if (zone.subset != null)
                throw new InvalidOperationException(Text("当前地图子区块无法上传", "The current map subset cannot be uploaded"));

            var ownerId = GetCurrentSteamId();
            if (ownerId.Length == 0)
                throw new InvalidOperationException(Text("无法获取SteamID64", "Unable to read SteamID64"));

            var mapKey = BuildLocalMapKey(zone);
            if (mapKey.Length == 0)
                throw new InvalidOperationException(Text("无法生成当前地图的稳定本地ID", "Unable to create a stable local ID for the current map"));

            var updateKeyStorageId = ComputeSha256(ownerId + "\u001f" + mapKey);
            if (!_uploadUpdateKeys.TryGetValue(updateKeyStorageId, out var updateKey) || updateKey.Length < 8)
            {
                updateKey = CreateUpdateKey();
                _uploadUpdateKeys[updateKeyStorageId] = updateKey;
                _host.SaveConfigFromModule(false, false);
            }

            temporaryPath = Path.Combine(
                Path.GetTempPath(),
                "ElinModifier_Moongate_" + Guid.NewGuid().ToString("N") + ".z");
            zone.Export(temporaryPath, null, true);
            MoongateContainerTransfer.AttachToExport(temporaryPath, map);

            var file = new FileInfo(temporaryPath);
            if (!file.Exists || file.Length <= 0)
                throw new IOException(Text("当前地图导出失败", "Failed to export the current map"));
            MapMetaData? metadata = null;
            try
            {
                metadata = Map.GetMetaData(temporaryPath);
            }
            catch
            {
            }

            var author = (pc.NameBraced ?? "").Trim();
            var title = (metadata?.name ?? zone.Name ?? "").Trim();
            if (author.Length == 0)
                author = ownerId;
            if (title.Length == 0)
                title = mapKey;
            var language = NormalizeLanguage(Lang.langCode);
            var category = zone is Zone_Tent ? "Tent" : "Home";
            var tags = (map.exportSetting?.tag ?? "").Trim();
            var version = metadata?.version ?? zone.version;

            using var form = new MultipartFormDataContent();
            AddFormText(form, "owner_id", ownerId);
            AddFormText(form, "map_key", mapKey);
            AddFormText(form, "update_key", updateKey);
            AddFormText(form, "author", author);
            AddFormText(form, "title", title);
            AddFormText(form, "language", language);
            AddFormText(form, "category", category);
            AddFormText(form, "tags", tags);
            AddFormText(form, "version", version.ToString(CultureInfo.InvariantCulture));

            using var stream = File.OpenRead(temporaryPath);
            using var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", Path.GetFileName(temporaryPath));

            using var response = await _cloudClient.PostAsync(CloudUploadApi, form);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(BuildUploadError(response.StatusCode, responseText));

            var root = JObject.Parse(responseText);
            var action = root.Value<string>("action") ?? "uploaded";
            var uploadedId = root["map"]?.Value<string>("id") ?? "";
            Log = string.Equals(action, "updated", StringComparison.OrdinalIgnoreCase)
                ? Text("地图已更新至 Elin Modifier 服务器: ", "Map updated on the Elin Modifier server: ") + uploadedId
                : Text("地图已上传至 Elin Modifier 服务器: ", "Map uploaded to the Elin Modifier server: ") + uploadedId;
            RestartIndexLoad();
        }
        catch (Exception ex)
        {
            Log = Text("上传地图失败: ", "Map upload failed: ") + ShortError(ex);
        }
        finally
        {
            _uploading = false;
            if (temporaryPath.Length > 0)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
            RefreshPage();
        }
    }
    private static void AddFormText(MultipartFormDataContent form, string name, string value)
    {
        form.Add(new StringContent(value ?? "", Encoding.UTF8), name);
    }
    private string BuildUploadError(HttpStatusCode statusCode, string responseText)
    {
        try
        {
            var root = JObject.Parse(responseText ?? "");
            var message = root.Value<string>("message");
            if (!string.IsNullOrWhiteSpace(message))
                return ((int)statusCode).ToString(CultureInfo.InvariantCulture) + " " + message;
        }
        catch (JsonException)
        {
        }
        return ((int)statusCode).ToString(CultureInfo.InvariantCulture) + " " + statusCode;
    }
    private static string GetCurrentSteamId()
    {
        try
        {
            var value = SteamUser.GetSteamID().m_SteamID;
            return value == 0UL ? "" : value.ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }
    private static string BuildLocalMapKey(Zone zone)
    {
        var savePath = (GameIO.pathCurrentSave ?? "").TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var saveId = Path.GetFileName(savePath) ?? "";
        return saveId.Length == 0 || zone.uid <= 0
            ? ""
            : saveId + ":" + zone.uid.ToString(CultureInfo.InvariantCulture);
    }
    private static string CreateUpdateKey()
    {
        var bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create())
            random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
        var builder = new StringBuilder(bytes.Length * 2);
        for (var i = 0; i < bytes.Length; i++)
            builder.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
