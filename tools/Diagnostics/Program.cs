using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

// 用与 WidgetHub 完全相同的绘制逻辑，把顶部品牌栏离屏渲染成 PNG，用于检查有无重叠
internal static class P
{
    private static void Render(float k, string outPath)
    {
        int w = (int)(1080 * k);
        int h = (int)(84 * k);
        using var bmp = new Bitmap(w, h);
        bmp.SetResolution(96 * k, 96 * k);   // 模拟高 DPI 下的字体放大
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, w, h);
        using (var br = new LinearGradientBrush(rect, Color.FromArgb(70, 140, 250), Color.FromArgb(37, 99, 235), 0f))
            g.FillRectangle(br, rect);

        int iconSize = (int)(40 * k);
        int iconLeft = (int)(20 * k);
        int iconTop = (h - iconSize) / 2;
        using (var ib = new SolidBrush(Color.FromArgb(255, 255, 255, 60)))
            g.FillEllipse(ib, iconLeft, iconTop, iconSize, iconSize);

        using var f1 = new Font("Microsoft YaHei", 17F, FontStyle.Bold);
        using var f2 = new Font("Microsoft YaHei", 9F);
        using var sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        const string title = "WidgetHub";
        const string sub = "桌面挂件统一管理器 · 一个图标管理全部挂件";
        var sz1 = g.MeasureString(title, f1, PointF.Empty, sf);
        var sz2 = g.MeasureString(sub, f2, PointF.Empty, sf);
        float textLeft = iconLeft + iconSize + 14 * k;
        float gap = 6 * k;
        float total = sz1.Height + gap + sz2.Height;
        float y = (h - total) / 2f;

        g.DrawString(title, f1, Brushes.White, textLeft, y, sf);
        using (var sb = new SolidBrush(Color.FromArgb(226, 236, 255)))
            g.DrawString(sub, f2, sb, textLeft + 2 * k, y + sz1.Height + gap, sf);

        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"{outPath}  headerH={h} y={y:F1} titleH={sz1.Height:F1} subTop={y + sz1.Height + gap:F1} subH={sz2.Height:F1} bottomGap={h - (y + total):F1}");
    }

    [STAThread]
    public static void RenderHeaderFiles()
    {
        string dir = AppContext.BaseDirectory;
        Render(1.00f, Path.Combine(dir, "header_100.png"));
        Render(1.25f, Path.Combine(dir, "header_125.png"));
        Render(1.50f, Path.Combine(dir, "header_150.png"));
    }
}
