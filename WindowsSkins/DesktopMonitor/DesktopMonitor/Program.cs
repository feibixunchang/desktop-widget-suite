using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopMonitor;

internal static class Program
{
	[DllImport("user32.dll")]
	private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

	[DllImport("user32.dll")]
	private static extern bool SetProcessDPIAware();

	[STAThread]
	private static void Main()
	{
		EnableDpiAwareness();
		ServicePointManagerShim.EnableModernTls();
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		Application.ThreadException += delegate
		{
		};
		AppDomain.CurrentDomain.UnhandledException += delegate
		{
		};
		Application.Run((Form)(object)new MonitorForm());
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
