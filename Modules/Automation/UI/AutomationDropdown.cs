using UnityEngine;
using UnityEngine.UI;

internal sealed class AutomationDropdown : Dropdown
{
    public bool IsListOpen { get; private set; }

    protected override GameObject CreateDropdownList(GameObject template)
    {
        var list = base.CreateDropdownList(template);
        IsListOpen = true;
        var canvas = list.GetComponent<Canvas>() ?? list.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;
        if (list.GetComponent<GraphicRaycaster>() == null)
            list.AddComponent<GraphicRaycaster>();
        list.transform.SetAsLastSibling();
        return list;
    }

    protected override void DestroyDropdownList(GameObject dropdownList)
    {
        IsListOpen = false;
        base.DestroyDropdownList(dropdownList);
    }
}
