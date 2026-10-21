using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Shared
{
    internal static class Fonts
    {
        /// <summary>弹窗与面板里允许的最小字号（pt），其它字号都在此基础上放大。</summary>
        public const float PopupSize = 12f;

        /// <summary>弹窗标题字号（pt）。</summary>
        public const float PopupTitle = 13.5f;

        /// <summary>弹窗里强调数字用的字号（pt）。</summary>
        public const float PopupBig = 15f;

        private static readonly string[] Preferred =
        {
            "方正宋黑_GBK", "方正宋黑简体", "方正宋黑_GB18030", "方正宋黑",
            "微软雅黑", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun",
            // 下面是"非中文 Windows"上常见的候选：只有前 9 个都没装时才用得上。
            // 少了这几行，繁体/日文/韩文系统上会退到没有汉字字形的 GenericSansSerif，
            // 表现是挂件只剩一块背景色卡片、字全是空的。
            "Microsoft JhengHei", "PMingLiU", "MingLiU", "NSimSun",
            "Malgun Gothic", "Yu Gothic", "Meiryo", "MS Gothic", "MS Mincho",
            "SimSun-ExtB", "Segoe UI", "Tahoma", "Arial"
        };

        private static string familyName = string.Empty;

        public static string FamilyName
        {
            get
            {
                if (familyName.Length == 0) { Resolve(); }
                return familyName;
            }
        }

        private static void Resolve()
        {
            var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (FontFamily family in FontFamily.Families)
                {
                    string installedName = family.Name;
                    if (!installed.ContainsKey(installedName)) { installed[installedName] = installedName; }
                }
            }
            catch
            {
            }

            foreach (string candidate in Preferred)
            {
                string actual;
                if (installed.TryGetValue(candidate, out actual) && CanRenderCjk(actual))
                {
                    familyName = actual;
                    return;
                }
            }

            // 上面一个都没命中：宁可挑第一个"真的画得出汉字"的已安装字体，
            // 也不要退到 GenericSansSerif —— 那个在非中文 Windows 上没有汉字字形，
            // 分层窗口那一版用 GDI+ DrawString 画出来就是一片空白（2026-09-28 其它电脑反馈）。
            try
            {
                foreach (FontFamily family in FontFamily.Families)
                {
                    if (CanRenderCjk(family.Name))
                    {
                        familyName = family.Name;
                        return;
                    }
                }
            }
            catch
            {
            }

            familyName = FontFamily.GenericSansSerif.Name;
        }

        /// <summary>把一个汉字真的画到 32x32 的位图上，看有没有落下像素。</summary>
        private static bool CanRenderCjk(string familyName)
        {
            try
            {
                using (FontFamily family = new FontFamily(familyName))
                using (Font probe = new Font(family, 18f, FontStyle.Regular, GraphicsUnit.Point))
                using (Bitmap bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
                {
                    g.Clear(Color.Transparent);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString("中", probe, Brushes.White, 1f, 1f, format);
                    for (int y = 0; y < 32; y++)
                    {
                        for (int x = 0; x < 32; x++)
                        {
                            if (bitmap.GetPixel(x, y).A > 0) { return true; }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static Font Widget(float size)
        {
            return new Font(FamilyName, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font WidgetBold(float size)
        {
            return new Font(FamilyName, size, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static Font Ui(float size)
        {
            return new Font(FamilyName, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font UiBold(float size)
        {
            return new Font(FamilyName, size, FontStyle.Bold, GraphicsUnit.Point);
        }

        /// <summary>逐级缩小字号，直到文本能放进给定宽度，避免出现“抗战…”这类截断。</summary>
        public static float Fit(Graphics graphics, string text, float size, float limit, float minSize)
        {
            while (size > minSize)
            {
                using (Font probe = Ui(size))
                {
                    if (graphics.MeasureString(text, probe).Width <= limit) { return size; }
                }
                size -= 0.5f;
            }
            return size;
        }
    }

    internal static class Theme
    {
        public static readonly Color Key = Color.FromArgb(1, 2, 3);

        public static readonly Color Text = Color.FromArgb(235, 240, 245);
        public static readonly Color TextDim = Color.FromArgb(150, 158, 168);
        public static readonly Color TextFaint = Color.FromArgb(110, 118, 128);
        public static readonly Color Panel = Color.FromArgb(30, 34, 40);
        public static readonly Color PanelSoft = Color.FromArgb(40, 45, 52);
        public static readonly Color Line = Color.FromArgb(70, 78, 88);
        public static readonly Color Accent = Color.FromArgb(80, 170, 255);
        public static readonly Color AccentSoft = Color.FromArgb(38, 78, 112);
        public static readonly Color Weekend = Color.FromArgb(255, 150, 130);
        public static readonly Color Holiday = Color.FromArgb(255, 110, 110);
        public static readonly Color Work = Color.FromArgb(120, 200, 255);
        public static readonly Color Lunar = Color.FromArgb(168, 176, 188);
        public static readonly Color Term = Color.FromArgb(120, 220, 170);
        public static readonly Color Today = Color.FromArgb(46, 108, 168);
        public static readonly Color WarmYellow = Color.FromArgb(255, 205, 90);
        public static readonly Color SkyBlue = Color.FromArgb(120, 190, 255);
        public static readonly Color Income = Color.FromArgb(255, 205, 90);
        public static readonly Color Expense = Color.FromArgb(255, 140, 120);
        public static readonly Color Balance = Color.FromArgb(120, 190, 255);
        public static readonly Color Note = Color.FromArgb(255, 210, 90);

        public static readonly string[] NoteColors =
        {
            "#FFD25A", "#7FB2FF", "#7CDCA0", "#FF9A8B", "#C79BFF", "#67D8D8", "#FFB3D9", "#E8E8E8"
        };

        public static Color FromHex(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) { return Note; }
                string text = hex.TrimStart('#');
                if (text.Length == 3)
                {
                    text = new string(new[] { text[0], text[0], text[1], text[1], text[2], text[2] });
                }

                if (text.Length != 6) { return Note; }
                int red = Convert.ToInt32(text.Substring(0, 2), 16);
                int green = Convert.ToInt32(text.Substring(2, 2), 16);
                int blue = Convert.ToInt32(text.Substring(4, 2), 16);
                return Color.FromArgb(red, green, blue);
            }
            catch
            {
                return Note;
            }
        }

        public static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }
}
