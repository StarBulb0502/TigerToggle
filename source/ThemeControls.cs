using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AppNetworkSwitch
{
    internal static class Palette
    {
        internal static readonly Color Cream = Color.FromArgb(255, 248, 238);
        internal static readonly Color Ink = Color.FromArgb(91, 48, 29);
        internal static readonly Color Muted = Color.FromArgb(137, 107, 86);
        internal static readonly Color Orange = Color.FromArgb(184, 89, 41);
        internal static readonly Color Border = Color.FromArgb(229, 200, 171);
        internal static readonly Color Disabled = Color.FromArgb(242, 226, 207);
        internal static readonly Color Peach = Color.FromArgb(255, 229, 202);
        internal static readonly Color Card = Color.FromArgb(255, 239, 218);

        internal static void FillBackground(Graphics graphics, Control control)
        {
            // The rectangular fill must cover the boundary pixels before smoothing the curved shape.
            graphics.SmoothingMode = SmoothingMode.None;
            using (var background = new SolidBrush(control.Parent == null ? Cream : control.Parent.BackColor))
                graphics.FillRectangle(background, control.ClientRectangle);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
        }

        internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        internal RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs args)
        {
            Palette.FillBackground(args.Graphics, this);
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath shape = Palette.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 18F))
            using (var fill = new SolidBrush(BackColor))
            using (var border = new Pen(Palette.Border)) {
                args.Graphics.FillPath(fill, shape);
                args.Graphics.DrawPath(border, shape);
            }
        }
    }

    internal sealed class BottomPictureBox : PictureBox
    {
        protected override void OnPaint(PaintEventArgs args)
        {
            if (Image == null) return;
            float scale = Math.Min((float)ClientSize.Width / Image.Width, (float)ClientSize.Height / Image.Height);
            int width = (int)Math.Round(Image.Width * scale);
            int height = (int)Math.Round(Image.Height * scale);
            args.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            args.Graphics.DrawImage(Image, new Rectangle((ClientSize.Width - width) / 2, ClientSize.Height - height, width, height));
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;

        internal RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs args) { base.OnMouseEnter(args); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); hovered = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs args) { base.OnMouseDown(args); pressed = args.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs args) { base.OnMouseUp(args); pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs args)
        {
            Palette.FillBackground(args.Graphics, this);
            if (Width < 2 || Height < 2) return;
            Color fill = BackColor;
            if (Enabled && hovered) fill = pressed ? ControlPaint.Dark(fill, 0.08F) : ControlPaint.Light(fill, 0.08F);
            using (GraphicsPath shape = Palette.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 14F))
            using (var brush = new SolidBrush(fill))
            {
                args.Graphics.FillPath(brush, shape);
                if (FlatAppearance.BorderSize > 0)
                    using (var pen = new Pen(FlatAppearance.BorderColor)) args.Graphics.DrawPath(pen, shape);
            }
            TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle,
                Enabled ? ForeColor : Palette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(args.Graphics, Rectangle.Inflate(ClientRectangle, -7, -7), ForeColor, fill);
        }
    }

    internal sealed class RoundedComboBox : ComboBox
    {
        internal RoundedComboBox() { FlatStyle = FlatStyle.Flat; }

        protected override void OnSizeChanged(EventArgs args)
        {
            base.OnSizeChanged(args);
            if (Width < 2 || Height < 2) return;
            Region previous = Region;
            using (GraphicsPath shape = Palette.RoundedRectangle(new RectangleF(0, 0, Width, Height), 9F))
                Region = new Region(shape);
            if (previous != null) previous.Dispose();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x000F) {
                using (Graphics graphics = Graphics.FromHwnd(Handle)) PaintSelection(graphics);
            }
            else if ((message.Msg == 0x0317 || message.Msg == 0x0318) && message.WParam != IntPtr.Zero) {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) PaintSelection(graphics);
            }
        }

        private void PaintSelection(Graphics graphics)
        {
            if (Width < 2 || Height < 2) return;
            Palette.FillBackground(graphics, this);
            using (GraphicsPath shape = Palette.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1, Height - 1), 9F))
            using (var fill = new SolidBrush(Enabled ? Color.White : Palette.Disabled))
            using (var border = new Pen(Focused ? Palette.Orange : Palette.Border)) {
                graphics.FillPath(fill, shape);
                graphics.DrawPath(border, shape);
            }
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(12, 0, Math.Max(0, Width - 46), Height),
                Enabled ? Palette.Ink : Palette.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int centerY = Height / 2;
            using (var arrow = new Pen(Palette.Muted, 1.5F))
                graphics.DrawLines(arrow, new Point[] {
                    new Point(Width - 23, centerY - 2), new Point(Width - 18, centerY + 3), new Point(Width - 13, centerY - 2)
                });
        }
    }
}
