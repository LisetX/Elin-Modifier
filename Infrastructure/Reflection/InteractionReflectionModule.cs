using System;
using System.Collections.Generic;
using System.Reflection;

internal sealed class InteractionReflectionModule
{
    private static readonly Type[] AddSignature =
    {
        typeof(string),
        typeof(int),
        typeof(Action),
        typeof(string)
    };

    private readonly Dictionary<Type, InteractionAccessor> _interactionAccessors =
        new Dictionary<Type, InteractionAccessor>();
    private MethodInfo? _inventorySetDirtyAll;
    private bool _inventorySetDirtyAllResolved;

    internal Thing? GetThing(object interactionList)
    {
        if (interactionList == null)
            return null;

        try
        {
            return GetAccessor(interactionList.GetType()).GetThing(interactionList);
        }
        catch
        {
            return null;
        }
    }

    internal bool TryAdd(
        object interactionList,
        string label,
        int priority,
        Action action,
        string id)
    {
        if (interactionList == null || action == null)
            return false;

        try
        {
            var add = GetAccessor(interactionList.GetType()).Add;
            if (add == null)
                return false;
            add.Invoke(interactionList, new object[] { label, priority, action, id });
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void MarkInventoryDirty()
    {
        try
        {
            if (!_inventorySetDirtyAllResolved)
            {
                _inventorySetDirtyAllResolved = true;
                _inventorySetDirtyAll = typeof(LayerInventory).GetMethod(
                    "SetDirtyAll",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }

            var method = _inventorySetDirtyAll;
            if (method == null)
                return;
            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                method.Invoke(null, new object[] { true });
            else if (parameters.Length == 0)
                method.Invoke(null, null);
        }
        catch
        {
        }
    }

    internal void Clear()
    {
        _interactionAccessors.Clear();
        _inventorySetDirtyAll = null;
        _inventorySetDirtyAllResolved = false;
    }

    private InteractionAccessor GetAccessor(Type type)
    {
        InteractionAccessor accessor;
        if (_interactionAccessors.TryGetValue(type, out accessor))
            return accessor;

        accessor = new InteractionAccessor(type);
        _interactionAccessors[type] = accessor;
        return accessor;
    }

    private sealed class InteractionAccessor
    {
        private readonly FieldInfo? _thingField;
        private readonly PropertyInfo? _thingProperty;

        internal InteractionAccessor(Type type)
        {
            Add = type.GetMethod(
                "Add",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                AddSignature,
                null);
            _thingField = type.GetField(
                "thing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _thingProperty = type.GetProperty(
                "thing",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        internal MethodInfo? Add { get; }

        internal Thing? GetThing(object instance)
        {
            if (_thingField != null)
                return _thingField.GetValue(instance) as Thing;
            if (_thingProperty != null)
                return _thingProperty.GetValue(instance, null) as Thing;
            return null;
        }
    }
}
