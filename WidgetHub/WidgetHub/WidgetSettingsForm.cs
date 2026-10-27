using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WidgetHub;

internal sealed class WidgetSettingsForm : Form
{
	private readonly WidgetSettings.Profile profile;

	private readonly Dictionary<string, Control> editors = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

	public WidgetSettingsForm(string title, WidgetSettings.Profile profile, Dictionary<string, string> current)
	{
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007f: Expected O, but got Unknown
		//IL_007f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Expected O, but got Unknown
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_034c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0351: Unknown result type (might be due to invalid IL or missing references)
		//IL_0367: Unknown result type (might be due to invalid IL or missing references)
		//IL_0381: Unknown result type (might be due to invalid IL or missing references)
		//IL_0391: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a4: Expected O, but got Unknown
		//IL_03b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e5: Expected O, but got Unknown
		//IL_03e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_040a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0419: Unknown result type (might be due to invalid IL or missing references)
		//IL_0422: Expected O, but got Unknown
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_0218: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_0245: Unknown result type (might be due to invalid IL or missing references)
		//IL_0259: Expected O, but got Unknown
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d1: Expected O, but got Unknown
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Expected O, but got Unknown
		//IL_01e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0202: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Expected O, but got Unknown
		//IL_0136: Expected O, but got Unknown
		this.profile = profile;
		((Control)this).Text = title + " · 设置";
		((Form)this).ClientSize = new Size(430, 420);
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).StartPosition = (FormStartPosition)4;
		((Control)this).BackColor = Color.White;
		((Control)this).Font = new Font("Microsoft YaHei", 9f);
		Panel val = new Panel
		{
			Dock = (DockStyle)5,
			AutoScroll = true,
			Padding = new Padding(18, 14, 18, 8)
		};
		int num = 6;
		foreach (SettingDef def in profile.Defs)
		{
			Control val2;
			if (def.Kind == "bool")
			{
				val2 = (Control)new CheckBox
				{
					Text = def.Label,
					AutoSize = true,
					Location = new Point(18, num),
					Checked = (current.TryGetValue(def.Key, out string value) && (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)))
				};
			}
			else if (def.Kind == "combo")
			{
				ComboBox val3 = new ComboBox
				{
					DropDownStyle = (ComboBoxStyle)2,
					Location = new Point(150, num - 3),
					Width = 230
				};
				System.Windows.Forms.ComboBox.ObjectCollection items = val3.Items;
				object[] options = def.Options;
				items.AddRange(options);
				int val4 = 0;
				if (current.TryGetValue(def.Key, out string value2) && int.TryParse(value2, out var result))
				{
					val4 = result - def.ComboOffset;
				}
				((ListControl)val3).SelectedIndex = Math.Min(Math.Max(val4, 0), def.Options.Length - 1);
				val2 = (Control)val3;
				((Control)val).Controls.Add((Control)new Label
				{
					Text = def.Label,
					Location = new Point(18, num),
					AutoSize = true
				});
			}
			else
			{
				NumericUpDown val5 = new NumericUpDown
				{
					Location = new Point(150, num - 3),
					Width = 100,
					Minimum = def.Min,
					Maximum = def.Max
				};
				if (current.TryGetValue(def.Key, out string value3) && int.TryParse(value3, out var result2))
				{
					val5.Value = Math.Min(Math.Max(result2, def.Min), def.Max);
				}
				val2 = (Control)(object)val5;
				((Control)val).Controls.Add((Control)new Label
				{
					Text = def.Label,
					Location = new Point(18, num),
					AutoSize = true
				});
			}
			editors[def.Key] = val2;
			if (def.Kind != "bool")
			{
				((Control)val).Controls.Add(val2);
			}
			else
			{
				((Control)val).Controls.Add(val2);
			}
			num += ((def.Kind == "bool") ? 30 : 34);
		}
		Label val6 = new Label
		{
			Text = "配置文件：" + profile.ConfigPath,
			ForeColor = Color.FromArgb(148, 163, 184),
			Location = new Point(18, num + 4),
			Size = new Size(396, 34)
		};
		((Control)val).Controls.Add((Control)(object)val6);
		Button val7 = new Button
		{
			Text = "保存并重启挂件",
			Location = new Point(222, 372),
			Size = new Size(120, 30)
		};
		Button val8 = new Button
		{
			Text = "取消",
			Location = new Point(348, 372),
			Size = new Size(64, 30),
			DialogResult = (DialogResult)2
		};
		((Control)val7).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		};
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)val7);
		((Control)this).Controls.Add((Control)(object)val8);
	}

	public Dictionary<string, string> Values()
	{
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (SettingDef def in profile.Defs)
		{
			if (editors.TryGetValue(def.Key, out Control value))
			{
				if (def.Kind == "bool")
				{
					dictionary[def.Key] = (((CheckBox)value).Checked ? "1" : "0");
				}
				else if (def.Kind == "combo")
				{
					int selectedIndex = ((ListControl)(ComboBox)value).SelectedIndex;
					dictionary[def.Key] = (selectedIndex + def.ComboOffset).ToString();
				}
				else
				{
					dictionary[def.Key] = ((NumericUpDown)value).Value.ToString();
				}
			}
		}
		return dictionary;
	}
}
