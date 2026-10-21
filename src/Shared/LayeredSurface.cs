using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shared
{
    /// <summary>
    /// 真正的"逐像素透明"窗口：把一张 32 位 ARGB 位图交给系统做 alpha 合成。
    ///
    /// 为什么以前做不到：挂件原来用 <c>TransparencyKey</c>（颜色键）。颜色键只认
    /// "像素值恰好等于键色"的那一个颜色，那一个颜色变透明，其它任何颜色都是实心。
    /// 所以 <c>Color.FromArgb(26, 卡片色)</c> 画出来只是"很暗的黑块"，不会透出桌面，
    /// 透明度设置里除了 0% 以外每一档看起来都差不多。
    /// 改成 UpdateLayeredWindow 之后：位图里每个像素自带 alpha，
    /// 背景按设置淡、文字按 100% 不透明画，系统再和桌面合成 ⇒ 每一档都真的有效。
    /// </summary>
    internal static class LayeredSurface
    {
        public const int WsExLayered = 0x00080000;
        private const int UlwAlpha = 0x00000002;
        private const byte AcSrcOver = 0x00;
        private const byte AcSrcAlpha = 0x01;
        private const uint DibRgbColors = 0;
        private const uint BiRgb = 0;

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hDC,
            ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION blend, int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int Width; public int Height; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public int Size;
            public int Width;
            public int Height;      // 负数 = 自上而下
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ColorsUsed;
            public uint ColorsImportant;
        }

        /// <summary>把位图交给系统做 alpha 合成。<paramref name="location"/> 是窗口左上角的屏幕坐标。</summary>
        public static void Update(IntPtr handle, Bitmap bitmap, Point location)
        {
            if (handle == IntPtr.Zero || bitmap == null) { return; }

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) { return; }

            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmapHandle = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                memoryDc = CreateCompatibleDC(screenDc);
                if (memoryDc == IntPtr.Zero) { return; }

                BITMAPINFO info = new BITMAPINFO
                {
                    Size = Marshal.SizeOf(typeof(BITMAPINFO)),
                    Width = bitmap.Width,
                    Height = -bitmap.Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb
                };

                IntPtr bits;
                bitmapHandle = CreateDIBSection(screenDc, ref info, DibRgbColors, out bits, IntPtr.Zero, 0);
                if (bitmapHandle == IntPtr.Zero || bits == IntPtr.Zero) { return; }

                oldBitmap = SelectObject(memoryDc, bitmapHandle);
                if (oldBitmap == IntPtr.Zero) { return; }

                CopyPremultipliedArgb(bitmap, bits);

                POINT destination = new POINT { X = location.X, Y = location.Y };
                SIZE size = new SIZE { Width = bitmap.Width, Height = bitmap.Height };
                POINT source = new POINT { X = 0, Y = 0 };
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = AcSrcOver,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AcSrcAlpha
                };

                UpdateLayeredWindow(handle, screenDc, ref destination, ref size,
                    memoryDc, ref source, 0, ref blend, UlwAlpha);
            }
            catch
            {
                // 画不出来就保持上一次的画面，不能让挂件整个消失
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) { SelectObject(memoryDc, oldBitmap); }
                if (bitmapHandle != IntPtr.Zero) { DeleteObject(bitmapHandle); }
                if (memoryDc != IntPtr.Zero) { DeleteDC(memoryDc); }
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>
        /// ULW_ALPHA 要的是"预乘 alpha"的像素（RGB 已经乘过 A），
        /// 而 GDI+ 画出来的是"直通 alpha"，这里逐像素转换一次。
        /// 用整行 Marshal.Copy 进出，比逐字节 ReadByte/WriteByte 快一个数量级。
        ///
        /// ⚠️ 全屏 3840x2160 时这一步是纯 CPU 瓶颈：830 万像素逐个算一遍（读 alpha、
        /// 判断、再写 RGB），一帧要 40~60ms，用户看到的就是"卡死了"。
        /// 所以先扫一遍这一行的 alpha：整行都是 255（不透明）就整行原样拷过去
        /// （RGB×255/255 就是 RGB，数学上不用动）；全屏时视频区域恰恰整片不透明。
        /// </summary>
        private static void CopyPremultipliedArgb(Bitmap bitmap, IntPtr destination)
        {
            Rectangle all = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bitmap.Width * 4;
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr source = IntPtr.Add(data.Scan0, y * data.Stride);
                    IntPtr target = IntPtr.Add(destination, y * rowBytes);
                    Marshal.Copy(source, row, 0, rowBytes);

                    bool opaque = true;
                    for (int x = 3; x < rowBytes; x += 4)
                    {
                        if (row[x] != 255) { opaque = false; break; }
                    }

                    if (opaque)
                    {
                        Marshal.Copy(row, 0, target, rowBytes);
                        continue;
                    }

                    for (int x = 0; x < rowBytes; x += 4)
                    {
                        int alpha = row[x + 3];
                        if (alpha == 0)
                        {
                            row[x] = 0; row[x + 1] = 0; row[x + 2] = 0; row[x + 3] = 0;
                        }
                        else if (alpha != 255)
                        {
                            row[x] = (byte)(row[x] * alpha / 255);
                            row[x + 1] = (byte)(row[x + 1] * alpha / 255);
                            row[x + 2] = (byte)(row[x + 2] * alpha / 255);
                        }
                    }

                    Marshal.Copy(row, 0, target, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
