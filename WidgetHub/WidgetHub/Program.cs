using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WidgetHub;

public static class Program
{
	[DllImport("user32.dll")]
	private static extern bool SetProcessDPIAware();

	[STAThread]
	public static void Main(string[] args)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Expected O, but got Unknown
		try
		{
			SetProcessDPIAware();
		}
		catch
		{
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(false);
		if (args.Length >= 2 && args[0] == "--shot")
		{
			try
			{
				ManagerForm managerForm = new ManagerForm(headless: true);
				((Form)managerForm).StartPosition = (FormStartPosition)0;
				((Form)managerForm).Location = new Point(0, 0);
				ManagerForm managerForm2 = managerForm;
				((Control)managerForm2).Show();
				Application.DoEvents();
				Thread.Sleep(500);
				Application.DoEvents();
				Bitmap val = new Bitmap(((Control)managerForm2).Width, ((Control)managerForm2).Height);
				try
				{
					((Control)managerForm2).DrawToBitmap(val, new Rectangle(0, 0, ((Control)managerForm2).Width, ((Control)managerForm2).Height));
					((Image)val).Save(args[1], ImageFormat.Png);
					((Form)managerForm2).Close();
					return;
				}
				finally
				{
					((IDisposable)val)?.Dispose();
				}
			}
			catch (Exception ex)
			{
				try
				{
					File.WriteAllText(args[1] + ".err.txt", ex.ToString());
					return;
				}
				catch
				{
					return;
				}
			}
		}
		Application.Run((Form)(object)new ManagerForm());
	}
}
