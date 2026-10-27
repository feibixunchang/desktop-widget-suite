using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Shared;

internal static class LayeredSurface
{
	private struct POINT
	{
		public int X;

		public int Y;
	}

	private struct SIZE
	{
		public int Width;

		public int Height;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct BLENDFUNCTION
	{
		public byte BlendOp;

		public byte BlendFlags;

		public byte SourceConstantAlpha;

		public byte AlphaFormat;
	}

	private struct BITMAPINFO
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

	public const int WsExLayered = 524288;

	private const int UlwAlpha = 2;

	private const byte AcSrcOver = 0;

	private const byte AcSrcAlpha = 1;

	private const uint DibRgbColors = 0u;

	private const uint BiRgb = 0u;

	[DllImport("user32.dll")]
	private static extern IntPtr GetDC(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteDC(IntPtr hDC);

	[DllImport("gdi32.dll")]
	private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr hObject);

	[DllImport("gdi32.dll")]
	private static extern IntPtr CreateDIBSection(IntPtr hDC, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);

	[DllImport("user32.dll")]
	private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION blend, int flags);

	public static void Update(IntPtr handle, Bitmap bitmap, Point location)
	{
		if (handle == IntPtr.Zero || bitmap == null)
		{
			return;
		}
		IntPtr dC = GetDC(IntPtr.Zero);
		if (dC == IntPtr.Zero)
		{
			return;
		}
		IntPtr intPtr = IntPtr.Zero;
		IntPtr intPtr2 = IntPtr.Zero;
		IntPtr intPtr3 = IntPtr.Zero;
		try
		{
			intPtr = CreateCompatibleDC(dC);
			if (intPtr == IntPtr.Zero)
			{
				return;
			}
			BITMAPINFO bITMAPINFO = default(BITMAPINFO);
			bITMAPINFO.Size = Marshal.SizeOf(typeof(BITMAPINFO));
			bITMAPINFO.Width = bitmap.Width;
			bITMAPINFO.Height = -bitmap.Height;
			bITMAPINFO.Planes = 1;
			bITMAPINFO.BitCount = 32;
			bITMAPINFO.Compression = 0u;
			BITMAPINFO info = bITMAPINFO;
			intPtr2 = CreateDIBSection(dC, ref info, 0u, out var bits, IntPtr.Zero, 0u);
			if (!(intPtr2 == IntPtr.Zero) && !(bits == IntPtr.Zero))
			{
				intPtr3 = SelectObject(intPtr, intPtr2);
				if (!(intPtr3 == IntPtr.Zero))
				{
					CopyPremultipliedArgb(bitmap, bits);
					POINT pOINT = default(POINT);
					pOINT.X = location.X;
					pOINT.Y = location.Y;
					POINT pptDst = pOINT;
					SIZE sIZE = default(SIZE);
					sIZE.Width = bitmap.Width;
					sIZE.Height = bitmap.Height;
					SIZE psize = sIZE;
					pOINT = default(POINT);
					pOINT.X = 0;
					pOINT.Y = 0;
					POINT pptSrc = pOINT;
					BLENDFUNCTION bLENDFUNCTION = default(BLENDFUNCTION);
					bLENDFUNCTION.BlendOp = 0;
					bLENDFUNCTION.BlendFlags = 0;
					bLENDFUNCTION.SourceConstantAlpha = byte.MaxValue;
					bLENDFUNCTION.AlphaFormat = 1;
					BLENDFUNCTION blend = bLENDFUNCTION;
					UpdateLayeredWindow(handle, dC, ref pptDst, ref psize, intPtr, ref pptSrc, 0, ref blend, 2);
				}
			}
		}
		catch
		{
		}
		finally
		{
			if (intPtr3 != IntPtr.Zero)
			{
				SelectObject(intPtr, intPtr3);
			}
			if (intPtr2 != IntPtr.Zero)
			{
				DeleteObject(intPtr2);
			}
			if (intPtr != IntPtr.Zero)
			{
				DeleteDC(intPtr);
			}
			ReleaseDC(IntPtr.Zero, dC);
		}
	}

	private static void CopyPremultipliedArgb(Bitmap bitmap, IntPtr destination)
	{
		Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
		BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			int num = bitmap.Width * 4;
			byte[] array = new byte[num];
			for (int i = 0; i < bitmap.Height; i++)
			{
				IntPtr source = IntPtr.Add(bitmapData.Scan0, i * bitmapData.Stride);
				IntPtr destination2 = IntPtr.Add(destination, i * num);
				Marshal.Copy(source, array, 0, num);
				bool flag = true;
				for (int j = 3; j < num; j += 4)
				{
					if (array[j] != byte.MaxValue)
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					Marshal.Copy(array, 0, destination2, num);
					continue;
				}
				for (int k = 0; k < num; k += 4)
				{
					int num2 = array[k + 3];
					switch (num2)
					{
					case 0:
						array[k] = 0;
						array[k + 1] = 0;
						array[k + 2] = 0;
						array[k + 3] = 0;
						break;
					default:
						array[k] = (byte)(array[k] * num2 / 255);
						array[k + 1] = (byte)(array[k + 1] * num2 / 255);
						array[k + 2] = (byte)(array[k + 2] * num2 / 255);
						break;
					case 255:
						break;
					}
				}
				Marshal.Copy(array, 0, destination2, num);
			}
		}
		finally
		{
			bitmap.UnlockBits(bitmapData);
		}
	}
}
