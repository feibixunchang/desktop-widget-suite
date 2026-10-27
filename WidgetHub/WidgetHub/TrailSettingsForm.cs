using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WidgetHub;

internal sealed class TrailSettingsForm : Form
{
	private readonly ComboBox cEffect = new ComboBox
	{
		DropDownStyle = (ComboBoxStyle)2
	};

	private readonly ComboBox cPalette = new ComboBox
	{
		DropDownStyle = (ComboBoxStyle)2
	};

	private readonly ComboBox cPerf = new ComboBox
	{
		DropDownStyle = (ComboBoxStyle)2
	};

	public int EffectIndex { get; private set; }

	public int PaletteIndex { get; private set; }

	public int PerfIndex { get; private set; }

	public TrailSettingsForm(Dictionary<string, string> map)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Expected O, but got Unknown
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Expected O, but got Unknown
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0197: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b9: Expected O, but got Unknown
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e0: Expected O, but got Unknown
		//IL_01e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0207: Expected O, but got Unknown
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_024e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0263: Unknown result type (might be due to invalid IL or missing references)
		//IL_0277: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_029d: Expected O, but got Unknown
		//IL_029d: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d3: Expected O, but got Unknown
		//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0307: Unknown result type (might be due to invalid IL or missing references)
		//IL_0310: Expected O, but got Unknown
		((Control)this).Text = "鼠标拖尾 · 特效设置";
		((Form)this).ClientSize = new Size(360, 236);
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).StartPosition = (FormStartPosition)4;
		((Control)this).BackColor = Color.White;
		((Control)this).Font = new Font("Microsoft YaHei", 9f);
		System.Windows.Forms.ComboBox.ObjectCollection items = cEffect.Items;
		object[] effects = TrailConfig.Effects;
		items.AddRange(effects);
		System.Windows.Forms.ComboBox.ObjectCollection items2 = cPalette.Items;
		effects = TrailConfig.Palettes;
		items2.AddRange(effects);
		System.Windows.Forms.ComboBox.ObjectCollection items3 = cPerf.Items;
		effects = TrailConfig.PerfModes;
		items3.AddRange(effects);
		int val = Math.Max(1, TrailConfig.GetInt(map, "Effect", 1)) - 1;
		EffectIndex = Math.Min(Math.Max(val, 0), TrailConfig.Effects.Length - 1);
		PaletteIndex = Math.Min(Math.Max(TrailConfig.GetInt(map, "Palette", 0), 0), TrailConfig.Palettes.Length - 1);
		PerfIndex = Math.Min(Math.Max(TrailConfig.GetInt(map, "PerfMode", 1), 0), TrailConfig.PerfModes.Length - 1);
		((ListControl)cEffect).SelectedIndex = EffectIndex;
		((ListControl)cPalette).SelectedIndex = PaletteIndex;
		((ListControl)cPerf).SelectedIndex = PerfIndex;
		Label val2 = new Label
		{
			Text = "特效",
			Location = new Point(24, 34),
			AutoSize = true
		};
		Label val3 = new Label
		{
			Text = "配色",
			Location = new Point(24, 76),
			AutoSize = true
		};
		Label val4 = new Label
		{
			Text = "性能",
			Location = new Point(24, 118),
			AutoSize = true
		};
		((Control)cEffect).SetBounds(100, 30, 230, 26);
		((Control)cPalette).SetBounds(100, 72, 230, 26);
		((Control)cPerf).SetBounds(100, 114, 230, 26);
		Label val5 = new Label
		{
			Text = "更多参数（密度/大小/亮度/拖尾/变色速度）可直接编辑：\r\n" + TrailConfig.FilePath,
			ForeColor = Color.FromArgb(100, 116, 139),
			Location = new Point(24, 156),
			Size = new Size(312, 34)
		};
		Button val6 = new Button
		{
			Text = "确定并应用",
			Location = new Point(164, 196),
			Size = new Size(90, 30)
		};
		Button val7 = new Button
		{
			Text = "取消",
			Location = new Point(260, 196),
			Size = new Size(76, 30),
			DialogResult = (DialogResult)2
		};
		((Control)val6).Click += delegate
		{
			EffectIndex = Math.Max(((ListControl)cEffect).SelectedIndex, 0);
			PaletteIndex = Math.Max(((ListControl)cPalette).SelectedIndex, 0);
			PerfIndex = Math.Max(((ListControl)cPerf).SelectedIndex, 0);
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		((Control)this).Controls.AddRange((Control[])(object)new Control[9]
		{
			(Control)val2,
			(Control)val3,
			(Control)val4,
			(Control)cEffect,
			(Control)cPalette,
			(Control)cPerf,
			(Control)val5,
			(Control)val6,
			(Control)val7
		});
	}
}
