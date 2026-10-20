using System.Drawing;
using System.Drawing.Drawing2D;

// 形状对照实验：同一时间画出多种"圆角药丸"的画法，实屏截图对比，
// 找出在真实屏幕上不会出现"左侧 V 形缺口"的画法。
internal static class Shapes
{
    public static void Show()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new F());
    }

    // 覆盖测试窗：用来验证挂件是否真的沉到桌面层（被别的窗口盖住）
    public static void Cover(int x, int y, int w, int h)
    {
        Application.EnableVisualStyles();
        Application.Run(new CoverForm(x, y, w, h));
    }

    private sealed class CoverForm : Form
    {
        public CoverForm(int x, int y, int w, int h)
        {
            Text = "Cover";
            BackColor = Color.Magenta;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
            ClientSize = new Size(w, h);
            FormBorderStyle = FormBorderStyle.None;
            TopMost = false;
        }
    }

    private sealed class F : Form
    {
        public F()
        {
            Text = "Shape Test";
            ClientSize = new Size(900, 780);
            BackColor = Color.White;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var font = new Font("Microsoft YaHei", 9F);
            var label = new SolidBrush(Color.Black);
            var blue = new SolidBrush(Color.FromArgb(59, 130, 246));

            void Row(string title, Func<Rectangle, GraphicsPath> make, FillMode fm, int row)
            {
                var r = new Rectangle(340, 20 + row * 80, 120, 34);
                g.DrawString(title, font, label, 10, r.Y + 10);
                using var p = make(r);
                p.FillMode = fm;
                g.FillPath(blue, p);
                g.DrawRectangle(Pens.Gray, r.X, r.Y, r.Width, r.Height);
            }

            Row("A 4×AddArc+CloseFigure, Winding", r => P1(r, 8), FillMode.Winding, 0);
            Row("B 4×AddArc+CloseFigure, Alternate", r => P1(r, 8), FillMode.Alternate, 1);
            Row("C 显式 AddLine 连接, Winding", r => P2(r, 8), FillMode.Winding, 2);
            Row("D 显式 AddLine 连接, Alternate", r => P2(r, 8), FillMode.Alternate, 3);
            g.DrawString("E 纯矩形(无圆角)", font, label, 10, 30 + 4 * 80);
            g.FillRectangle(blue, 340, 20 + 4 * 80, 120, 34);
            g.DrawRectangle(Pens.Gray, 340, 20 + 4 * 80, 120, 34);
            Row("F 半径16的 4×AddArc, Alternate", r => P1(r, 16), FillMode.Alternate, 5);
            // G: 用 Region 填充同一条路径
            {
                var r = new Rectangle(340, 20 + 6 * 80, 120, 34);
                g.DrawString("G Region(P1路径) 填充", font, label, 10, r.Y + 10);
                using var p = P1(r, 8);
                using var reg = new Region(p);
                g.FillRegion(blue, reg);
                g.DrawRectangle(Pens.Gray, r.X, r.Y, r.Width, r.Height);
            }
            // H: 贝塞尔曲线构造
            {
                var r = new Rectangle(340, 20 + 7 * 80, 120, 34);
                g.DrawString("H 贝塞尔曲线构造", font, label, 10, r.Y + 10);
                using var p = PB(r, 8);
                g.FillPath(blue, p);
                g.DrawRectangle(Pens.Gray, r.X, r.Y, r.Width, r.Height);
            }
            // I: 反向顺序 AddArc
            {
                var r = new Rectangle(340, 20 + 8 * 80, 120, 34);
                g.DrawString("I 反向顺序 AddArc", font, label, 10, r.Y + 10);
                using var p = P5(r, 8);
                g.FillPath(blue, p);
                g.DrawRectangle(Pens.Gray, r.X, r.Y, r.Width, r.Height);
            }
        }

        private static GraphicsPath PB(Rectangle r, int radius)
        {
            float rr = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
            float k = rr * 0.5523f;
            var gp = new GraphicsPath();
            gp.StartFigure();
            gp.AddLine(r.Left + rr, r.Top, r.Right - rr, r.Top);
            gp.AddBezier(r.Right - rr, r.Top, r.Right - rr + k, r.Top, r.Right, r.Top + rr - k, r.Right, r.Top + rr);
            gp.AddLine(r.Right, r.Top + rr, r.Right, r.Bottom - rr);
            gp.AddBezier(r.Right, r.Bottom - rr, r.Right, r.Bottom - rr + k, r.Right - rr + k, r.Bottom, r.Right - rr, r.Bottom);
            gp.AddLine(r.Right - rr, r.Bottom, r.Left + rr, r.Bottom);
            gp.AddBezier(r.Left + rr, r.Bottom, r.Left + rr - k, r.Bottom, r.Left, r.Bottom - rr + k, r.Left, r.Bottom - rr);
            gp.AddLine(r.Left, r.Bottom - rr, r.Left, r.Top + rr);
            gp.AddBezier(r.Left, r.Top + rr, r.Left, r.Top + rr - k, r.Left + rr - k, r.Top, r.Left + rr, r.Top);
            gp.CloseFigure();
            return gp;
        }

        private static GraphicsPath P5(Rectangle r, int radius)
        {
            var gp = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height) - 1);
            gp.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 180);
            gp.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            gp.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.CloseFigure();
            return gp;
        }

        private static GraphicsPath P1(Rectangle r, int radius)
        {
            var gp = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height) - 1);
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            gp.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 180);
            gp.CloseFigure();
            return gp;
        }

        private static GraphicsPath P2(Rectangle r, int radius)
        {
            var gp = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height) - 1);
            gp.AddArc(r.X, r.Y, d, d, 180, 90);
            gp.AddLine(r.X + d / 2, r.Y, r.X + r.Width - d / 2, r.Y);
            gp.AddArc(r.X + r.Width - d, r.Y, d, d, 270, 90);
            gp.AddLine(r.X + r.Width, r.Y + d / 2, r.X + r.Width, r.Y + r.Height - d / 2);
            gp.AddArc(r.X + r.Width - d, r.Y + r.Height - d, d, d, 0, 90);
            gp.AddLine(r.X + r.Width - d / 2, r.Y + r.Height, r.X + d / 2, r.Y + r.Height);
            gp.AddArc(r.X, r.Y + r.Height - d, d, d, 90, 180);
            gp.CloseFigure();
            return gp;
        }
    }
}
