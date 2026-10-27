using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WidgetHub;

public static class Win32
{
	public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	public const int GWL_EXSTYLE = -20;

	public const int WS_EX_LAYERED = 524288;

	public const int WS_EX_TOPMOST = 8;

	public const int WS_EX_TRANSPARENT = 32;

	public const uint RDW_INVALIDATE = 1u;

	public const uint RDW_ERASE = 4u;

	public const uint RDW_ALLCHILDREN = 128u;

	public const uint RDW_UPDATENOW = 256u;

	public const uint RDW_FRAME = 1024u;

	public const uint SWP_NOSIZE = 1u;

	public const uint SWP_NOMOVE = 2u;

	public const uint SWP_NOZORDER = 4u;

	public const uint LWA_ALPHA = 2u;

	public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

	public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

	public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

	public const int SW_SHOWMINNOACTIVE = 7;

	[DllImport("user32.dll")]
	public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lRect, IntPtr rgn, uint flags);

	[DllImport("user32.dll")]
	public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

	[DllImport("user32.dll")]
	public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	public static void MinimizeWindow(IntPtr hWnd)
	{
		if (hWnd == IntPtr.Zero)
		{
			return;
		}
		try
		{
			ShowWindow(hWnd, 7);
		}
		catch
		{
		}
	}

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

	[DllImport("user32.dll")]
	public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

	[DllImport("user32.dll")]
	public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool IsIconic(IntPtr hWnd);

	public static IntPtr FindMainWindow(uint pid)
	{
		IntPtr best = IntPtr.Zero;
		long bestArea = -1L;
		List<(int W, int H)> screenSizes = new List<(int, int)>();
		try
		{
			Screen[] allScreens = Screen.AllScreens;
			foreach (Screen val in allScreens)
			{
				screenSizes.Add((val.Bounds.Width, val.Bounds.Height));
			}
		}
		catch
		{
		}
		EnumWindows(delegate(IntPtr hWnd, IntPtr _)
		{
			GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
			if (lpdwProcessId != pid)
			{
				return true;
			}
			if (!IsWindowVisible(hWnd))
			{
				return true;
			}
			GetWindowRect(hWnd, out var lpRect);
			int num = lpRect.Right - lpRect.Left;
			int num2 = lpRect.Bottom - lpRect.Top;
			if (num <= 0 || num2 <= 0)
			{
				return true;
			}
			foreach (var (num3, num4) in screenSizes)
			{
				if (num >= num3 * 9 / 10 && num2 >= num4 * 9 / 10)
				{
					return true;
				}
			}
			long num5 = (long)num * (long)num2;
			if (num5 > bestArea)
			{
				bestArea = num5;
				best = hWnd;
			}
			return true;
		}, IntPtr.Zero);
		return best;
	}

	public static void ApplyLayout(Skin s, IntPtr hWnd)
	{
		if (hWnd == IntPtr.Zero)
		{
			return;
		}
		GetWindowRect(hWnd, out var lpRect);
		bool flag = false;
		if (s.TopMost)
		{
			SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, 3u);
		}
		else
		{
			int windowLong = GetWindowLong(hWnd, -20);
			if (((uint)windowLong & 8u) != 0)
			{
				SetWindowLong(hWnd, -20, windowLong & -9);
			}
			SetWindowPos(hWnd, HWND_BOTTOM, 0, 0, 0, 0, 3u);
		}
		int num = GetWindowLong(hWnd, -20);
		if (s.Opacity < 255 || s.ClickThrough)
		{
			num |= 0x80000;
		}
		if (s.ClickThrough)
		{
			num |= 0x20;
		}
		SetWindowLong(hWnd, -20, num);
		if (s.Opacity < 255)
		{
			SetLayeredWindowAttributes(hWnd, 0u, (byte)Net.Clamp255(s.Opacity), 2u);
		}
		if (!s.Positionless && s.X >= 0 && s.Y >= 0)
		{
			GetWindowRect(hWnd, out var lpRect2);
			int num2 = lpRect2.Right - lpRect2.Left;
			int num3 = lpRect2.Bottom - lpRect2.Top;
			if (num2 > 0 && num3 > 0)
			{
				flag = MoveWindow(hWnd, s.X, s.Y, num2, num3, bRepaint: true);
			}
			RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero, 1413u);
		}
		GetWindowRect(hWnd, out var lpRect3);
		Log.W($"  ApplyLayout {s.Name} hwnd={hWnd} target={s.X},{s.Y} ok={flag} " + $"before={lpRect.Left},{lpRect.Top} after={lpRect3.Left},{lpRect3.Top}");
	}
}
