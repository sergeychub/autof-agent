using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using Media = System.Windows.Media;

namespace WorkstationAgent.Forms;

internal sealed class TrayMenu : ContextMenuStrip
{
    private readonly Font _menuFont = new("Segoe UI", 10);

    public TrayMenu()
    {
        Font = _menuFont;
        ImageScalingSize = new Size(20, 20);
        if (!SystemInformation.HighContrast)
        {
            ForeColor = Color.FromArgb(32, 34, 38);
            Renderer = new MaterialMenuRenderer();
        }
    }

    public void AddCommand(string caption, PackIconKind icon, EventHandler action)
    {
        var ink = SystemInformation.HighContrast ? SystemColors.MenuText : Color.FromArgb(81, 84, 93);
        var item = new CommandItem(caption, RenderIcon(icon, ink))
        {
            Padding = new Padding(0, 4, 0, 4)
        };
        item.Click += action;
        Items.Add(item);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _menuFont.Dispose();
    }

    private static Bitmap RenderIcon(PackIconKind kind, Color ink)
    {
        const int size = 32;
        var shape = Media.Geometry.Parse(new PackIcon { Kind = kind }.Data);
        var brush = new Media.SolidColorBrush(Media.Color.FromRgb(ink.R, ink.G, ink.B));
        var visual = new Media.DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.PushTransform(new Media.ScaleTransform(size / 24d, size / 24d));
            drawing.DrawGeometry(brush, null, shape);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var image = new Bitmap(stream);
        return new Bitmap(image);
    }

    private sealed class CommandItem(string caption, Bitmap icon) : ToolStripMenuItem(caption, icon)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) icon.Dispose();
        }
    }

    private sealed class MaterialMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Surface = Color.FromArgb(255, 253, 250);
        private static readonly Color Outline = Color.FromArgb(225, 221, 213);
        private static readonly Color Highlight = Color.FromArgb(233, 235, 250);
        private static readonly Color Indigo = Color.FromArgb(63, 81, 181);

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Surface);

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Outline);
            e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Outline);
            var inset = (int)(e.Graphics.DpiX * 12 / 96);
            var center = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, inset, center, e.Item.Width - inset, center);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;

            var dpi = e.Graphics.DpiX / 96f;
            var area = new RectangleF(4 * dpi, 1 * dpi, e.Item.Width - 8 * dpi, e.Item.Height - 2 * dpi);
            using var shape = RoundedRectangle(area, 7 * dpi);
            using var brush = new SolidBrush(Highlight);
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, shape);
            e.Graphics.Restore(state);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled) e.TextColor = Indigo;
            base.OnRenderItemText(e);
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            var diameter = 2 * radius;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
