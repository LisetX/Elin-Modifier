using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class AutomationModule
{
    private void SetAutomationLog(string value)
    {
        _automationLog = value ?? "";
        if (_automationStatusText != null)
            _automationStatusText.text = GetAutomationStatusLine();
        _lGuiDataDirty = true;
    }
    private string GetAutomationStatusLine()
    {
        var state = _automationRunning
            ? AutomationText("运行中", "Running", "実行中", "Выполняется")
            : AutomationText("已停止", "Stopped", "停止中", "Остановлено");
        return AutomationText("状态: ", "Status: ", "状態: ", "Статус: ") + state + "  |  " + _automationLog;
    }
    internal void BuildAutomationPage()
    {
        EnsureAutomationProfiles();
        var profile = GetCurrentAutomationProfile();
        var toolbar = CreateLGuiRect(_lGuiPageHost!, "AutomationToolbar");
        AnchorLGuiTop(toolbar, 0f, 216f, 0f, 0f);

        var profileLabel = CreateLGuiText(toolbar, "ProfileLabel", AutomationText("执行配置", "Profile", "実行設定", "Профиль"), 17, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(profileLabel.rectTransform, 0f, 4f, 90f, 44f);
        CreateLGuiButton(toolbar, "PrevProfile", "◀", 92f, 4f, 46f, 44f, () => CycleAutomationProfile(-1));
        var profileCounter = CreateLGuiText(toolbar, "ProfileCounter",
            (_automationProfileIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + _automationProfiles.Count.ToString(CultureInfo.InvariantCulture),
            16, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(profileCounter.rectTransform, 142f, 4f, 68f, 44f);
        CreateLGuiButton(toolbar, "NextProfile", "▶", 214f, 4f, 46f, 44f, () => CycleAutomationProfile(1));
        var name = CreateLGuiInput(toolbar, "ProfileName", AutomationText("配置名称", "Profile name", "設定名", "Имя профиля"), 272f, 4f, 300f, 44f);
        name.text = profile.Name;
        name.onValueChanged.AddListener(value => profile.Name = string.IsNullOrWhiteSpace(value) ? profile.Name : value.Trim());
        CreateLGuiButton(toolbar, "AddProfile", AutomationText("新增配置", "Add profile", "設定追加", "Добавить"), 584f, 4f, 118f, 44f, AddAutomationProfile);
        CreateLGuiButton(toolbar, "CopyProfile", AutomationText("复制配置", "Copy profile", "設定コピー", "Копировать"), 712f, 4f, 118f, 44f, CopyAutomationProfile);
        CreateLGuiButton(toolbar, "DeleteProfile", AutomationText("删除配置", "Delete profile", "設定削除", "Удалить"), 840f, 4f, 118f, 44f, DeleteAutomationProfile);

        var loopToggle = CreateLGuiToggle(toolbar, "Loop", 0f, 58f, 154f, 44f, out var loopLabel);
        loopLabel.text = AutomationText("循环执行", "Loop", "繰り返し実行", "Цикл");
        loopToggle.isOn = profile.Loop;
        loopToggle.onValueChanged.AddListener(value => profile.Loop = value);

        CreateAutomationKeyControl(toolbar, AutomationText("运行快捷键", "Run key", "実行キー", "Клавиша запуска"), 170f, _automationRunKey, key => _automationRunKey = key, "RunKey");
        CreateAutomationKeyControl(toolbar, AutomationText("中止快捷键", "Stop key", "停止キー", "Клавиша остановки"), 458f, _automationStopKey, key => _automationStopKey = key, "StopKey");
        CreateLGuiButton(toolbar, "Run", AutomationText("运行", "Run", "実行", "Запуск"), 748f, 58f, 100f, 44f, StartAutomation);
        CreateLGuiButton(toolbar, "Stop", AutomationText("中止", "Stop", "停止", "Стоп"), 858f, 58f, 100f, 44f, () => StopAutomation(true, true));
        CreateLGuiButton(toolbar, "AddAction", AutomationText("新增执行项", "Add action", "実行項目追加", "Добавить действие"), 968f, 58f, 146f, 44f, AddAutomationAction);
        CreateLGuiButton(toolbar, "Save", AutomationText("保存配置", "Save config", "設定保存", "Сохранить"), 968f, 4f, 130f, 44f, () => SaveConfig(true));

        var ignoreWeightToggle = CreateLGuiToggle(toolbar, "IgnoreWeightDuringExecution", 0f, 112f, 300f, 44f, out var ignoreWeightLabel);
        ignoreWeightLabel.text = AutomationText("执行期间无视负重", "Ignore weight during execution", "実行中は重量を無視", "Игнорировать вес при выполнении");
        ignoreWeightToggle.isOn = _automationIgnoreWeightDuringExecution;
        ignoreWeightToggle.onValueChanged.AddListener(value =>
        {
            _automationIgnoreWeightDuringExecution = value;
            if (_automationRunning)
                MaintainAutomationFeatureOverrides();
        });

        var needsDetectionToggle = CreateLGuiToggle(toolbar, "NeedsDetectionDuringExecution", 320f, 112f, 300f, 44f, out var needsDetectionLabel);
        needsDetectionLabel.text = AutomationText("执行期间需求检测", "Needs detection during execution", "実行中の必要状態を検出", "Проверять потребности при выполнении");
        needsDetectionToggle.isOn = _automationNeedsDetectionDuringExecution;
        needsDetectionToggle.onValueChanged.AddListener(value => _automationNeedsDetectionDuringExecution = value);

        _automationStatusText = CreateLGuiText(toolbar, "AutomationStatus", GetAutomationStatusLine(), 15, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(_automationStatusText.rectTransform, 0f, 164f, 1450f, 42f);

        var scroll = CreateLGuiScroll(_lGuiPageHost!, "AutomationActions", 224f);
        _automationActionsScroll = scroll;
        var content = scroll.content!;
        var rowStep = 112f;
        content.sizeDelta = new Vector2(0f, Math.Max(20f, profile.Actions.Count * rowStep + 20f));
        if (profile.Actions.Count == 0)
        {
            var empty = CreateLGuiText(content, "Empty", AutomationText("当前没有执行项，点击“新增执行项”开始配置。", "No actions. Click Add action to begin.", "実行項目がありません。「実行項目追加」をクリックしてください。", "Нет действий. Нажмите «Добавить действие»."), 18, TextAnchor.MiddleCenter, FontStyle.Normal);
            PlaceLGuiRect(empty.rectTransform, 0f, 20f, 1450f, 70f);
        }
        else
        {
            for (var i = 0; i < profile.Actions.Count; i++)
                BuildAutomationActionRow(content, profile, profile.Actions[i], i, 8f + i * rowStep);
        }
    }
    private void CreateAutomationKeyControl(Transform parent, string label, float x, KeyCode value, Action<KeyCode> setter, string name)
    {
        var title = CreateLGuiText(parent, name + "Label", label, 14, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(title.rectTransform, x, 58f, 98f, 44f);
        CreateLGuiButton(parent, name + "Prev", "◀", x + 100f, 58f, 38f, 44f, () => CycleAutomationKey(value, -1, setter));
        CreateLGuiButton(parent, name + "Value", GetKeyLabel(value), x + 142f, 58f, 96f, 44f, () => CycleAutomationKey(value, 1, setter));
        CreateLGuiButton(parent, name + "Next", "▶", x + 242f, 58f, 38f, 44f, () => CycleAutomationKey(value, 1, setter));
    }
    private void CycleAutomationKey(KeyCode current, int direction, Action<KeyCode> setter)
    {
        setter(GetAdjacentKey(current, direction));
        RebuildAutomationPage();
    }
    private void BuildAutomationActionRow(RectTransform content, AutomationProfile profile, AutomationActionConfig action, int index, float y)
    {
        var row = CreateLGuiRect(content, "AutomationAction" + index.ToString(CultureInfo.InvariantCulture));
        PlaceLGuiRect(row, 0f, y, 1450f, 102f);
        var background = row.gameObject.AddComponent<Image>();
        background.color = GetLGuiRowColor(index, false);
        background.raycastTarget = false;
        RegisterLGuiRoundedImage(background);

        var enabled = CreateLGuiToggle(row, "Enabled", 10f, 28f, 56f, 44f, out var enabledLabel);
        enabledLabel.text = "";
        enabled.isOn = action.Enabled;
        enabled.onValueChanged.AddListener(value => action.Enabled = value);

        var order = CreateLGuiText(row, "Order", (index + 1).ToString(CultureInfo.InvariantCulture), 18, TextAnchor.MiddleCenter, FontStyle.Normal);
        PlaceLGuiRect(order.rectTransform, 54f, 28f, 38f, 44f);
        var actionTypeIndex = Array.IndexOf(AutomationActionTypes, NormalizeAutomationActionType(action.Type));
        var actionTypeLabels = AutomationActionTypes.Select(GetAutomationActionLabel).ToArray();
        CreateAutomationDropdown(row, "Type", actionTypeLabels, actionTypeIndex, 96f, 28f, 260f, 44f,
            selected => SetAutomationActionType(action, selected));

        BuildAutomationActionParameters(row, action);

        if (NormalizeAutomationActionType(action.Type) == AutomationTypeWait)
        {
            var delayLabel = CreateLGuiText(row, "DelayLabel", AutomationText("延时(秒)", "Delay (sec)", "遅延(秒)", "Задержка (с)"), 14, TextAnchor.MiddleLeft, FontStyle.Normal);
            PlaceLGuiRect(delayLabel.rectTransform, 366f, 4f, 150f, 24f);
            var delay = CreateLGuiInput(row, "Delay", "0", 366f, 32f, 150f, 42f);
            delay.contentType = InputField.ContentType.DecimalNumber;
            delay.text = Mathf.Clamp(action.DelaySeconds, 0f, 3600f).ToString("0.###", CultureInfo.InvariantCulture);
            delay.onEndEdit.AddListener(value =>
            {
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    action.DelaySeconds = Mathf.Clamp(parsed, 0f, 3600f);
                RebuildAutomationPage();
            });
        }

        CreateLGuiButton(row, "Up", "▲", 1250f, 8f, 66f, 40f, () => MoveAutomationAction(profile, index, -1));
        CreateLGuiButton(row, "Down", "▼", 1250f, 54f, 66f, 40f, () => MoveAutomationAction(profile, index, 1));
        CreateLGuiButton(row, "Delete", AutomationText("删除", "Delete", "削除", "Удалить"), 1330f, 31f, 104f, 44f, () => DeleteAutomationAction(profile, index));
    }
    private void BuildAutomationActionParameters(RectTransform row, AutomationActionConfig action)
    {
        var type = NormalizeAutomationActionType(action.Type);
        switch (type)
        {
            case AutomationTypeAutoMine:
            case AutomationTypeAutoChop:
            case AutomationTypeAutoHarvest:
            case AutomationTypeAutoFertilize:
            case AutomationTypeSearchContainers:
            case AutomationTypeAutoInteract:
            case AutomationTypeAutoKill:
            case AutomationTypeSaveGame:
            case AutomationTypeLoadGame:
                break;
            case AutomationTypeMoveTo:
                CreateAutomationParameterInput(row, "X", action.Param1, 366f, 150f, value => action.Param1 = value, true);
                CreateAutomationParameterInput(row, "Z", action.Param2, 530f, 150f, value => action.Param2 = value, true);
                break;
            case AutomationTypeUseAbility:
                CreateAutomationParameterInput(row, AutomationText("能力/咒语", "Ability / spell", "能力/呪文", "Способность / заклинание"), action.Param1, 366f, 280f, value => action.Param1 = value, false);
                var targetLabel = CreateLGuiText(row, "TargetLabel", AutomationText("目标", "Target", "対象", "Цель"), 14, TextAnchor.MiddleLeft, FontStyle.Normal);
                PlaceLGuiRect(targetLabel.rectTransform, 660f, 4f, 100f, 24f);
                CreateLGuiButton(row, "Target", GetAutomationAbilityTargetLabel(action.Param2), 660f, 32f, 160f, 42f, () => CycleAutomationAbilityTarget(action));
                CreateAutomationParameterInput(row, AutomationText("目标半径", "Target radius", "対象半径", "Радиус цели"), string.IsNullOrWhiteSpace(action.Param3) ? "30" : action.Param3, 832f, 150f, value => action.Param3 = value, true);
                break;
            case AutomationTypeNextFloor:
                break;
            case AutomationTypePickupByValue:
                CreateAutomationParameterInput(row, AutomationText("最低价值", "Minimum value", "最低価値", "Мин. стоимость"), action.Param1, 366f, 140f, value => action.Param1 = value, true);
                CreateAutomationParameterInput(row, AutomationText("优先拾取物品ID", "Preferred item IDs", "優先取得アイテムID", "ID приоритетных предметов"), action.Param4, 520f, 350f, value => action.Param4 = value, false);
                CreateAutomationParameterInput(row, AutomationText("搜索半径", "Search radius", "検索半径", "Радиус поиска"), action.Param2, 884f, 130f, value => action.Param2 = value, true);
                var replaceToggle = CreateLGuiToggle(row, "ReplaceLowestValue", 1028f, 32f, 210f, 42f, out var replaceLabel);
                replaceLabel.text = AutomationText("自动高价替换", "Automatic higher-value replacement", "高価値アイテム自動置換", "Автозамена на более ценный предмет");
                replaceToggle.isOn = IsAutomationPickupReplacementEnabled(action);
                replaceToggle.onValueChanged.AddListener(value => action.Param3 = value ? "true" : "false");
                break;
            case AutomationTypeWait:
                break;
        }
    }
    private void CreateAutomationParameterInput(RectTransform row, string label, string value, float x, float width, Action<string> setter, bool integer)
    {
        var title = CreateLGuiText(row, "ParamLabel", label, 14, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(title.rectTransform, x, 4f, width, 24f);
        var input = CreateLGuiInput(row, "ParamInput", "", x, 32f, width, 42f);
        if (integer) input.contentType = InputField.ContentType.IntegerNumber;
        input.text = value ?? "";
        input.onValueChanged.AddListener(next => setter(next ?? ""));
    }
    internal Dropdown CreateAutomationDropdown(Transform parent, string name, IReadOnlyList<string> options, int selectedIndex,
        float x, float y, float width, float height, Action<int> changed)
    {
        var rect = CreateLGuiRect(parent, name);
        PlaceLGuiRect(rect, x, y, width, height);
        var background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.18f, 0.2f, 0.23f, 1f);
        RegisterLGuiRoundedImage(background);

        var dropdown = rect.gameObject.AddComponent<AutomationDropdown>();
        dropdown.targetGraphic = background;

        var caption = CreateLGuiText(rect, "Label", "", 17, TextAnchor.MiddleCenter, FontStyle.Normal);
        StretchLGuiRect(caption.rectTransform, 8f, 2f, 34f, 2f);
        caption.horizontalOverflow = HorizontalWrapMode.Wrap;
        dropdown.captionText = caption;

        var arrow = CreateLGuiText(rect, "Arrow", "▼", 15, TextAnchor.MiddleCenter, FontStyle.Normal);
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.offsetMin = new Vector2(-30f, 2f);
        arrow.rectTransform.offsetMax = new Vector2(-4f, -2f);

        const float dropdownItemHeight = 42f;
        const int maximumVisibleItems = 5;
        var visibleItemCount = Math.Min(maximumVisibleItems, Math.Max(1, options.Count));
        var template = CreateLGuiRect(rect, "Template");
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = new Vector2(0f, -2f);
        template.sizeDelta = new Vector2(20f, visibleItemCount * dropdownItemHeight + 4f);
        var templateBackground = template.gameObject.AddComponent<Image>();
        templateBackground.color = new Color(0.055f, 0.062f, 0.073f, 1f);
        RegisterLGuiRoundedImage(templateBackground);

        var scroll = template.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = dropdownItemHeight;

        var viewport = CreateLGuiRect(template, "Viewport");
        StretchLGuiRect(viewport, 2f, 2f, 22f, 2f);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateLGuiRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, dropdownItemHeight);

        var item = CreateLGuiRect(content, "Item");
        item.anchorMin = new Vector2(0f, 1f);
        item.anchorMax = new Vector2(1f, 1f);
        item.pivot = new Vector2(0.5f, 1f);
        item.anchoredPosition = Vector2.zero;
        item.sizeDelta = new Vector2(0f, dropdownItemHeight);
        var itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = new Color(0.11f, 0.125f, 0.145f, 1f);
        RegisterLGuiRoundedImage(itemBackground);
        var itemToggle = item.gameObject.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;

        var checkmark = CreateLGuiImage(item, "Item Checkmark", 6f, 8f, 24f, 24f);
        checkmark.color = new Color(0.25f, 0.84f, 0.48f, 1f);
        RegisterLGuiRoundedImage(checkmark);
        itemToggle.graphic = checkmark;

        var itemLabel = CreateLGuiText(item, "Item Label", "", 16, TextAnchor.MiddleLeft, FontStyle.Normal);
        StretchLGuiRect(itemLabel.rectTransform, 38f, 2f, 8f, 2f);

        var scrollbarRect = CreateLGuiRect(template, "Scrollbar");
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-18f, 3f);
        scrollbarRect.offsetMax = new Vector2(-3f, -3f);
        var scrollbarImage = scrollbarRect.gameObject.AddComponent<Image>();
        scrollbarImage.color = new Color(0.08f, 0.085f, 0.095f, 1f);
        RegisterLGuiRoundedImage(scrollbarImage, true);
        var scrollbar = scrollbarRect.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        var slidingArea = CreateLGuiRect(scrollbarRect, "Sliding Area");
        StretchLGuiRect(slidingArea, 2f, 2f, 2f, 2f);
        var handle = CreateLGuiRect(slidingArea, "Handle");
        StretchLGuiRect(handle, 0f, 0f, 0f, 0f);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.35f, 0.38f, 0.43f, 1f);
        RegisterLGuiRoundedImage(handleImage, true);
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;

        scroll.viewport = viewport;
        scroll.content = content;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        dropdown.template = template;
        dropdown.itemText = itemLabel;
        dropdown.itemImage = null;
        dropdown.options.Clear();
        for (var i = 0; i < options.Count; i++)
            dropdown.options.Add(new Dropdown.OptionData(options[i] ?? ""));
        dropdown.value = Clamp(selectedIndex, 0, Math.Max(0, options.Count - 1));
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(value => changed(value));
        template.gameObject.SetActive(false);
        return dropdown;
    }
    private void RebuildAutomationPage()
    {
        if (_host.IsModuleAutomationPageActive() && IsLGuiInitialized())
            _host.RefreshModuleAutomationPage();
    }
    private void RebuildAutomationPagePreservingScroll()
    {
        var normalizedPosition = _automationActionsScroll == null
            ? 1f
            : _automationActionsScroll.verticalNormalizedPosition;

        RebuildAutomationPage();

        if (_automationActionsScroll == null)
            return;

        Canvas.ForceUpdateCanvases();
        _automationActionsScroll.StopMovement();
        _automationActionsScroll.verticalNormalizedPosition = Mathf.Clamp01(normalizedPosition);
    }
    private void CycleAutomationProfile(int direction)
    {
        EnsureAutomationProfiles();
        if (_automationRunning)
            StopAutomation(true, true);
        _automationProfileIndex = (_automationProfileIndex + direction) % _automationProfiles.Count;
        if (_automationProfileIndex < 0) _automationProfileIndex += _automationProfiles.Count;
        RebuildAutomationPage();
    }
    private void AddAutomationProfile()
    {
        if (_automationProfiles.Count >= 64) return;
        _automationProfiles.Add(new AutomationProfile
        {
            Name = AutomationText("配置 ", "Profile ", "設定 ", "Профиль ") + (_automationProfiles.Count + 1).ToString(CultureInfo.InvariantCulture),
            Loop = true
        });
        _automationProfileIndex = _automationProfiles.Count - 1;
        RebuildAutomationPage();
    }
    private void CopyAutomationProfile()
    {
        if (_automationProfiles.Count >= 64) return;
        var source = GetCurrentAutomationProfile();
        var copyName = source.Name + AutomationText(" - 副本", " - Copy", " - コピー", " - Копия");
        _automationProfiles.Add(source.Clone(copyName));
        _automationProfileIndex = _automationProfiles.Count - 1;
        RebuildAutomationPage();
    }
    private void DeleteAutomationProfile()
    {
        if (_automationRunning)
            StopAutomation(true, true);
        if (_automationProfiles.Count <= 1)
        {
            _automationProfiles[0].Actions.Clear();
            _automationProfiles[0].Name = AutomationText("配置 1", "Profile 1", "設定 1", "Профиль 1");
            _automationProfiles[0].Loop = true;
        }
        else
        {
            _automationProfiles.RemoveAt(_automationProfileIndex);
            _automationProfileIndex = Clamp(_automationProfileIndex, 0, _automationProfiles.Count - 1);
        }
        RebuildAutomationPage();
    }
    private void AddAutomationAction()
    {
        var profile = GetCurrentAutomationProfile();
        if (profile.Actions.Count >= 256) return;
        profile.Actions.Add(CreateDefaultAutomationAction(AutomationTypeAutoMine));
        RebuildAutomationPage();
    }
    private static AutomationActionConfig CreateDefaultAutomationAction(string type)
    {
        var action = new AutomationActionConfig { Type = NormalizeAutomationActionType(type), Enabled = true };
        action.DelaySeconds = action.Type == AutomationTypeWait ? 0.25f : 0f;
        switch (action.Type)
        {
            case AutomationTypeAutoMine:
            case AutomationTypeAutoChop:
            case AutomationTypeAutoHarvest:
            case AutomationTypeAutoFertilize:
            case AutomationTypeSearchContainers:
            case AutomationTypeAutoInteract:
            case AutomationTypeAutoKill:
                action.Param1 = "";
                break;
            case AutomationTypeMoveTo:
                action.Param1 = "0";
                action.Param2 = "0";
                break;
            case AutomationTypeUseAbility:
                action.Param1 = "";
                action.Param2 = "auto";
                action.Param3 = "30";
                break;
            case AutomationTypePickupByValue:
                action.Param1 = "0";
                action.Param2 = "30";
                action.Param3 = "false";
                action.Param4 = "";
                break;
            case AutomationTypeNextFloor:
            case AutomationTypeWait:
            case AutomationTypeSaveGame:
            case AutomationTypeLoadGame:
                action.Param1 = "";
                action.Param2 = "";
                action.Param3 = "";
                action.Param4 = "";
                break;
        }
        return action;
    }
    private void SetAutomationActionType(AutomationActionConfig action, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= AutomationActionTypes.Length)
            return;
        var selectedType = AutomationActionTypes[selectedIndex];
        if (string.Equals(NormalizeAutomationActionType(action.Type), selectedType, StringComparison.Ordinal))
            return;
        var replacement = CreateDefaultAutomationAction(selectedType);
        action.Type = replacement.Type;
        action.Param1 = replacement.Param1;
        action.Param2 = replacement.Param2;
        action.Param3 = replacement.Param3;
        action.Param4 = replacement.Param4;
        action.DelaySeconds = replacement.DelaySeconds;
        RebuildAutomationPage();
    }
    private void CycleAutomationAbilityTarget(AutomationActionConfig action)
    {
        var current = NormalizeAutomationAbilityTarget(action.Param2);
        action.Param2 = current == "auto" ? "self" : current == "self" ? "enemy" : "auto";
        RebuildAutomationPage();
    }
    private void MoveAutomationAction(AutomationProfile profile, int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= profile.Actions.Count || target < 0 || target >= profile.Actions.Count) return;
        var item = profile.Actions[index];
        profile.Actions.RemoveAt(index);
        profile.Actions.Insert(target, item);
        RebuildAutomationPagePreservingScroll();
    }
    private void DeleteAutomationAction(AutomationProfile profile, int index)
    {
        if (index < 0 || index >= profile.Actions.Count) return;
        if (_automationRunning) StopAutomation(true, true);
        profile.Actions.RemoveAt(index);
        RebuildAutomationPage();
    }
    private string GetAutomationActionLabel(string type)
    {
        switch (NormalizeAutomationActionType(type))
        {
            case AutomationTypeAutoMine: return AutomationText("自动挖掘", "Auto mine", "自動採掘", "Автодобыча");
            case AutomationTypeAutoChop: return AutomationText("自动砍树", "Auto chop trees", "自動伐採", "Автоматическая рубка деревьев");
            case AutomationTypeAutoHarvest: return AutomationText("自动采集", "Auto gather", "自動採集", "Автоматический сбор");
            case AutomationTypeAutoFertilize: return AutomationText("自动施肥", "Auto fertilize", "自動施肥", "Автоматическое удобрение");
            case AutomationTypeSearchContainers: return AutomationText("自动搜索容器", "Auto search containers", "コンテナ自動検索", "Автопоиск контейнеров");
            case AutomationTypeAutoInteract: return AutomationText("自动交互", "Auto interact", "自動インタラクション", "Автовзаимодействие");
            case AutomationTypeAutoKill: return AutomationText("自动杀怪", "Auto kill", "自動戦闘", "Автоубийство");
            case AutomationTypeMoveTo: return AutomationText("前往指定XZ", "Move to XZ", "指定XZへ移動", "Перейти к XZ");
            case AutomationTypeUseAbility: return AutomationText("使用指定能力&咒语", "Use ability / spell", "指定能力・呪文を使用", "Использовать способность / заклинание");
            case AutomationTypeNextFloor: return AutomationText("前往地牢下一层", "Go to next dungeon floor", "ダンジョン次階層へ移動", "Перейти на следующий этаж");
            case AutomationTypePickupByValue: return AutomationText("自动拾取", "Auto pickup", "自動取得", "Автоподбор");
            case AutomationTypeWait: return AutomationText("等待", "Wait", "待機", "Ожидание");
            case AutomationTypeSaveGame: return AutomationText("保存存档", "Save game", "ゲームを保存", "Сохранить игру");
            case AutomationTypeLoadGame: return AutomationText("加载存档", "Load game", "ゲームをロード", "Загрузить игру");
            default: return type ?? "";
        }
    }
    private string GetAutomationAbilityTargetLabel(string value)
    {
        switch (NormalizeAutomationAbilityTarget(value))
        {
            case "self": return AutomationText("自身", "Self", "自身", "На себя");
            case "enemy": return AutomationText("最近敌人", "Nearest enemy", "最寄りの敵", "Ближайший враг");
            default: return AutomationText("自动", "Auto", "自動", "Авто");
        }
    }
    private static string NormalizeAutomationActionType(string? type)
    {
        var value = (type ?? "").Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        return AutomationActionTypes.Contains(value) ? value : AutomationTypeAutoMine;
    }
    private static string NormalizeAutomationAbilityTarget(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized == "self" || normalized == "enemy") return normalized;
        return "auto";
    }
    private static int ParseAutomationInt(string? text, int fallback, int minimum, int maximum)
    {
        if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            value = fallback;
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }
}
