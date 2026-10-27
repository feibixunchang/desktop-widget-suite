using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Layout;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace WidgetHub;

public class ManagerForm : Form
{
	private static readonly Color CBack = Color.FromArgb(245, 247, 250);

	private static readonly Color CWhite = Color.White;

	private static readonly Color CText = Color.FromArgb(31, 41, 55);

	private static readonly Color CMuted = Color.FromArgb(100, 116, 139);

	private static readonly Color CAccent = Color.FromArgb(59, 130, 246);

	private static readonly Color CAccentD = Color.FromArgb(37, 99, 235);

	private static readonly Color CSecondary = Color.FromArgb(100, 116, 139);

	private static readonly Color CSecondaryD = Color.FromArgb(71, 85, 105);

	private static readonly Color CGreen = Color.FromArgb(22, 163, 74);

	private DataGridView grid;

	private NotifyIcon tray;

	private Config config;

	private CheckBox chkAutoManager;

	private Timer statusTimer;

	private Timer layerTimer;

	private (int Row, int Col) hoverCell = (Row: -1, Col: -1);

	private bool silent;

	private bool _selfSave;

	private FileSystemWatcher? _cfgWatch;

	private Timer? _cfgTimer;

	private static readonly Font CellBtnFont = new Font("Microsoft YaHei", 9.5f);

	private int _fitRows = -1;

	private DateTime _cfgKickAt = DateTime.MinValue;

	private bool _reallyExit;

	public ManagerForm(bool headless = false)
	{
		config = Config.Load();
		BuildUI();
		if (headless)
		{
			RefreshGrid();
			return;
		}
		SetupTray();
		Log.Reset();
		object arg = ((Control)this).DeviceDpi;
		Screen primaryScreen = Screen.PrimaryScreen;
		Log.W($"管理器启动 dpi={arg} 主屏={((primaryScreen != null) ? new Rectangle?(primaryScreen.Bounds) : null)} 屏数={Screen.AllScreens.Length}");
		List<Skin> toStart = new List<Skin>(config.Skins);
		Task.Run(delegate
		{
			foreach (Skin item in toStart)
			{
				if (item.Enabled)
				{
					Launcher.Start(item);
				}
			}
		});
		UpdateStatuses();
		ApplyAutoStartManager();
		StartStatusTimer();
		WatchConfig();
	}

	private void BuildUI()
	{
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Expected O, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Expected O, but got Unknown
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Expected O, but got Unknown
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0127: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Expected O, but got Unknown
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0154: Expected O, but got Unknown
		//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ec: Expected O, but got Unknown
		//IL_02f1: Expected O, but got Unknown
		//IL_0335: Unknown result type (might be due to invalid IL or missing references)
		//IL_033a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0341: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_034f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0356: Unknown result type (might be due to invalid IL or missing references)
		//IL_035d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0368: Unknown result type (might be due to invalid IL or missing references)
		//IL_036f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0376: Unknown result type (might be due to invalid IL or missing references)
		//IL_0383: Unknown result type (might be due to invalid IL or missing references)
		//IL_038a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0392: Unknown result type (might be due to invalid IL or missing references)
		//IL_0399: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b3: Expected O, but got Unknown
		//IL_03b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0402: Unknown result type (might be due to invalid IL or missing references)
		//IL_040c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0416: Unknown result type (might be due to invalid IL or missing references)
		//IL_0427: Unknown result type (might be due to invalid IL or missing references)
		//IL_0431: Expected O, but got Unknown
		//IL_0431: Unknown result type (might be due to invalid IL or missing references)
		//IL_0441: Unknown result type (might be due to invalid IL or missing references)
		//IL_0451: Unknown result type (might be due to invalid IL or missing references)
		//IL_0461: Unknown result type (might be due to invalid IL or missing references)
		//IL_0471: Unknown result type (might be due to invalid IL or missing references)
		//IL_047e: Unknown result type (might be due to invalid IL or missing references)
		//IL_049d: Unknown result type (might be due to invalid IL or missing references)
		//IL_04aa: Expected O, but got Unknown
		//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c1: Expected O, but got Unknown
		//IL_04ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d8: Expected O, but got Unknown
		//IL_04e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ef: Expected O, but got Unknown
		//IL_04fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0506: Expected O, but got Unknown
		//IL_0513: Unknown result type (might be due to invalid IL or missing references)
		//IL_051d: Expected O, but got Unknown
		//IL_052a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0534: Expected O, but got Unknown
		//IL_053f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0544: Unknown result type (might be due to invalid IL or missing references)
		//IL_054f: Unknown result type (might be due to invalid IL or missing references)
		//IL_055a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0561: Unknown result type (might be due to invalid IL or missing references)
		//IL_056c: Unknown result type (might be due to invalid IL or missing references)
		//IL_057c: Expected O, but got Unknown
		//IL_0588: Unknown result type (might be due to invalid IL or missing references)
		//IL_058d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0598: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_05bb: Expected O, but got Unknown
		//IL_05c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_05cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_05d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_05fa: Expected O, but got Unknown
		//IL_0606: Unknown result type (might be due to invalid IL or missing references)
		//IL_060b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0616: Unknown result type (might be due to invalid IL or missing references)
		//IL_0621: Unknown result type (might be due to invalid IL or missing references)
		//IL_062c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0639: Expected O, but got Unknown
		//IL_0645: Unknown result type (might be due to invalid IL or missing references)
		//IL_064a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0655: Unknown result type (might be due to invalid IL or missing references)
		//IL_0660: Unknown result type (might be due to invalid IL or missing references)
		//IL_066b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0678: Expected O, but got Unknown
		//IL_0684: Unknown result type (might be due to invalid IL or missing references)
		//IL_0689: Unknown result type (might be due to invalid IL or missing references)
		//IL_0694: Unknown result type (might be due to invalid IL or missing references)
		//IL_069f: Unknown result type (might be due to invalid IL or missing references)
		//IL_06aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b7: Expected O, but got Unknown
		//IL_06e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_06fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0708: Unknown result type (might be due to invalid IL or missing references)
		//IL_0715: Expected O, but got Unknown
		//IL_0721: Unknown result type (might be due to invalid IL or missing references)
		//IL_0726: Unknown result type (might be due to invalid IL or missing references)
		//IL_0731: Unknown result type (might be due to invalid IL or missing references)
		//IL_073c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0747: Unknown result type (might be due to invalid IL or missing references)
		//IL_0754: Expected O, but got Unknown
		//IL_0760: Unknown result type (might be due to invalid IL or missing references)
		//IL_0765: Unknown result type (might be due to invalid IL or missing references)
		//IL_0770: Unknown result type (might be due to invalid IL or missing references)
		//IL_077b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0782: Unknown result type (might be due to invalid IL or missing references)
		//IL_078d: Unknown result type (might be due to invalid IL or missing references)
		//IL_079a: Expected O, but got Unknown
		((Control)this).Text = "WidgetHub";
		((Form)this).Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		((ContainerControl)this).AutoScaleMode = (AutoScaleMode)2;
		((Control)this).Font = new Font("Microsoft YaHei", 10f);
		((Control)this).Width = 1080;
		((Control)this).Height = 740;
		((Control)this).MinimumSize = new Size(880, 330);
		((Form)this).StartPosition = (FormStartPosition)1;
		((Form)this).FormBorderStyle = (FormBorderStyle)4;
		((Control)this).BackColor = CBack;
		((Control)this).Padding = new Padding(0);
		Panel header = new Panel
		{
			Dock = (DockStyle)1,
			Height = 84,
			BackColor = CAccent
		};
		((Control)header).Paint += (PaintEventHandler)delegate(object _, PaintEventArgs e)
		{
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_004b: Expected O, but got Unknown
			//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
			//IL_0100: Expected O, but got Unknown
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0111: Expected O, but got Unknown
			//IL_0116: Unknown result type (might be due to invalid IL or missing references)
			//IL_011b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0128: Expected O, but got Unknown
			//IL_01c2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01c9: Expected O, but got Unknown
			//IL_020a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0211: Expected O, but got Unknown
			//IL_0225: Unknown result type (might be due to invalid IL or missing references)
			//IL_022c: Expected O, but got Unknown
			Graphics graphics = e.Graphics;
			graphics.SmoothingMode = (SmoothingMode)4;
			graphics.TextRenderingHint = (TextRenderingHint)5;
			LinearGradientBrush val3 = new LinearGradientBrush(((Control)header).ClientRectangle, Color.FromArgb(70, 140, 250), Color.FromArgb(37, 99, 235), 0f);
			try
			{
				graphics.FillRectangle((Brush)(object)val3, ((Control)header).ClientRectangle);
			}
			finally
			{
				((IDisposable)val3)?.Dispose();
			}
			float num = (float)((Control)header).DeviceDpi / 96f;
			int num2 = (int)(40f * num);
			int num3 = (int)(20f * num);
			int y = (((Control)header).Height - num2) / 2;
			try
			{
				Icon val4 = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
				try
				{
					if (val4 != null)
					{
						Bitmap val5 = val4.ToBitmap();
						try
						{
							graphics.DrawImage((Image)(object)val5, new Rectangle(num3, y, num2, num2));
						}
						finally
						{
							((IDisposable)val5)?.Dispose();
						}
					}
				}
				finally
				{
					((IDisposable)val4)?.Dispose();
				}
			}
			catch
			{
			}
			Font val6 = new Font("Microsoft YaHei", 17f, (FontStyle)1);
			try
			{
				Font val7 = new Font("Microsoft YaHei", 9f);
				try
				{
					StringFormat val8 = new StringFormat(StringFormat.GenericTypographic)
					{
						FormatFlags = (StringFormatFlags)4096
					};
					try
					{
						SizeF sizeF = graphics.MeasureString("WidgetHub", val6, PointF.Empty, val8);
						SizeF sizeF2 = graphics.MeasureString("桌面挂件合集 · 一个图标管理全部挂件 · 默认贴桌面层，不遮挡任何程序\0\0\0", val7, PointF.Empty, val8);
						float num4 = (float)(num3 + num2) + 14f * num;
						float num5 = 6f * num;
						float num6 = sizeF.Height + num5 + sizeF2.Height;
						float num7 = ((float)((Control)header).Height - num6) / 2f;
						graphics.DrawString("WidgetHub", val6, Brushes.White, num4, num7, val8);
						SolidBrush val9 = new SolidBrush(Color.FromArgb(226, 236, 255));
						try
						{
							graphics.DrawString("桌面挂件合集 · 一个图标管理全部挂件 · 默认贴桌面层，不遮挡任何程序\0\0\0", val7, (Brush)(object)val9, num4 + 2f * num, num7 + sizeF.Height + num5, val8);
						}
						finally
						{
							((IDisposable)val9)?.Dispose();
						}
						Font val10 = new Font("Microsoft YaHei", 10f, (FontStyle)1);
						try
						{
							SolidBrush val11 = new SolidBrush(Color.FromArgb(226, 236, 255));
							try
							{
								string text2 = "v" + AppInfo.Version;
								SizeF sizeF3 = graphics.MeasureString(text2, val10);
								float num8 = (float)((Control)header).Width - sizeF3.Width - 18f * num;
								graphics.DrawString(text2, val10, Brushes.White, num8, 18f * num, val8);
								string text3 = "点击检查更新";
								SizeF sizeF4 = graphics.MeasureString(text3, val7);
								graphics.DrawString(text3, val7, (Brush)(object)val11, (float)((Control)header).Width - sizeF4.Width - 18f * num, 18f * num + sizeF3.Height + 4f * num, val8);
							}
							finally
							{
								((IDisposable)val11)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)val10)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)val8)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)val7)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)val6)?.Dispose();
			}
		};
		((Control)header).Cursor = Cursors.Hand;
		((Control)header).Click += delegate
		{
			ShowAbout();
		};
		FlowLayoutPanel toolbar = new FlowLayoutPanel
		{
			Dock = (DockStyle)1,
			Height = 58,
			BackColor = CWhite,
			WrapContents = false,
			Padding = new Padding(18, 12, 18, 10),
			FlowDirection = (FlowDirection)0
		};
		((Control)toolbar).Paint += (PaintEventHandler)delegate(object _, PaintEventArgs e)
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Expected O, but got Unknown
			Pen val2 = new Pen(Color.FromArgb(228, 232, 238));
			try
			{
				e.Graphics.DrawLine(val2, 0, ((Control)toolbar).Height - 1, ((Control)toolbar).Width, ((Control)toolbar).Height - 1);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		};
		((Control)toolbar).Controls.Add((Control)(object)B("全部启动", delegate
		{
			foreach (Skin skin in config.Skins)
			{
				Launcher.Start(skin);
			}
			UpdateStatuses();
		}, CAccent, CAccentD));
		((Control)toolbar).Controls.Add((Control)(object)B("全部停止", delegate
		{
			foreach (Skin skin2 in config.Skins)
			{
				Launcher.Stop(skin2);
			}
			UpdateStatuses();
		}, Color.FromArgb(239, 68, 68), Color.FromArgb(220, 38, 38)));
		((Control)toolbar).Controls.Add((Control)(object)B("全部归位", delegate
		{
			foreach (Skin skin3 in config.Skins)
			{
				Launcher.Reapply(skin3);
			}
			UpdateStatuses();
		}, Color.FromArgb(16, 185, 129), Color.FromArgb(5, 150, 105)));
		((Control)toolbar).Controls.Add((Control)(object)B("保存配置", delegate
		{
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			SyncAll();
			SaveConfig();
			RefreshGrid();
			MessageBox.Show("配置已保存。", "WidgetHub");
		}, CSecondary, CSecondaryD));
		((Control)toolbar).Controls.Add((Control)(object)B("刷新状态", delegate
		{
			RefreshGrid();
			ClearHighlight();
			UpdateStatuses();
		}, CSecondary, CSecondaryD));
		((Control)toolbar).Controls.Add((Control)(object)B("拖尾特效…", delegate
		{
			ShowTrailSettings();
		}, Color.FromArgb(139, 92, 246), Color.FromArgb(124, 58, 237)));
		chkAutoManager = new CheckBox
		{
			Text = "管理器开机自启",
			AutoSize = true,
			Margin = new Padding(12, 8, 0, 0),
			Font = new Font("Microsoft YaHei", 10f)
		};
		chkAutoManager.Checked = config.AutoStartManager;
		chkAutoManager.CheckedChanged += delegate
		{
			config.AutoStartManager = chkAutoManager.Checked;
			ApplyAutoStartManager();
		};
		((Control)toolbar).Controls.Add((Control)(object)chkAutoManager);
		DataGridView val = new DataGridView
		{
			Dock = (DockStyle)5,
			AllowUserToAddRows = false,
			RowHeadersVisible = false,
			AllowUserToResizeRows = false,
			SelectionMode = (DataGridViewSelectionMode)1,
			BackgroundColor = CWhite,
			BorderStyle = (BorderStyle)0,
			CellBorderStyle = (DataGridViewCellBorderStyle)4
		};
		val.RowTemplate.Height = 40;
		val.ColumnHeadersBorderStyle = (DataGridViewHeaderBorderStyle)4;
		val.ColumnHeadersHeight = 42;
		val.ColumnHeadersHeightSizeMode = (DataGridViewColumnHeadersHeightSizeMode)1;
		val.DefaultCellStyle.Font = new Font("Microsoft YaHei", 10f);
		val.DefaultCellStyle.ForeColor = CText;
		val.DefaultCellStyle.BackColor = CWhite;
		val.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
		val.DefaultCellStyle.SelectionForeColor = CText;
		val.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
		val.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei", 10f, (FontStyle)1);
		val.ColumnHeadersDefaultCellStyle.ForeColor = CMuted;
		val.ColumnHeadersDefaultCellStyle.BackColor = CWhite;
		val.ColumnHeadersDefaultCellStyle.SelectionBackColor = CWhite;
		val.ColumnHeadersDefaultCellStyle.SelectionForeColor = CMuted;
		val.ColumnHeadersDefaultCellStyle.Alignment = (DataGridViewContentAlignment)32;
		val.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252);
		val.AutoSizeColumnsMode = (DataGridViewAutoSizeColumnsMode)16;
		grid = val;
		grid.CellContentClick += new DataGridViewCellEventHandler(Grid_CellContentClick);
		grid.CellValueChanged += new DataGridViewCellEventHandler(Grid_CellValueChanged);
		grid.CellFormatting += new DataGridViewCellFormattingEventHandler(Grid_CellFormatting);
		grid.CellPainting += new DataGridViewCellPaintingEventHandler(Grid_CellPainting);
		grid.CellMouseMove += (DataGridViewCellMouseEventHandler)delegate(object _, DataGridViewCellMouseEventArgs e)
		{
			if (e.RowIndex >= 0 && (grid.Columns[e.ColumnIndex].Name == "Toggle" || grid.Columns[e.ColumnIndex].Name == "Capture" || grid.Columns[e.ColumnIndex].Name == "Settings"))
			{
				(int, int) tuple2 = hoverCell;
				int rowIndex = e.RowIndex;
				int columnIndex = e.ColumnIndex;
				if (tuple2.Item1 != rowIndex || tuple2.Item2 != columnIndex)
				{
					(int, int) tuple3 = hoverCell;
					hoverCell = (Row: e.RowIndex, Col: e.ColumnIndex);
					if (tuple3.Item1 >= 0)
					{
						grid.InvalidateCell(tuple3.Item2, tuple3.Item1);
					}
					grid.InvalidateCell(e.ColumnIndex, e.RowIndex);
				}
			}
		};
		grid.CellMouseLeave += (DataGridViewCellEventHandler)delegate
		{
			if (hoverCell.Row >= 0)
			{
				(int, int) tuple = hoverCell;
				hoverCell = (Row: -1, Col: -1);
				grid.InvalidateCell(tuple.Item2, tuple.Item1);
			}
		};
		grid.Columns.Add((DataGridViewColumn)new DataGridViewTextBoxColumn
		{
			Name = "Name",
			HeaderText = "挂件（皮肤）",
			ReadOnly = true,
			FillWeight = 24f,
			MinimumWidth = 130
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewCheckBoxColumn
		{
			Name = "Enabled",
			HeaderText = "自启",
			FillWeight = 8f,
			MinimumWidth = 58
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewCheckBoxColumn
		{
			Name = "TopMost",
			HeaderText = "置顶",
			FillWeight = 8f,
			MinimumWidth = 58
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewCheckBoxColumn
		{
			Name = "ClickThrough",
			HeaderText = "穿透",
			FillWeight = 8f,
			MinimumWidth = 58
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewTextBoxColumn
		{
			Name = "Opacity",
			HeaderText = "透明度",
			FillWeight = 9f,
			MinimumWidth = 72
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewButtonColumn
		{
			Name = "Toggle",
			HeaderText = "启动 / 停止",
			FillWeight = 12f,
			MinimumWidth = 106
		});
		grid.Columns["Toggle"].ToolTipText = "这一列显示的是当前状态：已启动 / 已停止。点一下可以启停对应的挂件。";
		grid.Columns.Add((DataGridViewColumn)new DataGridViewButtonColumn
		{
			Name = "Capture",
			HeaderText = "位置记忆",
			FillWeight = 12f,
			MinimumWidth = 100
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewButtonColumn
		{
			Name = "Settings",
			HeaderText = "设置",
			FillWeight = 8f,
			MinimumWidth = 76
		});
		grid.Columns.Add((DataGridViewColumn)new DataGridViewTextBoxColumn
		{
			Name = "Status",
			HeaderText = "状态",
			ReadOnly = true,
			FillWeight = 9f,
			MinimumWidth = 72
		});
		((Control)this).Controls.Add((Control)(object)grid);
		((Control)this).Controls.Add((Control)(object)toolbar);
		((Control)this).Controls.Add((Control)(object)header);
		RefreshGrid();
		ClearHighlight();
		((Form)this).Shown += delegate
		{
			ClearHighlight();
			FitHeightToGrid();
		};
		static ModernButton B(string text, Action act, Color normal, Color hover)
		{
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			Action act2 = act;
			ModernButton modernButton = new ModernButton();
			((Control)modernButton).Text = text;
			modernButton.NormalColor = normal;
			modernButton.HoverColor = hover;
			((Control)modernButton).Width = 104;
			((Control)modernButton).Height = 34;
			((Control)modernButton).Margin = new Padding(0, 0, 10, 0);
			((Control)modernButton).Click += delegate
			{
				act2();
			};
			return modernButton;
		}
	}

	private void ClearHighlight()
	{
		grid.ClearSelection();
		try
		{
			grid.CurrentCell = null;
		}
		catch
		{
		}
	}

	private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
	{
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected O, but got Unknown
		if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "Status" && ((DataGridViewBand)grid.Rows[e.RowIndex]).Tag is Skin s)
		{
			e.CellStyle.ForeColor = (Launcher.IsRunning(s) ? CGreen : CMuted);
			e.CellStyle.Font = new Font("Microsoft YaHei", 10f, (FontStyle)1);
		}
	}

	private void RoundedButton(Graphics g, Rectangle bounds, Color fill, string text, bool hover, bool warn = false)
	{
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Expected O, but got Unknown
		Rectangle rectangle = new Rectangle(bounds.X + 9, bounds.Y + 6, bounds.Width - 18, bounds.Height - 12);
		if (rectangle.Width < 20 || rectangle.Height < 10)
		{
			return;
		}
		g.SmoothingMode = (SmoothingMode)4;
		int radius = Math.Min(7, rectangle.Height / 2);
		GraphicsPath val = ModernButton.RoundPath(rectangle, radius);
		try
		{
			SolidBrush val2 = new SolidBrush(hover ? CAccentD : fill);
			try
			{
				g.FillPath((Brush)(object)val2, val);
			}
			finally
			{
				((IDisposable)val2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		Color color = (warn ? Color.FromArgb(255, 244, 214) : Color.White);
		TextRenderer.DrawText((IDeviceContext)(object)g, text, CellBtnFont, rectangle, color, (TextFormatFlags)268468261);
	}

	private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
	{
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Expected O, but got Unknown
		if (e.RowIndex < 0)
		{
			return;
		}
		string name = grid.Columns[e.ColumnIndex].Name;
		if (name != "Toggle" && name != "Capture" && name != "Settings")
		{
			return;
		}
		SolidBrush val = new SolidBrush(((DataGridViewBand)grid.Rows[e.RowIndex]).Selected ? Color.FromArgb(219, 234, 254) : e.CellStyle.BackColor);
		try
		{
			e.Graphics.FillRectangle((Brush)(object)val, e.CellBounds);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
		Skin skin = ((DataGridViewBand)grid.Rows[e.RowIndex]).Tag as Skin;
		(int, int) tuple = hoverCell;
		int rowIndex = e.RowIndex;
		int columnIndex = e.ColumnIndex;
		bool hover = tuple.Item1 == rowIndex && tuple.Item2 == columnIndex;
		if (name == "Settings")
		{
			RoundedButton(e.Graphics, e.CellBounds, Color.FromArgb(139, 92, 246), "设置", hover);
			((HandledEventArgs)(object)e).Handled = true;
			return;
		}
		Color fill;
		string text;
		if (name == "Capture")
		{
			if (skin != null && skin.Positionless)
			{
				fill = Color.FromArgb(148, 163, 184);
				text = "无需记录";
			}
			else if (skin != null && skin.PositionSaved)
			{
				fill = CGreen;
				text = "已记录位置";
			}
			else
			{
				fill = CSecondary;
				text = "记录位置";
			}
		}
		else
		{
			string text2 = e.Value?.ToString() ?? "已停止";
			if (text2.Contains("已启动"))
			{
				fill = CGreen;
				text = text2;
			}
			else
			{
				fill = CSecondary;
				text = text2;
			}
		}
		RoundedButton(e.Graphics, e.CellBounds, fill, text, hover);
		((HandledEventArgs)(object)e).Handled = true;
	}

	private void RefreshGrid()
	{
		silent = true;
		grid.Rows.Clear();
		foreach (Skin skin in config.Skins)
		{
			Process process = Launcher.RunningProcess(skin);
			bool flag = process != null;
			string text = (skin.Positionless ? "无需记录" : (skin.PositionSaved ? "已记录位置" : "记录位置"));
			int num = grid.Rows.Add(new object[8]
			{
				skin.Name,
				skin.Enabled,
				skin.TopMost,
				skin.ClickThrough,
				skin.Opacity,
				flag ? "已启动" : "已停止",
				text,
				StatusText(process)
			});
			((DataGridViewBand)grid.Rows[num]).Tag = skin;
			grid.Rows[num].Cells["Toggle"].ToolTipText = (flag ? "正在运行 —— 点一下停止" : "没有运行 —— 点一下启动");
			grid.Rows[num].Cells["Status"].ToolTipText = StatusTip(process);
		}
		silent = false;
		if (((Control)this).IsHandleCreated && grid.Rows.Count != _fitRows)
		{
			try
			{
				((Control)this).BeginInvoke((Delegate)new Action(FitHeightToGrid));
			}
			catch
			{
				FitHeightToGrid();
			}
		}
	}

	private void SaveConfig()
	{
		_selfSave = true;
		config.Save();
	}

	private void WatchConfig()
	{
		try
		{
			string directoryName = Path.GetDirectoryName(Config.Path_);
			Directory.CreateDirectory(directoryName);
			FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(directoryName)
			{
				Filter = "config.json",
				NotifyFilter = (NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite),
				EnableRaisingEvents = true
			};
			fileSystemWatcher.Changed += delegate
			{
				KickReload();
			};
			fileSystemWatcher.Created += delegate
			{
				KickReload();
			};
			_cfgWatch = fileSystemWatcher;
			Log.W("config.json 监听已启动：" + directoryName);
		}
		catch (Exception ex)
		{
			Log.W("config.json 监听启动失败：" + ex.Message);
		}
	}

	private void KickReload()
	{
		try
		{
			if (!((Control)this).IsHandleCreated)
			{
				return;
			}
			DateTime utcNow = DateTime.UtcNow;
			if (_cfgKickAt != DateTime.MinValue && (utcNow - _cfgKickAt).TotalMilliseconds < 500.0)
			{
				return;
			}
			_cfgKickAt = utcNow;
			((Control)this).BeginInvoke((Delegate)(Action)delegate
			{
				//IL_0009: Unknown result type (might be due to invalid IL or missing references)
				//IL_000e: Unknown result type (might be due to invalid IL or missing references)
				//IL_001e: Expected O, but got Unknown
				if (_cfgTimer == null)
				{
					_cfgTimer = new Timer
					{
						Interval = 400
					};
					_cfgTimer.Tick += delegate
					{
						_cfgTimer.Stop();
						ReloadConfigExternal();
					};
				}
				_cfgTimer.Stop();
				_cfgTimer.Start();
			});
		}
		catch
		{
		}
	}

	private void ReloadConfigExternal()
	{
		try
		{
			if (_selfSave)
			{
				_selfSave = false;
				return;
			}
			Config config = Config.TryLoad();
			if (config != null)
			{
				int count = this.config.Skins.Count;
				this.config = config;
				RefreshGrid();
				UpdateStatuses();
				if (count != this.config.Skins.Count)
				{
					Log.W($"config.json 外部变化：挂件 {count} → {this.config.Skins.Count}，已重载并调整窗口高度");
				}
			}
		}
		catch
		{
		}
	}

	private void FitHeightToGrid()
	{
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		try
		{
			if (grid == null)
			{
				return;
			}
			((Control)grid).PerformLayout();
			if (grid.Rows.Count == 0)
			{
				if (Math.Abs(((Control)this).Height - ((Control)this).MinimumSize.Height) > 2)
				{
					((Control)this).Height = ((Control)this).MinimumSize.Height;
				}
				_fitRows = 0;
				return;
			}
			int num = 0;
			foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
			{
				DataGridViewRow val = item;
				num += val.Height;
			}
			int num2 = HorizontalScrollBarHeight();
			int num3 = grid.ColumnHeadersHeight + num + num2 + 4;
			int num4 = ((Form)this).ClientSize.Height - ((Control)grid).Height;
			if (num4 < 0)
			{
				num4 = 0;
			}
			int num5 = ((Control)this).Height - ((Form)this).ClientSize.Height;
			int num6 = num4 + num3 + num5;
			Rectangle workingArea = (Screen.FromControl((Control)(object)this) ?? Screen.PrimaryScreen).WorkingArea;
			int num7 = Math.Max(((Control)this).MinimumSize.Height, workingArea.Height - 60);
			int num8 = Math.Min(Math.Max(num6, ((Control)this).MinimumSize.Height), num7);
			Log.W($"FitHeight rows={grid.Rows.Count} rowsH={num} headH={grid.ColumnHeadersHeight} " + $"scrollH={num2} need={num3} chrome={num4} border={num5} want={num6} cur={((Control)this).Height} " + $"gridW={((Control)grid).ClientSize.Width} colW={SumColumnWidth()}");
			if (Math.Abs(num8 - ((Control)this).Height) > 2)
			{
				((Control)this).Height = num8;
				((Control)grid).PerformLayout();
				if (((Control)this).Bottom > workingArea.Bottom)
				{
					int num9 = workingArea.Bottom - num8;
					if (num9 < workingArea.Top)
					{
						num9 = workingArea.Top;
					}
					((Control)this).Top = num9;
				}
			}
			for (int i = 0; i < 12; i++)
			{
				int num10;
				try
				{
					num10 = grid.DisplayedRowCount(false);
				}
				catch
				{
					break;
				}
				int num11 = grid.Rows.Count - num10;
				if (num11 <= 0)
				{
					break;
				}
				int num12 = Math.Max(1, num / Math.Max(1, grid.Rows.Count));
				((Control)this).Height = ((Control)this).Height + (num11 * num12 + 8);
				if (((Control)this).Height > num7)
				{
					((Control)this).Height = num7;
				}
				((Control)grid).PerformLayout();
				if (((Control)this).Height >= num7)
				{
					break;
				}
			}
			_fitRows = grid.Rows.Count;
		}
		catch
		{
		}
	}

	private int SumColumnWidth()
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Expected O, but got Unknown
		int num = 0;
		try
		{
			foreach (DataGridViewColumn item in (BaseCollection)grid.Columns)
			{
				DataGridViewColumn val = item;
				num += val.Width;
			}
		}
		catch
		{
		}
		return num;
	}

	private int HorizontalScrollBarHeight()
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected O, but got Unknown
		try
		{
			foreach (Control item in (ArrangedElementCollection)((Control)grid).Controls)
			{
				object obj = (object)item;
				ScrollBar val = (ScrollBar)((obj is ScrollBar) ? obj : null);
				if (val != null && ((object)val).GetType().Name.StartsWith("H") && ((Control)val).Visible)
				{
					return ((Control)val).Height;
				}
			}
		}
		catch
		{
		}
		return 0;
	}

	private void UpdateStatuses()
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Expected O, but got Unknown
		if (grid == null || ((Control)grid).IsDisposed)
		{
			return;
		}
		silent = true;
		try
		{
			foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
			{
				DataGridViewRow val = item;
				if (((DataGridViewBand)val).Tag is Skin skin)
				{
					Process process = Launcher.RunningProcess(skin);
					bool flag = process != null;
					bool flag2 = false;
					string text = StatusText(process);
					if (!object.Equals(val.Cells["Status"].Value as string, text))
					{
						val.Cells["Status"].Value = text;
						flag2 = true;
					}
					val.Cells["Status"].ToolTipText = StatusTip(process);
					string text2 = (flag ? "已启动" : "已停止");
					if (!object.Equals(val.Cells["Toggle"].Value as string, text2))
					{
						val.Cells["Toggle"].Value = text2;
						flag2 = true;
					}
					val.Cells["Toggle"].ToolTipText = (flag ? "正在运行 —— 点一下停止" : "没有运行 —— 点一下启动");
					string text3 = (skin.Positionless ? "无需记录" : (skin.PositionSaved ? "已记录位置" : "记录位置"));
					if (!object.Equals(val.Cells["Capture"].Value as string, text3))
					{
						val.Cells["Capture"].Value = text3;
						flag2 = true;
					}
					if (flag2)
					{
						grid.InvalidateRow(((DataGridViewBand)val).Index);
					}
				}
			}
		}
		finally
		{
			silent = false;
		}
	}

	private static string StatusText(Process? proc)
	{
		if (proc == null)
		{
			return "—";
		}
		try
		{
			TimeSpan timeSpan = DateTime.Now - proc.StartTime;
			if (timeSpan.TotalMinutes < 1.0)
			{
				return "刚启动";
			}
			if (timeSpan.TotalHours < 1.0)
			{
				return (int)timeSpan.TotalMinutes + " 分";
			}
			if (timeSpan.TotalDays < 1.0)
			{
				return (int)timeSpan.TotalHours + " 时";
			}
			return (int)timeSpan.TotalDays + " 天";
		}
		catch
		{
			return "运行中";
		}
	}

	private static string StatusTip(Process? proc)
	{
		if (proc == null)
		{
			return "没有运行";
		}
		try
		{
			TimeSpan timeSpan = DateTime.Now - proc.StartTime;
			return "已运行 " + (int)timeSpan.TotalMinutes + " 分钟（进程号 " + proc.Id + "）";
		}
		catch
		{
			return "运行中";
		}
	}

	private void SyncRow(Skin s, DataGridViewRow row)
	{
		object value = row.Cells["Enabled"].Value;
		bool flag = default(bool);
		int num;
		if (value is bool)
		{
			flag = (bool)value;
			num = 1;
		}
		else
		{
			num = 0;
		}
		s.Enabled = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
		value = row.Cells["TopMost"].Value;
		bool flag2 = default(bool);
		int num2;
		if (value is bool)
		{
			flag2 = (bool)value;
			num2 = 1;
		}
		else
		{
			num2 = 0;
		}
		s.TopMost = (byte)((uint)num2 & (flag2 ? 1u : 0u)) != 0;
		value = row.Cells["ClickThrough"].Value;
		bool flag3 = default(bool);
		int num3;
		if (value is bool)
		{
			flag3 = (bool)value;
			num3 = 1;
		}
		else
		{
			num3 = 0;
		}
		s.ClickThrough = (byte)((uint)num3 & (flag3 ? 1u : 0u)) != 0;
		if (row.Cells["Opacity"].Value != null && int.TryParse(row.Cells["Opacity"].Value.ToString(), out var result))
		{
			s.Opacity = Net.Clamp255(result);
		}
	}

	private void SyncAll()
	{
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Expected O, but got Unknown
		grid.EndEdit();
		foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
		{
			DataGridViewRow val = item;
			if (((DataGridViewBand)val).Tag is Skin s)
			{
				SyncRow(s, val);
			}
		}
	}

	private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
	{
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		if (e.RowIndex < 0 || !(((DataGridViewBand)grid.Rows[e.RowIndex]).Tag is Skin skin))
		{
			return;
		}
		switch (grid.Columns[e.ColumnIndex].Name)
		{
		case "Toggle":
			if (Launcher.IsRunning(skin))
			{
				Launcher.Stop(skin);
			}
			else
			{
				Launcher.Start(skin);
			}
			UpdateStatuses();
			ClearHighlight();
			break;
		case "Settings":
			ShowWidgetSettings(skin);
			ClearHighlight();
			break;
		case "Capture":
		{
			if (skin.Positionless)
			{
				MessageBox.Show("「" + skin.Name + "」是跟随鼠标的全屏特效，没有固定的窗口位置，因此无需记录。", "WidgetHub", (MessageBoxButtons)0, (MessageBoxIcon)64);
				break;
			}
			Process process = Launcher.FindRunning(skin);
			if (process == null)
			{
				MessageBox.Show("「" + skin.Name + "」当前没有运行，请先启动它，再拖动到想要的位置后点「记录位置」。", "WidgetHub", (MessageBoxButtons)0, (MessageBoxIcon)64);
				break;
			}
			IntPtr intPtr = Win32.FindMainWindow((uint)process.Id);
			if (intPtr == IntPtr.Zero || !Win32.GetWindowRect(intPtr, out var lpRect))
			{
				MessageBox.Show("找不到「" + skin.Name + "」的挂件窗口，无法记录位置。", "WidgetHub", (MessageBoxButtons)0, (MessageBoxIcon)48);
				break;
			}
			skin.X = lpRect.Left;
			skin.Y = lpRect.Top;
			skin.Width = lpRect.Right - lpRect.Left;
			skin.Height = lpRect.Bottom - lpRect.Top;
			skin.PositionSaved = true;
			SaveConfig();
			Launcher.Reapply(skin);
			grid.InvalidateRow(e.RowIndex);
			UpdateStatuses();
			ClearHighlight();
			break;
		}
		}
	}

	private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
	{
		if (silent || e.RowIndex < 0)
		{
			return;
		}
		DataGridViewRow val = grid.Rows[e.RowIndex];
		if (!(((DataGridViewBand)val).Tag is Skin s))
		{
			return;
		}
		SyncRow(s, val);
		switch (grid.Columns[e.ColumnIndex].Name)
		{
		case "TopMost":
		case "ClickThrough":
		case "Opacity":
		{
			if (!Launcher.IsRunning(s))
			{
				break;
			}
			Process process = Launcher.FindRunning(s);
			if (process != null)
			{
				IntPtr intPtr = Win32.FindMainWindow((uint)process.Id);
				if (intPtr != IntPtr.Zero)
				{
					Win32.ApplyLayout(s, intPtr);
				}
			}
			break;
		}
		}
		ClearHighlight();
	}

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Invalid comparison between Unknown and I4
		SyncAll();
		SaveConfig();
		if (!_reallyExit && (int)e.CloseReason == 3)
		{
			((CancelEventArgs)(object)e).Cancel = true;
			((Control)this).Hide();
		}
		else
		{
			base.OnFormClosing(e);
		}
	}

	private void SetupTray()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Expected O, but got Unknown
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Expected O, but got Unknown
		tray = new NotifyIcon
		{
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
			Text = "桌面挂件合集 桌面挂件管理器\0\0\0",
			Visible = true
		};
		ContextMenuStrip val = new ContextMenuStrip();
		((ToolStrip)val).Items.Add("打开管理器", (Image)null, (EventHandler)delegate
		{
			ShowManager();
		});
		((ToolStrip)val).Items.Add("全部启动", (Image)null, (EventHandler)delegate
		{
			foreach (Skin skin in config.Skins)
			{
				Launcher.Start(skin);
			}
		});
		((ToolStrip)val).Items.Add("全部停止", (Image)null, (EventHandler)delegate
		{
			foreach (Skin skin2 in config.Skins)
			{
				Launcher.Stop(skin2);
			}
		});
		((ToolStrip)val).Items.Add((ToolStripItem)new ToolStripSeparator());
		((ToolStrip)val).Items.Add("拖尾特效…", (Image)null, (EventHandler)delegate
		{
			ShowTrailSettings();
		});
		((ToolStrip)val).Items.Add("卸载桌面挂件合集…", (Image)null, (EventHandler)delegate
		{
			LaunchUninstaller();
		});
		((ToolStrip)val).Items.Add("退出（同时关闭所有挂件）", (Image)null, (EventHandler)delegate
		{
			foreach (Skin skin3 in config.Skins)
			{
				Launcher.Stop(skin3);
			}
			_reallyExit = true;
			tray.Visible = false;
			((Form)this).Close();
			Application.Exit();
		});
		((ToolStrip)val).Items.Add("仅退出管理器（挂件继续运行）", (Image)null, (EventHandler)delegate
		{
			_reallyExit = true;
			tray.Visible = false;
			((Form)this).Close();
			Application.Exit();
		});
		tray.ContextMenuStrip = val;
		tray.DoubleClick += delegate
		{
			ShowManager();
		};
	}

	private void ShowManager()
	{
		((Control)this).Show();
		((Form)this).WindowState = (FormWindowState)0;
		((Control)this).BringToFront();
	}

	private void ShowAbout()
	{
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		AboutForm aboutForm = new AboutForm(config);
		try
		{
			((Form)aboutForm).ShowDialog((IWin32Window)(object)this);
			// 关于框里保存过令牌后，config.json 的监听可能已经把这个窗口的 config
			// 换成了令牌写盘之前读到的旧对象；不重新加载，下一次 SaveConfig 就会把令牌冲掉。
			Config reloaded = Config.TryLoad();
			if (reloaded != null)
			{
				config = reloaded;
			}
		}
		finally
		{
			((IDisposable)(object)aboutForm)?.Dispose();
		}
	}

	private void ShowWidgetSettings(Skin s)
	{
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Invalid comparison between Unknown and I4
		WidgetSettings.Profile profile = WidgetSettings.For(s);
		if (profile == null)
		{
			MessageBox.Show("「" + s.Name + "」没有可直接调整的设置项。\r\n请在桌面上直接右键这个挂件，它的右键菜单里就是它的设置。", "设置", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		if (!File.Exists(profile.ConfigPath))
		{
			MessageBox.Show("「" + s.Name + "」还没有生成配置文件（" + Path.GetFileName(profile.ConfigPath) + "），\r\n它的设置要等它正常退出一次才会保存。\r\n\r\n在此之前，可以直接右键桌面上的这个挂件来调整。", "设置", (MessageBoxButtons)0, (MessageBoxIcon)64);
			return;
		}
		try
		{
			Dictionary<string, string> current = WidgetSettings.Read(profile);
			WidgetSettingsForm widgetSettingsForm = new WidgetSettingsForm(s.Name, profile, current);
			try
			{
				if ((int)((Form)widgetSettingsForm).ShowDialog((IWin32Window)(object)this) == 1)
				{
					WidgetSettings.Write(profile, widgetSettingsForm.Values());
					if (Launcher.IsRunning(s))
					{
						Launcher.Stop(s);
					}
					Thread.Sleep(500);
					Launcher.Start(s);
					UpdateStatuses();
				}
			}
			finally
			{
				((IDisposable)(object)widgetSettingsForm)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("保存设置失败：" + ex.Message, "设置", (MessageBoxButtons)0, (MessageBoxIcon)48);
		}
	}

	private void ShowTrailSettings()
	{
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Invalid comparison between Unknown and I4
		try
		{
			Dictionary<string, string> dictionary = TrailConfig.Load();
			TrailSettingsForm trailSettingsForm = new TrailSettingsForm(dictionary);
			try
			{
				if ((int)((Form)trailSettingsForm).ShowDialog((IWin32Window)(object)this) == 1)
				{
					dictionary["Effect"] = trailSettingsForm.EffectIndex.ToString();
					dictionary["Palette"] = trailSettingsForm.PaletteIndex.ToString();
					dictionary["PerfMode"] = trailSettingsForm.PerfIndex.ToString();
					dictionary["ShowTray"] = "0";
					TrailConfig.Save(dictionary);
					Skin skin = config.Skins.FirstOrDefault((Skin s) => s.ExeName.IndexOf("NeonMouseTrail", StringComparison.OrdinalIgnoreCase) >= 0);
					if (skin != null)
					{
						Launcher.Stop(skin);
						Thread.Sleep(600);
						Launcher.Start(skin);
					}
					UpdateStatuses();
				}
			}
			finally
			{
				((IDisposable)(object)trailSettingsForm)?.Dispose();
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("设置拖尾特效失败：" + ex.Message, "拖尾特效", (MessageBoxButtons)0, (MessageBoxIcon)48);
		}
	}

	private void LaunchUninstaller()
	{
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			string text = AppContext.BaseDirectory.TrimEnd(new char[1] { '\\' });
			string[] array = new string[2]
			{
				Path.Combine(text, "Uninstall", "WidgetHubUninstall.exe"),
				Path.Combine(Path.GetDirectoryName(text) ?? text, "Uninstall", "WidgetHubUninstall.exe")
			};
			foreach (string text2 in array)
			{
				if (File.Exists(text2))
				{
					_reallyExit = true;
					Process.Start(new ProcessStartInfo(text2)
					{
						UseShellExecute = true,
						WorkingDirectory = Path.GetDirectoryName(text2)
					});
					tray.Visible = false;
					((Form)this).Close();
					Application.Exit();
					return;
				}
			}
			MessageBox.Show("没有找到卸载程序（Uninstall\\WidgetHubUninstall.exe）。\r\n可以到开始菜单里找“卸载桌面挂件合集”，或直接删除安装目录。", "卸载", (MessageBoxButtons)0, (MessageBoxIcon)64);
		}
		catch
		{
		}
	}

	private void StartStatusTimer()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Expected O, but got Unknown
		statusTimer = new Timer
		{
			Interval = 1200
		};
		statusTimer.Tick += delegate
		{
			UpdateStatuses();
		};
		statusTimer.Start();
		layerTimer = new Timer
		{
			Interval = 4000
		};
		layerTimer.Tick += delegate
		{
			KeepDesktopLevel();
		};
		layerTimer.Start();
	}

	private void KeepDesktopLevel()
	{
		try
		{
			foreach (Skin skin in config.Skins)
			{
				if (skin.TopMost || skin.Positionless || !Launcher.IsRunning(skin))
				{
					continue;
				}
				Process process = Launcher.FindRunning(skin);
				if (process == null)
				{
					continue;
				}
				IntPtr intPtr = Win32.FindMainWindow((uint)process.Id);
				if (intPtr == IntPtr.Zero)
				{
					continue;
				}
				int windowLong = Win32.GetWindowLong(intPtr, -20);
				if (((uint)windowLong & 8u) != 0)
				{
					Win32.GetWindowRect(intPtr, out var lpRect);
					Win32.SetWindowLong(intPtr, -20, windowLong & -9);
					Win32.SetWindowPos(intPtr, Win32.HWND_BOTTOM, 0, 0, 0, 0, 3u);
					Win32.GetWindowRect(intPtr, out var lpRect2);
					int num = lpRect2.Right - lpRect2.Left;
					int num2 = lpRect2.Bottom - lpRect2.Top;
					if ((lpRect2.Left != lpRect.Left || lpRect2.Top != lpRect.Top) && num > 0 && num2 > 0)
					{
						Win32.MoveWindow(intPtr, lpRect.Left, lpRect.Top, num, num2, bRepaint: true);
					}
				}
			}
		}
		catch
		{
		}
	}

	private void ApplyAutoStartManager()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey != null)
			{
				if (config.AutoStartManager)
				{
					registryKey.SetValue("WidgetHub", Application.ExecutablePath);
				}
				else
				{
					registryKey.DeleteValue("WidgetHub", throwOnMissingValue: false);
				}
			}
		}
		catch
		{
		}
	}
}
