using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WidgetHub;

internal sealed class AboutForm : Form
{
	private readonly Config config;

	private readonly Label lblState = new Label
	{
		AutoSize = true,
		ForeColor = Color.FromArgb(100, 116, 139),
		Location = new Point(24, 118)
	};

	private readonly TextBox txtToken = new TextBox
	{
		UseSystemPasswordChar = true,
		Location = new Point(24, 158),
		Width = 300
	};

	private readonly Button btnCheck = new Button
	{
		Text = "检查更新",
		Location = new Point(24, 196),
		Width = 96,
		Height = 30
	};

	private readonly Button btnInstall = new Button
	{
		Text = "下载并安装新版本",
		Location = new Point(128, 196),
		Width = 136,
		Height = 30,
		Enabled = false
	};

	private readonly Button btnRepo = new Button
	{
		Text = "打开发布页",
		Location = new Point(272, 196),
		Width = 96,
		Height = 30
	};

	private readonly ProgressBar bar = new ProgressBar
	{
		Style = (ProgressBarStyle)1,
		Location = new Point(24, 238),
		Width = 344,
		Height = 14,
		Visible = false
	};

	private UpdateInfo? latest;

	public AboutForm(Config config)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Expected O, but got Unknown
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Expected O, but got Unknown
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Expected O, but got Unknown
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected O, but got Unknown
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected O, but got Unknown
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_013b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_014e: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Expected O, but got Unknown
		//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c3: Expected O, but got Unknown
		//IL_01c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Expected O, but got Unknown
		//IL_01e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0201: Unknown result type (might be due to invalid IL or missing references)
		//IL_0211: Expected O, but got Unknown
		//IL_0211: Unknown result type (might be due to invalid IL or missing references)
		//IL_0216: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0235: Unknown result type (might be due to invalid IL or missing references)
		//IL_023c: Unknown result type (might be due to invalid IL or missing references)
		//IL_024c: Expected O, but got Unknown
		//IL_024c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0251: Unknown result type (might be due to invalid IL or missing references)
		//IL_025c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0270: Unknown result type (might be due to invalid IL or missing references)
		//IL_0277: Unknown result type (might be due to invalid IL or missing references)
		//IL_028a: Expected O, but got Unknown
		//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0316: Unknown result type (might be due to invalid IL or missing references)
		//IL_031e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0326: Expected O, but got Unknown
		this.config = config;
		((Control)this).Text = "关于 · 设置";
		((Form)this).ClientSize = new Size(392, 300);
		((Form)this).FormBorderStyle = (FormBorderStyle)3;
		((Form)this).MaximizeBox = false;
		((Form)this).MinimizeBox = false;
		((Form)this).StartPosition = (FormStartPosition)4;
		((Control)this).BackColor = Color.White;
		((Control)this).Font = new Font("Microsoft YaHei", 9f);
		Label val = new Label
		{
			Text = "桌面挂件合集  WidgetHub",
			Font = new Font("Microsoft YaHei", 13f, (FontStyle)1),
			ForeColor = Color.FromArgb(15, 23, 42),
			AutoSize = true,
			Location = new Point(24, 22)
		};
		Label val2 = new Label
		{
			Text = "当前版本：v" + AppInfo.Version + "     发布仓库：" + AppInfo.RepoOwner + "/" + AppInfo.RepoName,
			ForeColor = Color.FromArgb(100, 116, 139),
			AutoSize = true,
			Location = new Point(24, 58)
		};
		Label val3 = new Label
		{
			Text = "更新令牌（可选：仓库是公开的不需要填，只有改回私有仓库时才需要 GitHub PAT）：",
			ForeColor = Color.FromArgb(100, 116, 139),
			AutoSize = true,
			Location = new Point(24, 138)
		};
		((Control)txtToken).Text = config.UpdateToken ?? "";
		((Control)btnCheck).Click += async delegate
		{
			await CheckAsync();
		};
		((Control)btnInstall).Click += async delegate
		{
			await InstallAsync();
		};
		((Control)btnRepo).Click += delegate
		{
			try
			{
				Process.Start(new ProcessStartInfo(latest?.HtmlUrl ?? "https://github.com/feibixunchang/desktop-widget-suite")
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
		};
		Button val4 = new Button
		{
			Text = "关闭",
			Location = new Point(292, 262),
			Width = 76,
			Height = 28,
			DialogResult = (DialogResult)2
		};
		((Control)this).Controls.AddRange((Control[])(object)new Control[10]
		{
			(Control)val,
			(Control)val2,
			(Control)lblState,
			(Control)val3,
			(Control)txtToken,
			(Control)btnCheck,
			(Control)btnInstall,
			(Control)btnRepo,
			(Control)bar,
			(Control)val4
		});
		((Control)lblState).Text = "点击「检查更新」查看是否有新版本。";
	}

	private async Task CheckAsync()
	{
		((Control)btnCheck).Enabled = false;
		((Control)lblState).Text = "正在检查…";
		try
		{
			config.UpdateToken = ((Control)txtToken).Text.Trim();
			config.Save();
			UpdateInfo updateInfo = await UpdateService.CheckAsync(config.UpdateToken);
			if (updateInfo == null)
			{
				((Control)lblState).Text = "检查失败：连不上 GitHub 发布页（公开仓库不需要令牌，请检查网络/代理后重试）";
				return;
			}
			latest = updateInfo;
			if (string.IsNullOrEmpty(updateInfo.TagVersion))
			{
				((Control)lblState).Text = "仓库里还没有 Windows 版发布记录（tag 需带 win- 前缀）。";
				((Control)btnInstall).Enabled = false;
			}
			else if (UpdateService.IsNewer(updateInfo.TagVersion, AppInfo.Version))
			{
				((Control)lblState).Text = "发现新版本 v" + updateInfo.TagVersion + "（当前 v" + AppInfo.Version + "），点「下载并安装新版本」即可升级。";
				((Control)btnInstall).Enabled = !string.IsNullOrEmpty(updateInfo.AssetUrl);
			}
			else
			{
				((Control)lblState).Text = "已经是最新版本（v" + AppInfo.Version + "）。";
				((Control)btnInstall).Enabled = false;
			}
		}
		catch (Exception ex)
		{
			((Control)lblState).Text = "检查出错：" + ex.Message;
		}
		finally
		{
			((Control)btnCheck).Enabled = true;
		}
	}

	private async Task InstallAsync()
	{
		if (latest == null)
		{
			return;
		}
		((Control)btnInstall).Enabled = false;
		((Control)btnCheck).Enabled = false;
		((Control)bar).Visible = true;
		((Control)lblState).Text = "正在下载新版本…";
		try
		{
			int last = -1;
			string text = await UpdateService.DownloadAsync(latest, config.UpdateToken, delegate(int pct)
			{
				if (pct == last)
				{
					return;
				}
				last = pct;
				try
				{
					bar.Value = pct;
				}
				catch
				{
				}
			});
			if (text == null)
			{
				((Control)lblState).Text = "下载失败，请检查网络或令牌权限（Contents: read）。";
				return;
			}
			((Control)lblState).Text = "下载完成，正在启动安装程序…";
			Process.Start(new ProcessStartInfo(text)
			{
				UseShellExecute = true,
				WorkingDirectory = Path.GetDirectoryName(text)
			});
			Application.Exit();
		}
		catch (Exception ex)
		{
			((Control)lblState).Text = "更新失败：" + ex.Message;
		}
		finally
		{
			((Control)btnCheck).Enabled = true;
			((Control)btnInstall).Enabled = true;
		}
	}
}
