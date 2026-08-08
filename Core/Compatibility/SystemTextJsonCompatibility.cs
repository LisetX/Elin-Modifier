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

namespace System.Text.Json
{
    public enum JsonCommentHandling
    {
        Disallow = 0,
        Skip = 1
    }

    public enum JsonValueKind
    {
        Undefined = 0,
        Object = 1,
        Array = 2,
        String = 3,
        Number = 4,
        True = 5,
        False = 6,
        Null = 7
    }

    public struct JsonDocumentOptions
    {
        public bool AllowTrailingCommas { get; set; }
        public JsonCommentHandling CommentHandling { get; set; }
    }

    public sealed class JsonDocument : IDisposable
    {
        private readonly JToken _root;

        private JsonDocument(JToken root)
        {
            _root = root ?? JValue.CreateNull();
        }

        public JsonElement RootElement => new JsonElement(_root);

        public static JsonDocument Parse(string json, JsonDocumentOptions options)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                reader.CloseInput = false;

                var root = JToken.ReadFrom(reader);
                return new JsonDocument(root);
            }
        }

        public static JsonDocument Parse(string json)
        {
            return Parse(json, default(JsonDocumentOptions));
        }

        public void Dispose()
        {
        }
    }

    public struct JsonElement
    {
        private readonly JToken _token;

        internal JsonElement(JToken token)
        {
            _token = token;
        }

        public JsonValueKind ValueKind => GetValueKind(_token);

        public bool TryGetProperty(string name, out JsonElement value)
        {
            var obj = _token as JObject;
            if (obj != null)
            {
                foreach (var prop in obj.Properties())
                {
                    if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    value = new JsonElement(prop.Value);
                    return true;
                }
            }

            value = default(JsonElement);
            return false;
        }

        public IEnumerable<JsonElement> EnumerateArray()
        {
            var array = _token as JArray;
            if (array == null)
                yield break;

            foreach (var item in array)
                yield return new JsonElement(item);
        }

        public IEnumerable<JsonProperty> EnumerateObject()
        {
            var obj = _token as JObject;
            if (obj == null)
                yield break;

            foreach (var prop in obj.Properties())
                yield return new JsonProperty(prop.Name, new JsonElement(prop.Value));
        }

        public string GetString()
        {
            if (_token == null)
                return null;
            if (_token.Type == JTokenType.Null || _token.Type == JTokenType.Undefined)
                return null;
            if (_token.Type == JTokenType.String)
                return _token.Value<string>();
            return Convert.ToString(GetPrimitiveValue(_token), CultureInfo.InvariantCulture);
        }

        public bool TryGetInt32(out int value)
        {
            value = 0;
            if (_token == null)
                return false;

            try
            {
                if (_token.Type == JTokenType.Integer || _token.Type == JTokenType.Float || _token.Type == JTokenType.String)
                {
                    var text = _token.Type == JTokenType.String ? _token.Value<string>() : Convert.ToString(GetPrimitiveValue(_token), CultureInfo.InvariantCulture);
                    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                }
            }
            catch
            {
            }

            return false;
        }

        public override string ToString()
        {
            if (_token == null)
                return "";
            return _token.Type == JTokenType.String
                ? _token.Value<string>()
                : _token.ToString(Formatting.None);
        }

        private static JsonValueKind GetValueKind(JToken token)
        {
            if (token == null)
                return JsonValueKind.Undefined;

            switch (token.Type)
            {
                case JTokenType.Object: return JsonValueKind.Object;
                case JTokenType.Array: return JsonValueKind.Array;
                case JTokenType.String: return JsonValueKind.String;
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Date:
                case JTokenType.TimeSpan:
                    return JsonValueKind.Number;
                case JTokenType.Boolean:
                    return token.Value<bool>() ? JsonValueKind.True : JsonValueKind.False;
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return JsonValueKind.Null;
                default:
                    return JsonValueKind.Undefined;
            }
        }

        private static object GetPrimitiveValue(JToken token)
        {
            var value = token as JValue;
            return value != null ? value.Value : token.ToString(Formatting.None);
        }
    }

    public struct JsonProperty
    {
        public string Name { get; }
        public JsonElement Value { get; }

        public JsonProperty(string name, JsonElement value)
        {
            Name = name ?? "";
            Value = value;
        }
    }
}
