using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class ElinModifierPlugin
{
    private void OpenLGuiAbilityEditor(Chara target, AbilityDef ability, bool isPc)
    {
        CloseLGuiEditorModal(true);
        if (_lGuiWindow == null)
            return;
        var prefix = GetTargetInputPrefix(target, isPc) + "ability:" + ability.Id + ":";
        var levelKey = prefix + "level";
        var chanceKey = prefix + "chance";
        var powerKey = prefix + "power";
        var hpCostKey = prefix + "hpCost";
        var mpCostKey = prefix + "mpCost";
        var spCostKey = prefix + "spCost";
        var stockKey = prefix + "stock";
        var customAttributesEnabled = IsAbilityCustomAttributesEnabled(ability.Id);
        EnsureInput(levelKey, GetAbilityLevel(target, ability).ToString(CultureInfo.InvariantCulture));
        if (customAttributesEnabled)
        {
            EnsureInput(chanceKey, GetAbilityDisplayChance(target, ability).ToString(CultureInfo.InvariantCulture));
            EnsureInput(powerKey, GetAbilityDisplayPower(target, ability).ToString(CultureInfo.InvariantCulture));
            EnsureInput(hpCostKey, GetAbilityCost(target, ability, 0).ToString(CultureInfo.InvariantCulture));
            EnsureInput(mpCostKey, GetAbilityCost(target, ability, 1).ToString(CultureInfo.InvariantCulture));
            EnsureInput(spCostKey, GetAbilityCost(target, ability, 2).ToString(CultureInfo.InvariantCulture));
        }
        EnsureInput(stockKey, GetAbilityStock(target, ability).ToString(CultureInfo.InvariantCulture));

        _lGuiEditorModal = new GameObject("RuntimeAbilityEditor", typeof(RectTransform), typeof(Image));
        _lGuiEditorModal.transform.SetParent(_lGuiRoot!.transform, false);
        PrepareLGuiStandaloneModal(_lGuiEditorModal);
        var modal = (RectTransform)_lGuiEditorModal.transform;
        modal.anchorMin = new Vector2(0.5f, 0.5f);
        modal.anchorMax = new Vector2(0.5f, 0.5f);
        modal.pivot = new Vector2(0.5f, 0.5f);
        modal.sizeDelta = new Vector2(1260f, customAttributesEnabled ? 640f : 400f);
        modal.anchoredPosition = Vector2.zero;
        _lGuiEditorModal.GetComponent<Image>().color = GetLGuiRowColor(0, true);
        var title = CreateLGuiText(modal, "Title", GetAbilityLabel(ability), 22, TextAnchor.MiddleLeft, FontStyle.Normal);
        PlaceLGuiRect(title.rectTransform, 24f, 16f, 950f, 42f);
        EnableLGuiModalDragging(modal, title);
        CreateLGuiButton(modal, "Close", "×", 1180f, 14f, 54f, 44f, CloseLGuiEditorModal);
        var content = CreateLGuiRect(modal, "Content");
        StretchLGuiRect(content, 24f, 0f, 24f, 0f);
        var customAttributesToggle = CreateLGuiToggle(
            content,
            "CustomAbilityAttributes",
            0f,
            82f,
            420f,
            44f,
            out var customAttributesLabel);
        customAttributesLabel.text = T("自定义具体属性", "Custom specific attributes");
        customAttributesToggle.isOn = customAttributesEnabled;
        customAttributesToggle.onValueChanged.AddListener(value =>
        {
            if (value)
                EnableAbilityCustomAttributes(target, ability);
            else
                DisableAbilityCustomAttributes(ability.Id);

            _inputs.Remove(chanceKey);
            _inputs.Remove(powerKey);
            _inputs.Remove(hpCostKey);
            _inputs.Remove(mpCostKey);
            _inputs.Remove(spCostKey);
            SaveConfig(false, false);
            OpenLGuiAbilityEditor(target, ability, isPc);
        });

        var fieldY = 136f;
        fieldY = AddLGuiBoundInput(content, T("等级", "Level"), () => _inputs[levelKey], value => _inputs[levelKey] = value, fieldY, 160f);
        if (customAttributesEnabled)
        {
            fieldY = AddLGuiBoundInput(content, T("成功率", "Chance"), () => _inputs[chanceKey], value => _inputs[chanceKey] = value, fieldY, 160f);
            fieldY = AddLGuiBoundInput(content, T("威力", "Power"), () => _inputs[powerKey], value => _inputs[powerKey] = value, fieldY, 160f);
            fieldY = AddLGuiBoundInput(content, T("生命消耗", "HP Cost"), () => _inputs[hpCostKey], value => _inputs[hpCostKey] = value, fieldY, 160f);
            fieldY = AddLGuiBoundInput(content, T("玛那消耗", "MP Cost"), () => _inputs[mpCostKey], value => _inputs[mpCostKey] = value, fieldY, 160f);
            fieldY = AddLGuiBoundInput(content, T("活力消耗", "SP Cost"), () => _inputs[spCostKey], value => _inputs[spCostKey] = value, fieldY, 160f);
        }
        fieldY = AddLGuiBoundInput(content, T("库存", "Stock"), () => _inputs[stockKey], value => _inputs[stockKey] = value, fieldY, 160f);
        CreateLGuiButton(content, "Apply", T("应用", "Apply"), 220f, fieldY + 12f, 150f, 48f, () =>
        {
            ApplyAbilityValues(
                target,
                ability,
                levelKey,
                chanceKey,
                powerKey,
                hpCostKey,
                mpCostKey,
                spCostKey,
                stockKey,
                customAttributesEnabled,
                true);
            MarkCharacterDataDirty();
            CloseLGuiEditorModal();
        });
        ApplyLGuiVisualSettings();
    }
    private void CloseLGuiEditorModal()
    {
        CloseLGuiEditorModal(_lGuiModalRestoreMainOnClose);
    }
    private void CloseLGuiEditorModal(bool restoreMain)
    {
        var closingModal = _lGuiEditorModal;
        _lGuiEditorModal = null;
        _lGuiModalHidesMain = false;
        _lGuiModalRestoreMainOnClose = false;
        if (closingModal != null)
        {
            var group = closingModal.GetComponent<CanvasGroup>();
            var fade = closingModal.GetComponent<LGuiFadeDriver>();
            if (group != null && fade != null)
            {
                group.blocksRaycasts = false;
                group.interactable = false;
                fade.FadeTo(0f, LGuiModalFadeOutSeconds, false, () =>
                {
                    if (closingModal != null)
                        UnityEngine.Object.Destroy(closingModal);
                });
            }
            else
            {
                UnityEngine.Object.Destroy(closingModal);
            }
        }
        if (_lGuiWindowGroup != null)
        {
            if (restoreMain && _lGuiVisible && _lGuiRoot != null && _lGuiRoot.activeSelf && _lGuiWindowFade != null)
                _lGuiWindowFade.FadeTo(Clamp(_uiAlpha, 0.2f, 1f), LGuiModalFadeOutSeconds, true);
            else if (restoreMain)
            {
                _lGuiWindowGroup.alpha = Clamp(_uiAlpha, 0.2f, 1f);
                _lGuiWindowGroup.blocksRaycasts = true;
                _lGuiWindowGroup.interactable = true;
            }
        }
        if (_lGuiBlockerImage != null)
            _lGuiBlockerImage.raycastTarget = _forceGameUnfocus;
        if (!restoreMain && IsLGuiInitialized())
            BeginLGuiHide();
    }
    private void EnableLGuiModalDragging(RectTransform modal, Graphic dragSurface)
    {
        if (modal == null || dragSurface == null || _lGuiCanvas == null)
            return;
        dragSurface.raycastTarget = true;
        var drag = dragSurface.gameObject.GetComponent<LGuiDragHandle>() ?? dragSurface.gameObject.AddComponent<LGuiDragHandle>();
        drag.Initialize(modal, _lGuiCanvas);
    }
    private void PrepareLGuiStandaloneModal(GameObject modal)
    {
        var group = modal.GetComponent<CanvasGroup>() ?? modal.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        var fade = modal.GetComponent<LGuiFadeDriver>() ?? modal.AddComponent<LGuiFadeDriver>();
        fade.Initialize(group);
        fade.SetImmediate(0f, false);
        fade.FadeTo(1f, LGuiModalFadeInSeconds, true);
        _lGuiModalHidesMain = true;
        if (_lGuiWindowGroup != null)
        {
            if (_lGuiWindowFade != null)
                _lGuiWindowFade.SetImmediate(0f, false);
            else
            {
                _lGuiWindowGroup.alpha = 0f;
                _lGuiWindowGroup.blocksRaycasts = false;
                _lGuiWindowGroup.interactable = false;
            }
        }
        if (_lGuiBlockerImage != null)
            _lGuiBlockerImage.raycastTarget = true;
    }
}
