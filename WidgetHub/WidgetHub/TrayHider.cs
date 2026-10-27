using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WidgetHub;

public static class TrayHider
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct TBBUTTONINFO
	{
		public int cbSize;

		public int dwMask;

		public int idCommand;

		public int fsState;

		public int fsStyle;

		public short cx;

		public IntPtr lpszText;

		public int cchText;
	}

	private const uint TB_BUTTONCOUNT = 1048u;

	private const uint TB_GETBUTTON = 1047u;

	private const uint TB_SETBUTTONINFO = 1103u;

	private const int TBSTATE_HIDDEN = 8;

	private const int TBIF_STATE = 1;

	private const uint MEM_COMMIT = 4096u;

	private const uint PAGE_READWRITE = 4u;

	private const uint MEM_RELEASE = 32768u;

	private const uint PROCESS_VM_READ = 16u;

	private const uint PROCESS_VM_OPERATION = 8u;

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpClassName, string? lpWindowName);

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref TBBUTTONINFO lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll")]
	private static extern IntPtr OpenProcess(uint dwAccess, bool bInherit, uint dwProcessId);

	[DllImport("kernel32.dll")]
	private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint flAllocationType, uint flProtect);

	[DllImport("kernel32.dll")]
	private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint dwFreeType);

	[DllImport("kernel32.dll")]
	private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr hObject);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hWnd);

	private static List<IntPtr> GetTrayToolbars()
	{
		List<IntPtr> list = new List<IntPtr>();
		IntPtr intPtr = FindWindow("Shell_TrayWnd", null);
		if (intPtr != IntPtr.Zero)
		{
			IntPtr intPtr2 = FindWindowEx(intPtr, IntPtr.Zero, "TrayNotifyWnd", null);
			if (intPtr2 != IntPtr.Zero)
			{
				IntPtr intPtr3 = FindWindowEx(intPtr2, IntPtr.Zero, "SysPager", null);
				IntPtr intPtr4 = ((intPtr3 != IntPtr.Zero) ? FindWindowEx(intPtr3, IntPtr.Zero, "ToolbarWindow32", null) : FindWindowEx(intPtr2, IntPtr.Zero, "ToolbarWindow32", null));
				if (intPtr4 != IntPtr.Zero)
				{
					list.Add(intPtr4);
				}
			}
		}
		IntPtr intPtr5 = FindWindow("NotifyIconOverflowWindow", null);
		if (intPtr5 != IntPtr.Zero)
		{
			IntPtr intPtr6 = FindWindowEx(intPtr5, IntPtr.Zero, "ToolbarWindow32", null);
			if (intPtr6 != IntPtr.Zero)
			{
				list.Add(intPtr6);
			}
		}
		IntPtr intPtr7 = FindWindow("Shell_SecondaryTrayWnd", null);
		if (intPtr7 != IntPtr.Zero)
		{
			IntPtr intPtr8 = FindWindowEx(intPtr7, IntPtr.Zero, "ToolbarWindow32", null);
			if (intPtr8 != IntPtr.Zero)
			{
				list.Add(intPtr8);
			}
		}
		return list;
	}

	public static void HideIconsForProcesses(HashSet<uint> pids)
	{
		if (pids.Count == 0)
		{
			return;
		}
		try
		{
			foreach (IntPtr trayToolbar in GetTrayToolbars())
			{
				int num = (int)SendMessage(trayToolbar, 1048u, IntPtr.Zero, IntPtr.Zero);
				if (num <= 0)
				{
					continue;
				}
				GetWindowThreadProcessId(trayToolbar, out var lpdwProcessId);
				IntPtr intPtr = OpenProcess(24u, bInherit: false, lpdwProcessId);
				if (intPtr == IntPtr.Zero)
				{
					continue;
				}
				try
				{
					int num2 = 64;
					IntPtr intPtr2 = VirtualAllocEx(intPtr, IntPtr.Zero, num2, 4096u, 4u);
					if (intPtr2 == IntPtr.Zero)
					{
						continue;
					}
					try
					{
						for (int i = 0; i < num; i++)
						{
							SendMessage(trayToolbar, 1047u, (IntPtr)i, intPtr2);
							byte[] array = new byte[num2];
							if (!ReadProcessMemory(intPtr, intPtr2, array, num2, out var lpNumberOfBytesRead))
							{
								continue;
							}
							int num3 = BitConverter.ToInt32(array, 4);
							uint num4 = BitConverter.ToUInt32(array, 12);
							if (num4 == 0)
							{
								continue;
							}
							byte[] array2 = new byte[8];
							if (!ReadProcessMemory(intPtr, (IntPtr)num4, array2, 8, out lpNumberOfBytesRead))
							{
								continue;
							}
							IntPtr intPtr3 = (IntPtr)BitConverter.ToInt64(array2, 0);
							if (!(intPtr3 == IntPtr.Zero) && IsWindow(intPtr3))
							{
								GetWindowThreadProcessId(intPtr3, out var lpdwProcessId2);
								if (pids.Contains(lpdwProcessId2))
								{
									TBBUTTONINFO tBBUTTONINFO = default(TBBUTTONINFO);
									tBBUTTONINFO.cbSize = Marshal.SizeOf<TBBUTTONINFO>();
									tBBUTTONINFO.dwMask = 1;
									tBBUTTONINFO.idCommand = num3;
									tBBUTTONINFO.fsState = 8;
									TBBUTTONINFO lParam = tBBUTTONINFO;
									SendMessage(trayToolbar, 1103u, (IntPtr)num3, ref lParam);
								}
							}
						}
					}
					finally
					{
						VirtualFreeEx(intPtr, intPtr2, 0, 32768u);
					}
				}
				finally
				{
					CloseHandle(intPtr);
				}
			}
		}
		catch
		{
		}
	}
}
