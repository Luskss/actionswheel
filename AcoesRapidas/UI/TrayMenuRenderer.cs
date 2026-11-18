using System.Drawing.Drawing2D;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace AcoesRapidas.UI;

/// <summary>Desenha o menu da bandeja com as cores do tema do Windows.</summary>
internal sealed class TrayMenuRenderer(ThemePalette palette) : Forms.ToolStripProfessionalRenderer
{
    private const int HighlightRadius = 6;
    private const int HighlightInset = 4;

    public static Drawing.Color ToDrawing(Media.Color color) =>
        Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(ToDrawing(palette.Hub));

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        // A borda e os cantos arredondados vêm do próprio Windows.
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected)
        {
            return;
        }

        var bounds = new Drawing.Rectangle(
            HighlightInset, 1, e.Item.Width - (HighlightInset * 2), e.Item.Height - 2);
        using var path = CreateRoundedRectangle(bounds, HighlightRadius);
        using var brush = new Drawing.SolidBrush(ToDrawing(palette.AccentStart));

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ToDrawing(e.Item.Selected ? palette.OnAccent : palette.Text);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Drawing.Pen(ToDrawing(ColorMath.Mix(palette.Hub, palette.MutedText, 0.3)));
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, HighlightInset * 2, y, e.Item.Width - (HighlightInset * 2), y);
    }

    private static GraphicsPath CreateRoundedRectangle(Drawing.Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
