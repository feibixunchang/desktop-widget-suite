using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace XiaoPuHeWeather;

public sealed class WidgetSurface : IDisposable
{
    private const char CelsiusUnit = '\u2103';
    private const int InkThreshold = 24;

    /// <summary>
    /// 背景是不透明度 205（约 80%）的黑色半透明底。
    /// 原来是 alpha=1（等于全透明），而分层窗口里 alpha=0 的像素是不接收鼠标的，
    /// 所以整条挂件只有文字能点中、空白处会穿透到桌面。改成非 0 的半透明黑底之后，
    /// 整条都能点右键菜单。
    /// </summary>
    /// <summary>默认背景浓淡（205 约等于 80% 黑），可由用户设置覆盖。</summary>
    public const int DefaultBackgroundAlpha = 205;

    private static readonly ConcurrentDictionary<string, float> UnitSideBearings = new();

    private static readonly StringFormat SegmentFormat = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.None
    };

    private WidgetSurface(Bitmap bitmap, Rectangle firstLineBounds, Rectangle secondLineBounds)
    {
        Bitmap = bitmap;
        FirstLineBounds = firstLineBounds;
        SecondLineBounds = secondLineBounds;
    }

    public Bitmap Bitmap { get; }

    public Rectangle FirstLineBounds { get; }

    public Rectangle SecondLineBounds { get; }

    public void Dispose()
    {
        Bitmap.Dispose();
    }

    public static WidgetSurface Create(
        string firstLine,
        string secondLine,
        Font firstLineFont,
        Color firstLineColor,
        Font secondLineFont,
        Color secondLineColor,
        int backgroundAlpha = DefaultBackgroundAlpha)
        {
        
        const int horizontalPadding = 8;
        const int verticalPadding = 5;
        // 行间距要和"第二行 ↔ 天气CPU 那一行"的视觉间距一致（用户要求，看着协调）。
        // 那个间距 = 窗口间距 4 + 本控件下内边距 5 + 天气CPU 上内边距约 3.5 ≈ 12。
        const int lineGap = 12;
        using var measurementBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var measurementGraphics = Graphics.FromImage(measurementBitmap);
        ConfigureGraphics(measurementGraphics);
        using var format = CreateFormat();
        var firstSize = MeasureLine(measurementGraphics, firstLine, firstLineFont, format);
        var secondSize = MeasureLine(measurementGraphics, secondLine, secondLineFont, format);
        var width = Math.Max(1, (int)Math.Ceiling(Math.Max(firstSize.Width, secondSize.Width)) + horizontalPadding * 2);
        var height = Math.Max(
            1,
            (int)Math.Ceiling(firstSize.Height + secondSize.Height) + verticalPadding * 2 + lineGap);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(graphics);
        int alpha = backgroundAlpha < 0 ? 0 : (backgroundAlpha > 255 ? 255 : backgroundAlpha);
          graphics.Clear(Color.FromArgb(alpha, 0, 0, 0));
        var firstBounds = new Rectangle(
            0,
            verticalPadding,
            width,
            Math.Max(1, (int)Math.Ceiling(firstSize.Height)));
        var secondBounds = new Rectangle(
            0,
            verticalPadding + firstBounds.Height + lineGap,
            width,
            Math.Max(1, (int)Math.Ceiling(secondSize.Height)));
        using var firstBrush = new SolidBrush(Color.FromArgb(255, firstLineColor));
        using var secondBrush = new SolidBrush(Color.FromArgb(255, secondLineColor));
        DrawLine(graphics, firstLine, firstLineFont, firstBrush, firstBounds, format);
        DrawLine(graphics, secondLine, secondLineFont, secondBrush, secondBounds, format);
        return new WidgetSurface(bitmap, firstBounds, secondBounds);
    }

    private static void DrawLine(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        Rectangle bounds,
        StringFormat format)
    {
        var segments = SplitUnits(text);
        if (segments.Length == 1)
        {
            graphics.DrawString(text, font, brush, bounds, format);
            return;
        }

        var lineHeight = graphics.MeasureString(
            text,
            font,
            new SizeF(float.MaxValue, float.MaxValue),
            format).Height;
        var unitAdvance = MeasureUnitAdvance(graphics, font, format);
        var unitSideBearing = MeasureUnitSideBearing(graphics, font, unitAdvance);
        var unitStep = unitAdvance - unitSideBearing;
        var x = bounds.Left + (bounds.Width - MeasureLine(graphics, text, font, format).Width) / 2f;
        var top = bounds.Top + (bounds.Height - lineHeight) / 2f;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length > 0)
            {
                graphics.DrawString(segment, font, brush, new PointF(x, top), SegmentFormat);
                x += graphics.MeasureString(
                    segment,
                    font,
                    new SizeF(float.MaxValue, float.MaxValue),
                    format).Width;
            }

            if (index < segments.Length - 1)
            {
                graphics.DrawString(
                    CelsiusUnit.ToString(),
                    font,
                    brush,
                    new PointF(x - unitSideBearing, top),
                    SegmentFormat);
                x += unitStep;
            }
        }
    }

    private static SizeF MeasureLine(Graphics graphics, string text, Font font, StringFormat format)
    {
        var size = graphics.MeasureString(text, font, new SizeF(float.MaxValue, float.MaxValue), format);
        var segments = SplitUnits(text);
        if (segments.Length == 1)
        {
            return size;
        }

        var unitAdvance = MeasureUnitAdvance(graphics, font, format);
        var unitStep = unitAdvance - MeasureUnitSideBearing(graphics, font, unitAdvance);
        var width = 0f;
        foreach (var segment in segments)
        {
            width += graphics.MeasureString(
                segment,
                font,
                new SizeF(float.MaxValue, float.MaxValue),
                format).Width;
        }

        width += unitStep * (segments.Length - 1);
        return new SizeF(width, size.Height);
    }

    private static float MeasureUnitAdvance(Graphics graphics, Font font, StringFormat format)
    {
        return graphics.MeasureString(
            CelsiusUnit.ToString(),
            font,
            new SizeF(float.MaxValue, float.MaxValue),
            format).Width;
    }

    private static string[] SplitUnits(string text)
    {
        return text.Contains(CelsiusUnit) ? text.Split(CelsiusUnit) : new[] { text };
    }

    private static float MeasureUnitSideBearing(Graphics graphics, Font font, float unitAdvance)
    {
        var key = $"{font.FontFamily.Name}|{font.Size}|{font.Style}";
        return UnitSideBearings.GetOrAdd(key, _ => MeasureUnitSideBearingCore(graphics, font, unitAdvance));
    }

    private static float MeasureUnitSideBearingCore(Graphics graphics, Font font, float unitAdvance)
    {
        var width = Math.Max(4, (int)Math.Ceiling(unitAdvance) + 4);
        var height = Math.Max(4, (int)Math.Ceiling(font.GetHeight(graphics)) + 4);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var bitmapGraphics = Graphics.FromImage(bitmap);
        ConfigureGraphics(bitmapGraphics);
        bitmapGraphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(Color.Black);
        bitmapGraphics.DrawString(
            CelsiusUnit.ToString(),
            font,
            brush,
            new PointF(0, 0),
            StringFormat.GenericTypographic);
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).A > InkThreshold)
                {
                    return Math.Min(x, unitAdvance / 2f);
                }
            }
        }

        return 0f;
    }

    private static StringFormat CreateFormat()
    {
        return new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None
        };
    }

    private static void ConfigureGraphics(Graphics graphics)
    {
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }
}
