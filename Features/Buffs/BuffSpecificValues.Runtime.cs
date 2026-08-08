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

public sealed partial class ElinModifierPlugin
{
    private static string BuildBuffConditionSpecificInfo(Condition condition)
    {
        if (condition == null)
            return "";

        var details = new List<string>(2);
        try
        {
            if (condition is ConBuffStats buffStats)
            {
                var value = buffStats.CalcValue();
                if (buffStats.IsDebuff)
                    value = -value;
                details.Add("L:" + FormatCompactCount(value));
            }
            else if (condition.power != 0)
            {
                details.Add("L:" + FormatCompactCount(condition.power));
            }
        }
        catch { }

        try
        {
            if (condition.HasDuration)
            {
                var duration = condition.TextDuration;
                if (!string.IsNullOrWhiteSpace(duration))
                    details.Add("T:" + FormatCompactNumericText(duration));
            }
        }
        catch { }

        return details.Count == 0 ? "" : string.Join(" ", details.ToArray());
    }
    private static int GetBuffSpecificInfoFontSize(bool iconStyle)
    {
        var offset = iconStyle
            ? Instance?._showBuffSpecificValuesIconFontSizeOffset ?? 0
            : Instance?._showBuffSpecificValuesTextFontSizeOffset ?? 0;
        return Clamp(14 + offset, 6, 22);
    }
    private static string ApplyBuffIconSpecificInfoFontSize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";
        return "<size=" + GetBuffSpecificInfoFontSize(iconStyle: true).ToString(CultureInfo.InvariantCulture) + ">" + text + "</size>";
    }
    private const string BuffSpecificInfoTextObjectName = "ElinModifier_BuffSpecificInfo";
    private static UnityEngine.UI.Text? GetBuffIconSpecificInfoStyleSource()
    {
        try
        {
            return WidgetStats.Instance?.moldBuff?.textDuration;
        }
        catch
        {
            return null;
        }
    }
    private static UnityEngine.UI.Text? GetBuffSpecificInfoText(BaseNotification notification, bool create)
    {
        var item = notification?.item;
        var mainText = item?.button?.mainText;
        if (item == null || mainText == null)
            return null;

        try
        {
            var child = item.transform.Find(BuffSpecificInfoTextObjectName);
            var text = child == null ? null : child.GetComponent<UnityEngine.UI.Text>();
            if (text != null || !create)
                return text;

            GameObject go;
            var iconStyleSource = GetBuffIconSpecificInfoStyleSource();
            if (iconStyleSource != null)
            {
                go = UnityEngine.Object.Instantiate(iconStyleSource.gameObject, item.transform);
                go.name = BuffSpecificInfoTextObjectName;
            }
            else
            {
                go = new GameObject(
                    BuffSpecificInfoTextObjectName,
                    typeof(RectTransform),
                    typeof(UnityEngine.UI.Text),
                    typeof(UnityEngine.UI.LayoutElement));
                go.transform.SetParent(item.transform, false);
            }
            go.transform.SetAsLastSibling();

            text = go.GetComponent<UnityEngine.UI.Text>();
            if (text == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            var layout = go.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout != null)
                layout.ignoreLayout = true;

            text.raycastTarget = false;
            text.supportRichText = true;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
            text.verticalOverflow = UnityEngine.VerticalWrapMode.Overflow;
            CopyBuffSpecificInfoTextStyle(iconStyleSource ?? mainText, text);

            var rect = text.rectTransform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 220f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(30f, mainText.rectTransform.rect.height));
            return text;
        }
        catch
        {
            return null;
        }
    }
    private static void CopyBuffSpecificInfoTextStyle(UnityEngine.UI.Text source, UnityEngine.UI.Text target)
    {
        if (source == null || target == null)
            return;

        try
        {
            target.font = source.font;
            target.fontStyle = source.fontStyle;
            target.lineSpacing = source.lineSpacing;
            target.color = Color.white;
            target.material = source.material;
            target.maskable = source.maskable;
            target.fontSize = GetBuffSpecificInfoFontSize(iconStyle: false);
        }
        catch { }
    }
    private static void SetBuffTextSpecificInfo(BaseNotification notification, string details)
    {
        if (notification == null)
            return;

        var enabled = Instance?._showBuffSpecificValues == true && !string.IsNullOrEmpty(details);
        var text = GetBuffSpecificInfoText(notification, enabled);
        if (text == null)
            return;

        if (!enabled)
        {
            text.gameObject.SetActive(false);
            return;
        }

        var mainText = notification.item?.button?.mainText;
        var styleSource = GetBuffIconSpecificInfoStyleSource() ?? mainText;
        if (styleSource != null)
            CopyBuffSpecificInfoTextStyle(styleSource, text);
        text.text = details;
        text.gameObject.SetActive(true);
        PositionBuffTextSpecificInfo(notification);
    }
    private static void PositionBuffTextSpecificInfo(BaseNotification notification)
    {
        var instance = Instance;
        if (instance == null || !instance._showBuffSpecificValues || notification?.item?.button == null)
            return;

        try
        {
            var text = GetBuffSpecificInfoText(notification, false);
            if (text == null || !text.gameObject.activeSelf)
                return;

            var buttonRect = notification.item.button.transform as RectTransform;
            var mainText = notification.item.button.mainText;
            var mainRect = mainText?.rectTransform;
            var anchorRect = buttonRect ?? mainRect;
            if (anchorRect == null)
                return;

            var styleSource = GetBuffIconSpecificInfoStyleSource() ?? mainText;
            CopyBuffSpecificInfoTextStyle(styleSource, text);
            var layout = text.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout == null)
                layout = text.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.ignoreLayout = true;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
            text.verticalOverflow = UnityEngine.VerticalWrapMode.Overflow;
            var textRect = text.rectTransform;
            textRect.pivot = new Vector2(0f, 0.5f);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 220f);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(30f, anchorRect.rect.height));

            var rightCenter = anchorRect.TransformPoint(new Vector3(anchorRect.rect.xMax + 8f, anchorRect.rect.center.y, 0f));
            var current = textRect.position;
            textRect.position = new Vector3(rightCenter.x, rightCenter.y, current.z);
            text.transform.SetAsLastSibling();
        }
        catch { }
    }
    private static void AppendBuffConditionSpecificValues(NotificationCondition notification)
    {
        if (notification == null)
            return;

        var details = Instance?._showBuffSpecificValues == true
            ? BuildBuffConditionSpecificInfo(notification.condition)
            : "";
        SetBuffTextSpecificInfo(notification, details);
    }
    private static void ApplyBuffIconSpecificInfo(NotificationBuff notification)
    {
        var instance = Instance;
        if (instance == null || !instance._showBuffSpecificValues || notification == null || notification.item?.textDuration == null)
            return;
        var condition = notification.condition;
        if (condition == null)
            return;

        var details = BuildBuffConditionSpecificInfo(condition);
        notification.item.textDuration.SetText(ApplyBuffIconSpecificInfoFontSize(details));
        notification.item.textDuration.SetActive(!string.IsNullOrEmpty(details));
        PositionBuffIconSpecificInfo(notification);
    }
    private static void PositionBuffIconSpecificInfo(NotificationBuff notification)
    {
        var instance = Instance;
        if (instance == null || !instance._showBuffSpecificValues || notification?.item?.textDuration == null || notification.item.button?.icon == null)
            return;

        try
        {
            var text = notification.item.textDuration;
            var textRect = text.rectTransform;
            var iconRect = notification.item.button.icon.rectTransform;
            if (textRect == null || iconRect == null)
                return;

            var layout = text.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout == null)
                layout = text.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.ignoreLayout = true;

            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = UnityEngine.HorizontalWrapMode.Overflow;
            text.verticalOverflow = UnityEngine.VerticalWrapMode.Overflow;
            textRect.pivot = new Vector2(0f, 0.5f);
            if (textRect.rect.width < 220f)
                textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 220f);

            var iconRightCenter = iconRect.TransformPoint(new Vector3(iconRect.rect.xMax + 8f, iconRect.rect.center.y, 0f));
            var current = textRect.position;
            textRect.position = new Vector3(iconRightCenter.x, iconRightCenter.y, current.z);
        }
        catch { }
    }
    private static void AppendBuffStatsSpecificValue(NotificationStats notification)
    {
        if (notification == null)
            return;

        var details = "";
        try
        {
            if (Instance?._showBuffSpecificValues == true)
            {
                var stats = notification.stats?.Invoke();
                if (stats != null)
                    details = "L:" + FormatCompactCount(stats.GetValue());
            }
        }
        catch { }
        SetBuffTextSpecificInfo(notification, details);
    }
}
