using System;
using System.Runtime.InteropServices;

namespace DesktopMonitor;

internal static class NativeMethods
{
	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	public const uint SwpNoMove = 2u;

	public const uint SwpNoSize = 1u;

	public const uint SwpNoActivate = 16u;

	public const uint SwpFrameChanged = 32u;

	public const int SwShow = 5;

	public const int WsChild = 1073741824;

	public const int WsPopup = int.MinValue;

	public const int WsClipSiblings = 67108864;

	private const int GwlStyle = -16;

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(IntPtr window, out RECT rect);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr FindWindow(string className, string windowName);

	[DllImport("user32.dll")]
	public static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int command);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
	private static extern int GetWindowLong32(IntPtr window, int index);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern IntPtr GetWindowLong64(IntPtr window, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
	private static extern int SetWindowLong32(IntPtr window, int index, int value);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLong64(IntPtr window, int index, IntPtr value);

	public static int GetWindowLongStyle(IntPtr window)
	{
		if (IntPtr.Size != 8)
		{
			return GetWindowLong32(window, -16);
		}
		return GetWindowLong64(window, -16).ToInt32();
	}

	public static void SetWindowLongStyle(IntPtr window, int value)
	{
		if (IntPtr.Size == 8)
		{
			SetWindowLong64(window, -16, new IntPtr(value));
		}
		else
		{
			SetWindowLong32(window, -16, value);
		}
	}
}
