using System;
using System.Threading;
using System.Windows.Forms;
using Shared;

namespace RssWidgetApp;

internal static class Program
{
	private static Mutex singleInstance;

	private static EventWaitHandle showRequest;

	[STAThread]
	private static void Main()
	{
		singleInstance = new Mutex(initiallyOwned: true, "Local\\RssWidget.SingleInstance", out var createdNew);
		if (!createdNew)
		{
			try
			{
				using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Local\\RssWidget.Show");
				eventWaitHandle.Set();
				return;
			}
			catch
			{
				return;
			}
		}
		showRequest = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\RssWidget.Show");
		AppPaths.AppId = "RssWidget";
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		RssWidgetForm form = new RssWidgetForm();
		Thread thread = new Thread((ThreadStart)delegate
		{
			while (showRequest.WaitOne(500))
			{
				try
				{
					if (form.IsDisposed)
					{
						break;
					}
					form.Show();
					form.BringToFront();
				}
				catch
				{
				}
			}
		});
		thread.IsBackground = true;
		thread.Start();
		Application.Run(form);
	}
}
