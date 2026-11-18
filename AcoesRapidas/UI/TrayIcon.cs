using AcoesRapidas.Input;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace AcoesRapidas.UI;

/// <summary>Ícone da bandeja do sistema com as opções do app.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Drawing.Font _menuFont = new("Segoe UI", 9.5f);
    private Drawing.Icon? _icon;
    private nint _iconHandle;

    public TrayIcon(
        Action openSettings, Action openTutorial, Action openDiscordTutorial, Action reloadConfig, Action openConfig, Action exit)
    {
        _menu = new Forms.ContextMenuStrip { ShowImageMargin = false, Font = _menuFont, Padding = new Forms.Padding(0, 4, 0, 4) };
        AddItem("Configurações da rodinha", openSettings);
        AddItem("Como usar", openTutorial);
        AddItem("Como adicionar canais do Discord", openDiscordTutorial);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("Recarregar configuração", reloadConfig);
        AddItem("Abrir arquivo de configuração", openConfig);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem("Sair", exit);

        _menu.Opening += (_, _) => _menu.Renderer = new TrayMenuRenderer(WindowsTheme.Current());
        _menu.Opened += (_, _) => NativeMethods.ApplyRoundedCorners(_menu.Handle);

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Ações rápidas",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                openSettings();
            }
        };
        ApplyTheme(WindowsTheme.Current());
        _notifyIcon.Visible = true;
    }

    /// <summary>Redesenha o ícone com a cor de destaque atual.</summary>
    public void ApplyTheme(ThemePalette palette)
    {
        var previousIcon = _icon;
        var previousHandle = _iconHandle;

        using var bitmap = DrawIcon(TrayMenuRenderer.ToDrawing(palette.Accent));
        _iconHandle = bitmap.GetHicon();
        _icon = Drawing.Icon.FromHandle(_iconHandle);
        _notifyIcon.Icon = _icon;

        previousIcon?.Dispose();
        if (previousHandle != 0)
        {
            NativeMethods.DestroyIcon(previousHandle);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _menuFont.Dispose();
        _icon?.Dispose();
        NativeMethods.DestroyIcon(_iconHandle);
    }

    private void AddItem(string text, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text) { Padding = new Forms.Padding(10, 5, 18, 5) };
        item.Click += (_, _) => onClick();
        _menu.Items.Add(item);
    }

    private static Drawing.Bitmap DrawIcon(Drawing.Color color)
    {
        var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var ring = new Drawing.Pen(color, 6);
        for (var i = 0; i < 4; i++)
        {
            graphics.DrawArc(ring, 5, 5, 22, 22, (i * 90) + 8, 74);
        }

        using var hub = new Drawing.SolidBrush(color);
        graphics.FillEllipse(hub, 13, 13, 6, 6);
        return bitmap;
    }
}
