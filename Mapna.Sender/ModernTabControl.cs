using System.Drawing.Drawing2D;

namespace Mapna.Sender;

public class ModernTabControl : TabControl
{
    private static readonly Color ActiveBg = Color.White;
    private static readonly Color InactiveBg = Color.FromArgb(241, 245, 249);
    private static readonly Color HoverBg = Color.FromArgb(226, 232, 240);
    private static readonly Color StripBg = Color.FromArgb(226, 232, 240);
    private static readonly Color ActiveText = Color.FromArgb(15, 23, 42);
    private static readonly Color InactiveText = Color.FromArgb(100, 116, 139);
    private static readonly Color AccentBar = Color.FromArgb(37, 99, 235);
    private static readonly Color DotInactive = Color.FromArgb(180, 188, 199);

    private int _hoverIndex = -1;

    public ModernTabControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(200, 48);
        Padding = new Point(20, 8);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        for (var i = 0; i < TabCount; i++)
        {
            if (!GetTabRect(i).Contains(e.Location)) continue;
            if (_hoverIndex == i) return;
            _hoverIndex = i; Invalidate();
            return;
        }

        if (_hoverIndex == -1) return;
        _hoverIndex = -1; Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex == -1) return;
        _hoverIndex = -1; Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(StripBg);

        for (var i = 0; i < TabCount; i++)
        {
            var rect = GetTabRect(i);
            var isSelected = SelectedIndex == i;
            var isHover = _hoverIndex == i && !isSelected;

            var bg = isSelected ? ActiveBg : isHover ? HoverBg : InactiveBg;
            using (var brush = new SolidBrush(bg))
                g.FillRectangle(brush, rect);

            if (isSelected)
            {
                using var accentBrush = new SolidBrush(AccentBar);
                g.FillRectangle(accentBrush, rect.Left, rect.Bottom - 3, rect.Width, 3);
            }

            var dotColor = isSelected ? AccentBar : DotInactive;
            using (var dotBrush = new SolidBrush(dotColor))
                g.FillEllipse(dotBrush, rect.Left + 18, rect.Top + rect.Height / 2 - 4, 8, 8);

            var textColor = isSelected ? ActiveText : InactiveText;
            using var font = new Font(Font.FontFamily, Font.Size + (isSelected ? 0.5f : 0f),
                isSelected ? FontStyle.Bold : FontStyle.Regular);
            using var textBrush = new SolidBrush(textColor);
            using var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near };

            var textRect = new Rectangle(rect.Left + 34, rect.Top, rect.Width - 42, rect.Height - 3);
            g.DrawString(TabPages[i].Text, font, textBrush, textRect, sf);
        }

        using var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1);
        g.DrawLine(borderPen, 0, ItemSize.Height + 3, Width, ItemSize.Height + 3);
    }
}