using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed class ConfigurationStorageModule
{
    internal string ReadAllText(string path, Encoding encoding)
    {
        return File.ReadAllText(path, encoding);
    }

    internal void WriteAllTextAtomic(string path, string content, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, content, encoding);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temporaryPath, path, null);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temporaryPath, path, true);
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    File.Copy(temporaryPath, path, true);
                    File.Delete(temporaryPath);
                }
            }
            else
                File.Move(temporaryPath, path);
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
            throw;
        }
    }
}

internal sealed class ConfigurationValueDocument
{
    private static readonly ConfigurationValueDocument Empty =
        new ConfigurationValueDocument(new JObject());

    [ThreadStatic] private static string? _cachedJson;
    [ThreadStatic] private static ConfigurationValueDocument? _cachedDocument;

    private readonly JObject _root;

    private ConfigurationValueDocument(JObject root)
    {
        _root = root;
    }

    internal static ConfigurationValueDocument For(string? json)
    {
        json ??= "";
        if (ReferenceEquals(json, _cachedJson) && _cachedDocument != null)
            return _cachedDocument;

        ConfigurationValueDocument document;
        try
        {
            document = new ConfigurationValueDocument(JObject.Parse(json));
        }
        catch (JsonException)
        {
            document = Empty;
        }

        _cachedJson = json;
        _cachedDocument = document;
        return document;
    }

    internal bool Contains(string name)
    {
        JToken? token;
        return !string.IsNullOrEmpty(name) &&
               _root.TryGetValue(name, StringComparison.Ordinal, out token);
    }

    internal string GetString(string name, string fallback)
    {
        var token = GetToken(name);
        if (token == null || token.Type == JTokenType.Null ||
            token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            return fallback;
        return token.Type == JTokenType.String
            ? token.Value<string>() ?? fallback
            : token.ToString(Formatting.None);
    }

    internal string GetScalar(string name)
    {
        var token = GetToken(name);
        if (token == null || token.Type == JTokenType.Null ||
            token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            return "";
        return token.Type == JTokenType.String
            ? token.Value<string>() ?? ""
            : token.ToString(Formatting.None);
    }

    internal string GetRawJson(string name)
    {
        var token = GetToken(name);
        return token == null ? "" : token.ToString(Formatting.None);
    }

    internal int GetInt(string name, int fallback)
    {
        var value = GetScalar(name);
        int parsed;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : fallback;
    }

    internal float GetFloat(string name, float fallback)
    {
        var value = GetScalar(name);
        float parsed;
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : fallback;
    }

    internal bool GetBool(string name, bool fallback)
    {
        var value = GetScalar(name);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        switch (value.Trim().ToLowerInvariant())
        {
            case "true":
            case "1":
            case "on":
            case "yes":
            case "enable":
            case "enabled":
                return true;
            case "false":
            case "0":
            case "off":
            case "no":
            case "disable":
            case "disabled":
                return false;
            default:
                return fallback;
        }
    }

    private JToken? GetToken(string name)
    {
        JToken? token;
        return !string.IsNullOrEmpty(name) &&
               _root.TryGetValue(name, StringComparison.Ordinal, out token)
            ? token
            : null;
    }
}
