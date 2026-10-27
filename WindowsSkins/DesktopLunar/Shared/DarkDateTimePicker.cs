using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Shared;

internal sealed class DarkDateTimePicker : DateTimePicker
{
	[DllImport("uxtheme.dll")]
	private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

	public DarkDateTimePicker()
	{
		ApplyColors();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			SetWindowTheme(((Control)this).Handle, "", "");
		}
		catch
		{
		}
		ApplyColors();
	}

	private void ApplyColors()
	{
		((Control)this).BackColor = PopupPanel.PanelBlack;
		((Control)this).ForeColor = Theme.Text;
	}
}
