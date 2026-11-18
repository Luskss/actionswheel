namespace AcoesRapidas.UI;

/// <summary>
/// Cálculos do menu circular. Os ângulos são em graus, com 0 no topo e crescendo no sentido horário.
/// </summary>
public static class RadialMenuGeometry
{
    public static double SliceSweep(int count) => 360.0 / count;

    public static double SliceCenterAngle(int index, int count) => index * SliceSweep(count);

    /// <summary>Devolve a fatia na direção do deslocamento, ou nulo dentro da zona central.</summary>
    public static int? SliceAt(double dx, double dy, int count, double deadZoneRadius)
    {
        if (count <= 0 || Math.Sqrt((dx * dx) + (dy * dy)) < deadZoneRadius)
        {
            return null;
        }

        var sweep = SliceSweep(count);
        var normalized = (AngleOf(dx, dy) + (sweep / 2.0) + 360.0) % 360.0;
        return (int)(normalized / sweep) % count;
    }

    /// <summary>Anda pelas fatias em círculo. Sem fatia destacada, começa pela primeira ou pela última.</summary>
    public static int? StepSlice(int? current, int step, int count)
    {
        if (count <= 0)
        {
            return null;
        }

        return current is { } index
            ? (((index + step) % count) + count) % count
            : step > 0 ? 0 : count - 1;
    }

    public static double AngleOf(double dx, double dy) => Math.Atan2(dx, -dy) * 180.0 / Math.PI;

    /// <summary>Menor rotação, entre -180 e 180 graus, para ir de um ângulo a outro.</summary>
    public static double ShortestRotation(double from, double to)
    {
        var delta = (to - from) % 360.0;
        return ((delta + 540.0) % 360.0) - 180.0;
    }

    public static (double X, double Y) PointOnCircle(double angleDegrees, double radius)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return (radius * Math.Sin(radians), -radius * Math.Cos(radians));
    }

    /// <summary>Mantém o menu inteiro dentro da área disponível.</summary>
    public static (double X, double Y) ClampCenter(double x, double y, double width, double height, double radius) =>
        (ClampAxis(x, width, radius), ClampAxis(y, height, radius));

    private static double ClampAxis(double value, double size, double radius) =>
        size <= radius * 2 ? size / 2 : Math.Clamp(value, radius, size - radius);
}
