using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace DesktopMonitor;

internal sealed class CitySearchDialog : Form
{
	private const int HorizontalPadding = 14;

	private const int VerticalPadding = 12;

	private readonly TextBox keywordBox;

	private readonly Button searchButton;

	private readonly Button confirmButton;

	private readonly Button cancelButton;

	private readonly ListBox resultList;

	private readonly Label statusLabel;

	private readonly List<CityInfo> results = new List<CityInfo>();

	private bool searching;

	public CityInfo SelectedCity { get; private set; }

	public CitySearchDialog(string currentCity)
	{
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Expected O, but got Unknown
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0124: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0153: Expected O, but got Unknown
		//IL_0153: Unknown result type (might be due to invalid IL or missing references)
		//IL_0158: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_016d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Expected O, but got Unknown
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0250: Unknown result type (might be due to invalid IL or missing references)
		//IL_0257: Unknown result type (might be due to invalid IL or missing references)
		//IL_0265: Unknown result type (might be due to invalid IL or missing references)
		//IL_027a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0285: Expected O, but got Unknown
		//IL_0286: Unknown result type (might be due to invalid IL or missing references)
		//IL_028b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0292: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_02bb: Expected O, but got Unknown
		//IL_0328: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0334: Unknown result type (might be due to invalid IL or missing references)
		//IL_033b: Unknown result type (might be due to invalid IL or missing references)
		//IL_034f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0360: Unknown result type (might be due to invalid IL or missing references)
		//IL_0375: Unknown result type (might be due to invalid IL or missing references)
		//IL_037f: Unknown result type (might be due to invalid IL or missing references)
		//IL_038b: Expected O, but got Unknown
		//IL_038b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0390: Unknown result type (might be due to invalid IL or missing references)
		//IL_0397: Unknown result type (might be due to invalid IL or missing references)
		//IL_039e: Unknown result type (might be due to invalid IL or missing references)
		//IL_03aa: Expected O, but got Unknown
		float num = DisplayScale();
		((Control)this).Text = "更换城市";
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).ShowInTaskbar = false;
		((Form)this).ClientSize = new Size((int)(560f * num), (int)(540f * num));
		((Control)this).MinimumSize = new Size((int)(480f * num), (int)(460f * num));
		((Control)this).Font = Fonts.Menu(12f);
		((Control)this).BackColor = Color.White;
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)0;
		resultList = new ListBox
		{
			IntegralHeight = false,
			BorderStyle = (BorderStyle)1,
			Dock = (DockStyle)5
		};
		resultList.SelectedIndexChanged += delegate
		{
			((Control)confirmButton).Enabled = ((ListControl)resultList).SelectedIndex >= 0;
		};
		((Control)resultList).DoubleClick += delegate
		{
			if (((ListControl)resultList).SelectedIndex >= 0)
			{
				AcceptSelection();
			}
		};
		statusLabel = new Label
		{
			AutoSize = true,
			Dock = (DockStyle)2,
			MaximumSize = new Size((int)(520f * num), 0),
			ForeColor = Color.FromArgb(90, 90, 90),
			Padding = new Padding(14, (int)(6f * num), 14, (int)(6f * num)),
			Text = "已载入常用城市，可直接选择，或输入关键字搜索全国城市。"
		};
		Panel val = new Panel
		{
			Dock = (DockStyle)2,
			Height = (int)(56f * num),
			Padding = new Padding(14, (int)(8f * num), 14, (int)(8f * num))
		};
		confirmButton = CreateButton("确定", num);
		((Control)confirmButton).Enabled = false;
		((Control)confirmButton).Click += delegate
		{
			AcceptSelection();
		};
		((Control)confirmButton).Location = new Point((int)(340f * num), (int)(10f * num));
		cancelButton = CreateButton("取消", num);
		((Control)cancelButton).Location = new Point((int)(440f * num), (int)(10f * num));
		((Control)cancelButton).Click += delegate
		{
			((Form)this).DialogResult = (DialogResult)2;
			((Form)this).Close();
		};
		((Control)val).Controls.Add((Control)(object)confirmButton);
		((Control)val).Controls.Add((Control)(object)cancelButton);
		Panel val2 = new Panel
		{
			Dock = (DockStyle)1,
			Height = (int)(44f * num),
			Padding = new Padding(14, (int)(6f * num), 14, (int)(6f * num))
		};
		keywordBox = new TextBox
		{
			Dock = (DockStyle)5,
			Margin = new Padding(0, (int)(4f * num), (int)(8f * num), (int)(4f * num))
		};
		((Control)keywordBox).TextChanged += delegate
		{
			((Control)confirmButton).Enabled = false;
		};
		searchButton = CreateButton("搜索", num);
		((Control)searchButton).Dock = (DockStyle)4;
		((Control)searchButton).Click += delegate
		{
			StartSearch();
		};
		((Control)val2).Controls.Add((Control)(object)keywordBox);
		((Control)val2).Controls.Add((Control)(object)searchButton);
		Label val3 = new Label
		{
			AutoSize = true,
			Dock = (DockStyle)1,
			MaximumSize = new Size((int)(520f * num), 0),
			ForeColor = Color.FromArgb(50, 50, 50),
			Padding = new Padding(14, (int)(8f * num), 14, (int)(8f * num)),
			Text = "输入城市名称，点“搜索”或直接回车，可搜索全国城市"
		};
		Panel val4 = new Panel
		{
			Dock = (DockStyle)5,
			Padding = new Padding(14, 0, 14, 0)
		};
		((Control)val4).Controls.Add((Control)(object)resultList);
		((Control)this).Controls.Add((Control)(object)val4);
		((Control)this).Controls.Add((Control)(object)val);
		((Control)this).Controls.Add((Control)(object)statusLabel);
		((Control)this).Controls.Add((Control)(object)val2);
		((Control)this).Controls.Add((Control)(object)val3);
		((Form)this).AcceptButton = (IButtonControl)(object)confirmButton;
		((Form)this).CancelButton = (IButtonControl)(object)cancelButton;
		LoadQuickCities();
		((Form)this).Shown += delegate
		{
			((Control)keywordBox).Focus();
		};
	}

	private static Button CreateButton(string text, float scale)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Expected O, but got Unknown
		return new Button
		{
			Text = text,
			AutoSize = true,
			AutoSizeMode = (AutoSizeMode)0,
			MinimumSize = new Size((int)(84f * scale), (int)(32f * scale)),
			UseVisualStyleBackColor = true
		};
	}

	private static float DisplayScale()
	{
		try
		{
			Graphics val = Graphics.FromHwnd(IntPtr.Zero);
			try
			{
				float num = val.DpiX / 96f;
				return (num > 0.5f && num < 4f) ? num : 1f;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch
		{
			return 1f;
		}
	}

	private void LoadQuickCities()
	{
		results.Clear();
		resultList.BeginUpdate();
		string[] quickCityNames = CityCatalog.QuickCityNames;
		for (int i = 0; i < quickCityNames.Length; i++)
		{
			CityInfo cityInfo = CityCatalog.Resolve(quickCityNames[i]);
			if (cityInfo != null)
			{
				results.Add(cityInfo);
				resultList.Items.Add((object)cityInfo.DetailText);
			}
		}
		resultList.EndUpdate();
		((Control)statusLabel).Text = "已载入 " + results.Count + " 个常用城市；输入关键字可搜索全国 " + PlaceCatalog.Count + " 个地名。";
	}

	private void StartSearch()
	{
		string keyword = ((Control)keywordBox).Text.Trim();
		if (keyword.Length == 0 || searching)
		{
			return;
		}
		searching = true;
		((Control)searchButton).Enabled = false;
		((Control)confirmButton).Enabled = false;
		List<CityInfo> local = WeatherService.SearchLocal(keyword, 40);
		if (local.Count > 0)
		{
			ShowResults(keyword, local, "本地地名库找到 " + local.Count + " 个结果");
		}
		else
		{
			((Control)statusLabel).Text = "正在搜索“" + keyword + "”…";
		}
		ThreadPool.QueueUserWorkItem(delegate
		{
			//IL_0047: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Expected O, but got Unknown
			List<CityInfo> online = WeatherService.SearchOnline(keyword, 20);
			try
			{
				if (((Control)this).IsHandleCreated && !((Control)this).IsDisposed)
				{
					((Control)this).BeginInvoke((Delegate)(MethodInvoker)delegate
					{
						MergeOnlineResults(keyword, local, online);
					});
				}
			}
			catch
			{
			}
		});
	}

	private void ShowResults(string keyword, List<CityInfo> found, string status)
	{
		results.Clear();
		resultList.BeginUpdate();
		resultList.Items.Clear();
		AppendResults(found);
		resultList.EndUpdate();
		if (found.Count > 0)
		{
			((ListControl)resultList).SelectedIndex = 0;
		}
		((Control)statusLabel).Text = ((found.Count > 0) ? (status + "，请选择后点“确定”。") : status);
	}

	private void MergeOnlineResults(string keyword, List<CityInfo> local, List<CityInfo> online)
	{
		searching = false;
		((Control)searchButton).Enabled = true;
		int num = 0;
		if (online.Count > 0)
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (CityInfo result in results)
			{
				hashSet.Add(result.Admin1 + "|" + result.Name);
			}
			resultList.BeginUpdate();
			foreach (CityInfo item in online)
			{
				if ((!(item.CountryCode != "CN") || item.CountryCode.Length <= 0) && hashSet.Add(item.Admin1 + "|" + item.Name))
				{
					results.Add(item);
					resultList.Items.Add((object)item.DetailText);
					num++;
				}
			}
			resultList.EndUpdate();
		}
		if (results.Count == 0)
		{
			((Control)statusLabel).Text = "没有找到“" + keyword + "”，请换一个城市名（例如：北京、北京、上海、北京）。";
		}
		else if (num > 0)
		{
			((Control)statusLabel).Text = "共 " + results.Count + " 个结果（已补充联网结果），请选择后点“确定”。";
		}
		else if (local.Count == 0)
		{
			((Control)statusLabel).Text = "找到 " + results.Count + " 个结果，请选择后点“确定”。";
		}
	}

	private void AppendResults(List<CityInfo> found)
	{
		foreach (CityInfo item in found)
		{
			results.Add(item);
			resultList.Items.Add((object)item.DetailText);
		}
	}

	private void AcceptSelection()
	{
		int selectedIndex = ((ListControl)resultList).SelectedIndex;
		if (selectedIndex >= 0 && selectedIndex < results.Count)
		{
			SelectedCity = results[selectedIndex];
			((Form)this).DialogResult = (DialogResult)1;
			((Form)this).Close();
		}
	}
}
