using System;

internal sealed class GameRandomService : IGameRandomService
{
    private readonly IBoundGameMethod _nextInt32;
    private readonly IBoundGameMethod _nextInt64;
    private readonly IBoundGameMethod _nextSingle;
    private readonly IBoundGameMethod _curve;

    internal GameRandomService(IGameMemberBinder binder)
    {
        if (binder == null)
            throw new ArgumentNullException("binder");

        _nextInt32 = binder.BindStaticMethod(
            typeof(EClass),
            typeof(int),
            new[] { typeof(int) },
            "rnd");
        _nextInt64 = binder.BindStaticMethod(
            typeof(EClass),
            typeof(int),
            new[] { typeof(long) },
            "rnd");
        _nextSingle = binder.BindStaticMethod(
            typeof(EClass),
            typeof(float),
            new[] { typeof(float) },
            "rndf");
        _curve = binder.BindStaticMethod(
            typeof(EClass),
            typeof(int),
            new[] { typeof(long), typeof(int), typeof(int), typeof(int) },
            "curve");
    }

    public int Next(int maximum)
    {
        return GameAccessServiceHelpers.InvokeValue<int>(_nextInt32, null, maximum);
    }

    public int Next(long maximum)
    {
        return GameAccessServiceHelpers.InvokeValue<int>(_nextInt64, null, maximum);
    }

    public float NextFloat(float maximum)
    {
        return GameAccessServiceHelpers.InvokeValue<float>(_nextSingle, null, maximum);
    }

    public int Curve(long value, int start, int end, int maximum = 100)
    {
        return GameAccessServiceHelpers.InvokeValue<int>(_curve, null, value, start, end, maximum);
    }
}
