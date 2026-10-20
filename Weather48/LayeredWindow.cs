using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XiaoPuHeWeather;

internal static class LayeredWindow
{
    public const int WsExLayered = 0x00080000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hDC,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hWnd,
        IntPtr hdcDst,
        ref Point pptDst,
        ref Size psize,
        IntPtr hdcSrc,
        ref Point pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    public static void Update(IntPtr handle, Bitmap bitmap, System.Drawing.Point location)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法获取屏幕设备上下文。");
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            throw new InvalidOperationException("无法创建内存设备上下文。");
        }

        var bitmapHandle = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        var bits = IntPtr.Zero;
        try
        {
            var bitmapInfo = new BitmapInfo
            {
                Size = Marshal.SizeOf<BitmapInfo>(),
                Width = bitmap.Width,
                Height = -bitmap.Height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb
            };
            bitmapHandle = CreateDIBSection(
                screenDc,
                ref bitmapInfo,
                DibRgbColors,
                out bits,
                IntPtr.Zero,
                0);
            if (bitmapHandle == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法创建透明天气图层。");
            }

            oldBitmap = SelectObject(memoryDc, bitmapHandle);
            if (oldBitmap == IntPtr.Zero)
            {
                throw new InvalidOperationException("无法选择透明天气图层。");
            }

            CopyPremultipliedArgb(bitmap, bits);
            var destination = new Point(location.X, location.Y);
            var size = new Size(bitmap.Width, bitmap.Height);
            var source = new Point(0, 0);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            if (!UpdateLayeredWindow(handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, UlwAlpha))
            {
                throw new InvalidOperationException("无法更新透明天气窗口。");
            }
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                SelectObject(memoryDc, oldBitmap);
            }

            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static void CopyPremultipliedArgb(Bitmap bitmap, IntPtr destination)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var source = IntPtr.Add(data.Scan0, y * data.Stride);
                var target = IntPtr.Add(destination, y * rowBytes);
                for (var x = 0; x < rowBytes; x += 4)
                {
                    var blue = Marshal.ReadByte(source, x);
                    var green = Marshal.ReadByte(source, x + 1);
                    var red = Marshal.ReadByte(source, x + 2);
                    var alpha = Marshal.ReadByte(source, x + 3);
                    if (alpha == 0)
                    {
                        Marshal.WriteByte(target, x, 0);
                        Marshal.WriteByte(target, x + 1, 0);
                        Marshal.WriteByte(target, x + 2, 0);
                        Marshal.WriteByte(target, x + 3, 0);
                        continue;
                    }

                    Marshal.WriteByte(target, x, (byte)(blue * alpha / 255));
                    Marshal.WriteByte(target, x + 1, (byte)(green * alpha / 255));
                    Marshal.WriteByte(target, x + 2, (byte)(red * alpha / 255));
                    Marshal.WriteByte(target, x + 3, alpha);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
