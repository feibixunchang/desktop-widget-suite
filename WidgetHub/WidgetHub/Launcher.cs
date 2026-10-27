using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetHub;

public static class Launcher
{
	public static Process? FindRunning(Skin s)
	{
		string b = s.FullPath();
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(s.ExeName);
		Process[] processesByName;
		try
		{
			processesByName = Process.GetProcessesByName(fileNameWithoutExtension);
		}
		catch
		{
			return null;
		}
		Process process = null;
		Process[] array = processesByName;
		foreach (Process process2 in array)
		{
			bool flag = false;
			try
			{
				if (process2.MainModule != null)
				{
					flag = string.Equals(process2.MainModule.FileName, b, StringComparison.OrdinalIgnoreCase);
				}
			}
			catch
			{
				flag = true;
			}
			if (flag)
			{
				s.Process = process2;
				return process2;
			}
			if (process == null)
			{
				process = process2;
			}
			else
			{
				process2.Dispose();
			}
		}
		if (process != null)
		{
			s.Process = process;
			return process;
		}
		return null;
	}

	public static bool IsRunning(Skin s)
	{
		if (s.Process != null)
		{
			try
			{
				if (!s.Process.HasExited)
				{
					return true;
				}
			}
			catch
			{
			}
			s.Process = null;
		}
		return FindRunning(s) != null;
	}

	public static Process? RunningProcess(Skin s)
	{
		return FindRunning(s);
	}

	public static Process? Start(Skin s)
	{
		Skin s2 = s;
		Process process = FindRunning(s2);
		if (process != null)
		{
			s2.Process = process;
			IntPtr intPtr = Win32.FindMainWindow((uint)process.Id);
			if (intPtr != IntPtr.Zero)
			{
				Win32.ApplyLayout(s2, intPtr);
			}
			MinimizeIfTrail(s2, process.Id);
			int epid = process.Id;
			Task.Run(delegate
			{
				ReapplyLoop(s2, epid);
			});
			return process;
		}
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = s2.FullPath(),
			WorkingDirectory = Path.GetDirectoryName(s2.FullPath())
		};
		Process process2 = null;
		try
		{
			process2 = Process.Start(startInfo);
		}
		catch (Exception ex)
		{
			Log.W("Start " + s2.Name + " 失败: " + ex.Message);
			return null;
		}
		if (process2 == null)
		{
			Log.W("Start " + s2.Name + " 返回 null");
			return null;
		}
		s2.Process = process2;
		Log.W($"Start {s2.Name} pid={process2.Id} target={s2.X},{s2.Y} size={s2.Width}x{s2.Height}");
		for (int i = 0; i < 30; i++)
		{
			try
			{
				if (process2.HasExited)
				{
					Log.W("Start " + s2.Name + " 启动后立即退出");
					return null;
				}
			}
			catch
			{
				return null;
			}
			IntPtr intPtr2 = Win32.FindMainWindow((uint)process2.Id);
			if (intPtr2 != IntPtr.Zero)
			{
				Log.W($"Start {s2.Name} 第{i}次找到窗口 hwnd={intPtr2}");
				Win32.ApplyLayout(s2, intPtr2);
				MinimizeIfTrail(s2, process2.Id);
				break;
			}
			if (i == 29)
			{
				Log.W("Start " + s2.Name + " 30 次仍未找到窗口");
			}
			Thread.Sleep(120);
		}
		int pid = process2.Id;
		Task.Run(delegate
		{
			ReapplyLoop(s2, pid);
		});
		return process2;
	}

	private static void MinimizeIfTrail(Skin s, int pid)
	{
		try
		{
			if (s.ExeName == null || s.ExeName.IndexOf("NeonMouseTrail", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return;
			}
			IntPtr intPtr = Win32.FindMainWindow((uint)pid);
			if (intPtr != IntPtr.Zero)
			{
				Win32.MinimizeWindow(intPtr);
			}
			Task.Delay(1500).ContinueWith(delegate
			{
				try
				{
					IntPtr intPtr2 = Win32.FindMainWindow((uint)pid);
					if (intPtr2 != IntPtr.Zero && !Win32.IsIconic(intPtr2))
					{
						Win32.MinimizeWindow(intPtr2);
					}
				}
				catch
				{
				}
			});
		}
		catch
		{
		}
	}

	private static void ReapplyLoop(Skin s, int pid)
	{
		if (s.Positionless || s.X < 0 || s.Y < 0)
		{
			return;
		}
		int num = 0;
		Log.W($"ReapplyLoop {s.Name} 开始 target={s.X},{s.Y}");
		for (int i = 0; i < 100; i++)
		{
			Thread.Sleep(300);
			try
			{
				using Process process = Process.GetProcessById(pid);
				if (process.HasExited)
				{
					Log.W("ReapplyLoop " + s.Name + " 进程已退出");
					return;
				}
				IntPtr intPtr = Win32.FindMainWindow((uint)pid);
				if (intPtr == IntPtr.Zero)
				{
					num = 0;
					if (i % 10 == 0)
					{
						Log.W($"ReapplyLoop {s.Name} 未找到窗口 i={i}");
					}
					continue;
				}
				Win32.GetWindowRect(intPtr, out var lpRect);
				if (Math.Abs(lpRect.Left - s.X) <= 2 && Math.Abs(lpRect.Top - s.Y) <= 2)
				{
					if (++num >= 10)
					{
						Log.W("ReapplyLoop " + s.Name + " 位置已稳定");
						return;
					}
				}
				else
				{
					num = 0;
					Log.W($"ReapplyLoop {s.Name} 偏离({lpRect.Left},{lpRect.Top}) 重摆");
					Win32.ApplyLayout(s, intPtr);
				}
			}
			catch (Exception ex)
			{
				Log.W("ReapplyLoop " + s.Name + " 异常 " + ex.GetType().Name);
				return;
			}
		}
		Log.W("ReapplyLoop " + s.Name + " 循环结束仍未稳定");
	}

	public static bool Reapply(Skin s)
	{
		Skin s2 = s;
		if (s2.Positionless || s2.X < 0 || s2.Y < 0)
		{
			return false;
		}
		Process process = FindRunning(s2);
		if (process == null)
		{
			return false;
		}
		IntPtr intPtr = Win32.FindMainWindow((uint)process.Id);
		if (intPtr == IntPtr.Zero)
		{
			return false;
		}
		Win32.ApplyLayout(s2, intPtr);
		int pid = process.Id;
		Task.Run(delegate
		{
			ReapplyLoop(s2, pid);
		});
		return true;
	}

	public static void Stop(Skin s)
	{
		Process process = s.Process ?? FindRunning(s);
		if (process != null)
		{
			try
			{
				if (!process.CloseMainWindow())
				{
					process.Kill();
				}
				if (!process.WaitForExit(3000))
				{
					process.Kill();
				}
			}
			catch
			{
				try
				{
					process.Kill();
				}
				catch
				{
				}
			}
		}
		s.Process = null;
	}
}
