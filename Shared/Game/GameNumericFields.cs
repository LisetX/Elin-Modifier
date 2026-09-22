using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;

internal static class GameNumericField
{
    internal static Func<T, long> Read<T>(string name)
    {
        FieldInfo? field = null;
        try
        {
            field = AccessTools.Field(typeof(T), name);
        }
        catch
        {
        }
        if (field == null)
            return _ => 0L;

        try
        {
            var parameter = Expression.Parameter(typeof(T), "target");
            var body = Expression.Convert(Expression.Field(parameter, field), typeof(long));
            return Expression.Lambda<Func<T, long>>(body, parameter).Compile();
        }
        catch
        {
        }

        var reflected = field;
        return target =>
        {
            try
            {
                var value = reflected.GetValue(target);
                return value == null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0L;
            }
        };
    }
}

internal static class CellNumericFields
{
    private static readonly Func<Cell, long> ObjGetter = GameNumericField.Read<Cell>("obj");
    private static readonly Func<Cell, long> ObjValGetter = GameNumericField.Read<Cell>("objVal");
    private static readonly Func<Cell, long> TopHeightGetter = GameNumericField.Read<Cell>("topHeight");
    private static readonly Func<Cell, long> MinHeightGetter = GameNumericField.Read<Cell>("minHeight");

    internal static int Obj(Cell? cell) => cell == null ? 0 : (int)ObjGetter(cell);

    internal static int ObjVal(Cell? cell) => cell == null ? 0 : (int)ObjValGetter(cell);

    internal static int TopHeight(Cell? cell) => cell == null ? 0 : (int)TopHeightGetter(cell);

    internal static int MinHeight(Cell? cell) => cell == null ? 0 : (int)MinHeightGetter(cell);
}

internal static class AttackNumericFields
{
    private static readonly Func<AttackProcess, long> ToHitGetter =
        GameNumericField.Read<AttackProcess>("toHit");

    private static readonly Func<AttackProcess, long> EvasionGetter =
        GameNumericField.Read<AttackProcess>("evasion");

    internal static long ToHit(AttackProcess? attack) => attack == null ? 0L : ToHitGetter(attack);

    internal static long Evasion(AttackProcess? attack) => attack == null ? 0L : EvasionGetter(attack);
}
