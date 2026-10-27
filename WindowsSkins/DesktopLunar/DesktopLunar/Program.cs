using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Shared;

namespace DesktopLunar;

internal static class Program
{
	private const string MutexName = "DesktopLunar.Widget.SingleInstance";

	[DllImport("user32.dll")]
	private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

	[DllImport("user32.dll")]
	private static extern bool SetProcessDPIAware();

	[STAThread]
	private static void Main()
	{
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		EnableDpiAwareness();
		AppPaths.AppId = "DesktopLunar";
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "DesktopLunar.Widget.SingleInstance", out createdNew))
		{
			if (!createdNew)
			{
				MessageBox.Show("桌面阴历挂件已经在运行了。", "桌面阴历", (MessageBoxButtons)0, (MessageBoxIcon)64);
				return;
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run((Form)(object)new LunarClockForm());
		}
	}

	private static void EnableDpiAwareness()
	{
		try
		{
			if (SetProcessDpiAwarenessContext(new IntPtr(-4)))
			{
				return;
			}
		}
		catch
		{
		}
		try
		{
			SetProcessDPIAware();
		}
		catch
		{
		}
	}
}
