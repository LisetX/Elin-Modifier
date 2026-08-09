using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

internal sealed class MerchantRefreshNoCostModule
{
    private readonly IBoundGameValue<UIInventory.Tab> _currentTab;
    private readonly IBoundGameValue<UIInventory.Mode> _mode;
    private readonly IBoundGameValue<InvOwner> _inventoryOwner;
    private readonly IBoundGameValue<Card> _ownerCard;
    private readonly IBoundGameValue<Trait> _trait;
    private readonly IBoundGameValue<int> _rerollCost;
    private readonly IBoundGameValue<Window> _window;
    private readonly IBoundGameValue<WindowMenu> _menuBottom;
    private readonly IBoundGameValue<LayoutGroup> _layout;
    private readonly IBoundGameValue<UIText> _mainText;
    private readonly IBoundGameValue<int> _stockExpire;
    private readonly IBoundGameMethod _refreshMenu;
    private readonly IBoundGameMethod _refreshGrid;
    private readonly IBoundGameMethod _sortWithRefresh;
    private readonly IBoundGameMethod _sortWithoutRefresh;
    private readonly IBoundGameMethod _setText;
    private readonly IBoundGameMethod _onBarter;
    private readonly IBoundGameMethod? _dice;
    private readonly IBoundGameMethod? _play;
    private readonly bool _bindingsReady;

    internal MerchantRefreshNoCostModule(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException(nameof(binder));

        var soundType = LoadedAssemblyTypeResolver.ResolveExact("SE");
        _currentTab = binder.BindInstanceValue<UIInventory.Tab>(
            typeof(UIInventory),
            GameValueAccess.Read,
            "currentTab");
        _mode = binder.BindInstanceValue<UIInventory.Mode>(
            typeof(UIInventory.Tab),
            GameValueAccess.Read,
            "mode");
        _inventoryOwner = binder.BindInstanceValue<InvOwner>(
            typeof(UIInventory),
            GameValueAccess.Read,
            "owner");
        _ownerCard = binder.BindInstanceValue<Card>(typeof(InvOwner), GameValueAccess.Read, "owner");
        _trait = binder.BindInstanceValue<Trait>(typeof(Card), GameValueAccess.Read, "trait");
        _rerollCost = binder.BindInstanceValue<int>(typeof(Trait), GameValueAccess.Read, "CostRerollShop");
        _window = binder.BindInstanceValue<Window>(typeof(UIInventory), GameValueAccess.Read, "window");
        _menuBottom = binder.BindInstanceValue<WindowMenu>(typeof(Window), GameValueAccess.Read, "menuBottom");
        _layout = binder.BindInstanceValue<LayoutGroup>(typeof(WindowMenu), GameValueAccess.Read, "layout");
        _mainText = binder.BindInstanceValue<UIText>(typeof(UIButton), GameValueAccess.Read, "mainText");
        _stockExpire = binder.BindInstanceValue<int>(typeof(Card), GameValueAccess.Write, "c_dateStockExpire");
        _refreshMenu = binder.BindInstanceMethod(
            typeof(UIInventory),
            typeof(void),
            Type.EmptyTypes,
            "RefreshMenu");
        _refreshGrid = binder.BindInstanceMethod(
            typeof(UIInventory),
            typeof(void),
            Type.EmptyTypes,
            "RefreshGrid");
        _sortWithRefresh = binder.BindInstanceMethod(
            typeof(UIInventory),
            typeof(void),
            new[] { typeof(bool) },
            "Sort");
        _sortWithoutRefresh = binder.BindInstanceMethod(
            typeof(UIInventory),
            typeof(void),
            Type.EmptyTypes,
            "Sort");
        _setText = binder.BindInstanceMethod(
            typeof(UIText),
            typeof(void),
            new[] { typeof(string) },
            "SetText");
        _onBarter = binder.BindInstanceMethod(
            typeof(Trait),
            typeof(void),
            new[] { typeof(bool) },
            "OnBarter");
        if (soundType != null)
        {
            _dice = binder.BindStaticMethod(soundType, typeof(void), Type.EmptyTypes, "Dice");
            _play = binder.BindStaticMethod(soundType, typeof(void), new[] { typeof(string) }, "Play");
        }
        _bindingsReady = _currentTab.IsBound && _mode.IsBound && _inventoryOwner.IsBound &&
                         _ownerCard.IsBound && _trait.IsBound && _rerollCost.IsBound &&
                         _window.IsBound && _menuBottom.IsBound && _layout.IsBound &&
                         _mainText.IsBound && _stockExpire.IsBound && _refreshMenu.IsBound &&
                         _refreshGrid.IsBound && (_sortWithRefresh.IsBound || _sortWithoutRefresh.IsBound) &&
                         _setText.IsBound && _onBarter.IsBound;
    }

    internal bool Enabled { get; private set; }

    internal void Load(bool enabled)
    {
        SetState(enabled);
    }

    internal void Reset()
    {
        SetState(false);
    }

    internal bool SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return false;
        SetState(enabled);
        return true;
    }

    internal void Apply(UIInventory? inventory)
    {
        if (!Enabled || !_bindingsReady || inventory == null ||
            !TryGetRefreshContext(inventory, out var merchant, out var trait, out var cost,
                out var layout))
            return;

        try
        {
            var originalLabel = "rerollShop".lang(cost.ToString(CultureInfo.InvariantCulture));
            var buttons = layout.GetComponentsInChildren<UIButton>(true);
            UIButton? refreshButton = null;
            UIText? refreshText = null;
            for (var i = 0; i < buttons.Length; i++)
            {
                if (!_mainText.TryGet(buttons[i], out var text) || text == null)
                    continue;
                if (!string.Equals(text.text, originalLabel, StringComparison.Ordinal))
                    continue;
                refreshButton = buttons[i];
                refreshText = text;
                break;
            }

            if (refreshButton == null && buttons.Length == 1 &&
                _mainText.TryGet(buttons[0], out var onlyText) && onlyText != null)
            {
                refreshButton = buttons[0];
                refreshText = onlyText;
            }

            if (refreshButton == null || refreshText == null)
                return;

            _setText.TryInvoke(
                refreshText,
                new object?[] { "rerollShop".lang("0") },
                out _);
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(() => ExecuteRefresh(inventory, merchant, trait));
        }
        catch
        {
        }
    }

    private void SetState(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        RefreshOpenMenus();
    }

    private void RefreshOpenMenus()
    {
        if (!_bindingsReady)
            return;
        try
        {
            var inventories = UnityEngine.Object.FindObjectsOfType<UIInventory>();
            for (var i = 0; i < inventories.Length; i++)
            {
                var inventory = inventories[i];
                if (inventory != null && inventory.gameObject.activeInHierarchy)
                    _refreshMenu.TryInvoke(inventory, Array.Empty<object?>(), out _);
            }
        }
        catch
        {
        }
    }

    private bool TryGetRefreshContext(
        UIInventory inventory,
        out Card merchant,
        out Trait trait,
        out int cost,
        out LayoutGroup layout)
    {
        merchant = null!;
        trait = null!;
        cost = 0;
        layout = null!;

        return _currentTab.TryGet(inventory, out var tab) && tab != null &&
               _mode.TryGet(tab, out var mode) && mode == UIInventory.Mode.Buy &&
               _inventoryOwner.TryGet(inventory, out var inventoryOwner) && inventoryOwner != null &&
               _ownerCard.TryGet(inventoryOwner, out merchant) && merchant != null &&
               _trait.TryGet(merchant, out trait) && trait != null &&
               _rerollCost.TryGet(trait, out cost) && cost > 0 &&
               _window.TryGet(inventory, out var window) && window != null &&
               _menuBottom.TryGet(window, out var menu) && menu != null &&
               _layout.TryGet(menu, out layout) && layout != null;
    }

    private void ExecuteRefresh(UIInventory inventory, Card merchant, Trait trait)
    {
        if (!Enabled)
        {
            _refreshMenu.TryInvoke(inventory, Array.Empty<object?>(), out _);
            return;
        }

        try
        {
            _dice?.TryInvoke(null, Array.Empty<object?>(), out _);
            if (!_stockExpire.TrySet(merchant, 0) ||
                !_onBarter.TryInvoke(trait, new object?[] { true }, out _))
                return;
            _refreshGrid.TryInvoke(inventory, Array.Empty<object?>(), out _);
            if (_sortWithRefresh.IsBound)
                _sortWithRefresh.TryInvoke(inventory, new object?[] { true }, out _);
            else
                _sortWithoutRefresh.TryInvoke(inventory, Array.Empty<object?>(), out _);
            _play?.TryInvoke(null, new object?[] { "shop_open" }, out _);
        }
        catch
        {
        }
    }

}

internal static class MerchantRefreshNoCostPatchContext
{
    internal static MerchantRefreshNoCostModule? Current =>
        ElinModifierPlugin.ActiveModules?.MerchantRefreshNoCost;
}

[HarmonyPatch(typeof(UIInventory), "RefreshMenu")]
internal static class UIInventoryRefreshMenuMerchantRefreshNoCostPatch
{
    private static void Postfix(UIInventory __instance)
    {
        MerchantRefreshNoCostPatchContext.Current?.Apply(__instance);
    }
}
