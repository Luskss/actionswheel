using System.Windows;
using System.Windows.Media;

namespace AcoesRapidas.UI;

/// <summary>Forma de uma fatia da rodinha, com vão de largura constante entre as fatias.</summary>
internal static class SliceShape
{
    /// <param name="centerAngle">Ângulo central da fatia, em graus.</param>
    /// <param name="sliceCount">Quantidade de fatias da rodinha.</param>
    /// <param name="innerEdge">Raio da borda interna, já com o arredondamento.</param>
    /// <param name="outerEdge">Raio da borda externa, já com o arredondamento.</param>
    /// <param name="cornerRadius">Raio dos cantos, desenhados com um traço arredondado dessa metade de espessura.</param>
    /// <param name="gap">Largura do vão entre fatias vizinhas.</param>
    public static Geometry Create(
        double centerAngle, int sliceCount, double innerEdge, double outerEdge, double cornerRadius, double gap)
    {
        var outer = outerEdge - cornerRadius;
        var inner = innerEdge + cornerRadius;
        var inset = (gap / 2) + cornerRadius;
        var halfSweep = RadialMenuGeometry.SliceSweep(sliceCount) / 2;

        var outerOffset = halfSweep - ToDegrees(Math.Asin(inset / outer));
        var innerOffset = halfSweep - ToDegrees(Math.Asin(inset / inner));

        return Build(centerAngle, inner, outer, innerOffset, outerOffset);
    }

    /// <summary>Aba curva de largura fixa, que acompanha a borda da rodinha.</summary>
    /// <param name="centerAngle">Ângulo central da aba, em graus.</param>
    /// <param name="halfWidth">Metade da largura da aba.</param>
    /// <param name="innerEdge">Raio da borda interna, já com o arredondamento.</param>
    /// <param name="outerEdge">Raio da borda externa, já com o arredondamento.</param>
    /// <param name="cornerRadius">Raio dos cantos, desenhados com um traço arredondado.</param>
    public static Geometry CreateTab(
        double centerAngle, double halfWidth, double innerEdge, double outerEdge, double cornerRadius)
    {
        var outer = outerEdge - cornerRadius;
        var inner = innerEdge + cornerRadius;
        var side = halfWidth - cornerRadius;

        return Build(
            centerAngle, inner, outer, ToDegrees(Math.Asin(side / inner)), ToDegrees(Math.Asin(side / outer)));
    }

    private static Geometry Build(double centerAngle, double inner, double outer, double innerOffset, double outerOffset)
    {
        var figure = new PathFigure { StartPoint = ToPoint(centerAngle - outerOffset, outer), IsClosed = true };
        figure.Segments.Add(new ArcSegment(
            ToPoint(centerAngle + outerOffset, outer), new Size(outer, outer), 0, outerOffset > 90, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(ToPoint(centerAngle + innerOffset, inner), true));
        figure.Segments.Add(new ArcSegment(
            ToPoint(centerAngle - innerOffset, inner), new Size(inner, inner), 0, innerOffset > 90, SweepDirection.Counterclockwise, true));

        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;

    private static Point ToPoint(double angle, double radius)
    {
        var (x, y) = RadialMenuGeometry.PointOnCircle(angle, radius);
        return new Point(x, y);
    }
}
