using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DesktopMonitor;

internal static class SystemMetrics
{
	private struct MEMORYSTATUSEX
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}

	private const double OneGigabyte = 1073741824.0;

	[DllImport("kernel32.dll")]
	private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

	public static PerformanceCounter CreateCpuCounter()
	{
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected O, but got Unknown
		try
		{
			PerformanceCounter val = new PerformanceCounter("Processor", "% Processor Time", "_Total");
			val.NextValue();
			return val;
		}
		catch
		{
			return null;
		}
	}

	public static int GetCpuUsagePct(PerformanceCounter counter)
	{
		if (counter == null)
		{
			return -1;
		}
		try
		{
			int num = (int)Math.Round(counter.NextValue());
			if (num < 0)
			{
				num = 0;
			}
			return (num > 100) ? 100 : num;
		}
		catch
		{
			return -1;
		}
	}

	public static MemoryUsage GetMemory()
	{
		try
		{
			MEMORYSTATUSEX buffer = default(MEMORYSTATUSEX);
			buffer.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
			if (GlobalMemoryStatusEx(ref buffer) && buffer.ullTotalPhys != 0)
			{
				double usedGb = (double)(buffer.ullTotalPhys - buffer.ullAvailPhys) / 1073741824.0;
				double totalGb = (double)buffer.ullTotalPhys / 1073741824.0;
				return new MemoryUsage((double)(buffer.ullTotalPhys - buffer.ullAvailPhys) / (double)buffer.ullTotalPhys * 100.0, usedGb, totalGb);
			}
		}
		catch
		{
		}
		return new MemoryUsage(0.0, 0.0, 0.0);
	}

	public static List<DiskEntry> GetDisks()
	{
		List<DiskEntry> list = new List<DiskEntry>();
		try
		{
			DriveInfo[] drives = DriveInfo.GetDrives();
			foreach (DriveInfo driveInfo in drives)
			{
				if (driveInfo.IsReady && (driveInfo.DriveType == DriveType.Fixed || driveInfo.DriveType == DriveType.Removable))
				{
					long totalSize = driveInfo.TotalSize;
					long availableFreeSpace = driveInfo.AvailableFreeSpace;
					if (totalSize > 0)
					{
						double value = (double)(totalSize - availableFreeSpace) / (double)totalSize * 100.0;
						string text = string.Format(CultureInfo.InvariantCulture, "{0}{1}% {2}/{3}GB", driveInfo.Name.TrimEnd(new char[1] { '\\' }), (int)Math.Round(value, MidpointRounding.AwayFromZero), (int)Math.Round((double)availableFreeSpace / 1073741824.0, MidpointRounding.AwayFromZero), (int)Math.Round((double)totalSize / 1073741824.0, MidpointRounding.AwayFromZero));
						list.Add(new DiskEntry(text, list.Count));
					}
				}
			}
		}
		catch
		{
		}
		return list;
	}

	public static string FormatSpeed(double bytesPerSecond)
	{
		if (bytesPerSecond >= 1073741824.0)
		{
			return Math.Round(bytesPerSecond / 1073741824.0, 2).ToString(CultureInfo.InvariantCulture) + "GB/s";
		}
		if (bytesPerSecond >= 1048576.0)
		{
			return Math.Round(bytesPerSecond / 1048576.0, 1).ToString(CultureInfo.InvariantCulture) + "MB/s";
		}
		if (bytesPerSecond >= 1024.0)
		{
			return Math.Round(bytesPerSecond / 1024.0, 0).ToString(CultureInfo.InvariantCulture) + "KB/s";
		}
		return Math.Round(bytesPerSecond, 0).ToString(CultureInfo.InvariantCulture) + "B/s";
	}

	public static NetworkUsage GetNetworkSpeed(ref long previousReceived, ref long previousSent, int refreshMilliseconds)
	{
		long num = 0L;
		long num2 = 0L;
		try
		{
			NetworkInterface[] allNetworkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
			foreach (NetworkInterface networkInterface in allNetworkInterfaces)
			{
				if (networkInterface.OperationalStatus == OperationalStatus.Up && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
				{
					IPInterfaceStatistics iPStatistics = networkInterface.GetIPStatistics();
					num += iPStatistics.BytesReceived;
					num2 += iPStatistics.BytesSent;
				}
			}
			string receive = "0B/s";
			string send = "0B/s";
			if (previousReceived >= 0)
			{
				double num3 = (double)(num - previousReceived) * 1000.0 / (double)refreshMilliseconds;
				double num4 = (double)(num2 - previousSent) * 1000.0 / (double)refreshMilliseconds;
				if (num3 < 0.0)
				{
					num3 = 0.0;
				}
				if (num4 < 0.0)
				{
					num4 = 0.0;
				}
				receive = FormatSpeed(num3);
				send = FormatSpeed(num4);
			}
			previousReceived = num;
			previousSent = num2;
			return new NetworkUsage(receive, send);
		}
		catch
		{
			return new NetworkUsage("0B/s", "0B/s");
		}
	}
}
