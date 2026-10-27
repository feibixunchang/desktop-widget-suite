using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DesktopLunar;

internal static class TimeSync
{
	private static readonly string[] Servers = new string[4] { "ntp.aliyun.com", "cn.pool.ntp.org", "pool.ntp.org", "time.windows.com" };

	private const int NtpPort = 123;

	private const int TimeoutMs = 2500;

	private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	private static readonly object Gate = new object();

	private static Timer timer;

	public static TimeSpan Offset { get; private set; }

	public static DateTime? LastSyncAt { get; private set; }

	public static string LastServer { get; private set; }

	public static double LastDelayMs { get; private set; }

	public static bool Synced
	{
		get
		{
			lock (Gate)
			{
				return LastSyncAt.HasValue;
			}
		}
	}

	public static DateTime Now
	{
		get
		{
			lock (Gate)
			{
				return DateTime.Now + Offset;
			}
		}
	}

	public static string StatusText
	{
		get
		{
			lock (Gate)
			{
				if (!LastSyncAt.HasValue)
				{
					return "未对时";
				}
				double totalSeconds = Offset.TotalSeconds;
				if (Math.Abs(totalSeconds) < 0.05)
				{
					return "已对时，无偏差";
				}
				string text = Math.Abs(totalSeconds).ToString("0.###", CultureInfo.InvariantCulture) + " 秒";
				return "已对时，本机" + ((totalSeconds > 0.0) ? "慢 " : "快 ") + text;
			}
		}
	}

	public static void Start(TimeSpan interval, Action<SyncResult> callback)
	{
		lock (Gate)
		{
			if (timer != null)
			{
				return;
			}
			timer = new Timer(delegate
			{
				SyncResult obj = SyncOnce();
				try
				{
					callback(obj);
				}
				catch
				{
				}
			}, null, TimeSpan.Zero, interval);
		}
	}

	public static SyncResult SyncOnce()
	{
		string[] servers = Servers;
		foreach (string text in servers)
		{
			if (Query(text, out var offset, out var delayMs))
			{
				lock (Gate)
				{
					Offset = offset;
					LastSyncAt = DateTime.Now;
					LastServer = text;
					LastDelayMs = delayMs;
				}
				return new SyncResult
				{
					Ok = true,
					Offset = offset,
					Server = text,
					DelayMs = delayMs
				};
			}
		}
		return new SyncResult
		{
			Ok = false
		};
	}

	private static bool Query(string server, out TimeSpan offset, out double delayMs)
	{
		offset = TimeSpan.Zero;
		delayMs = 0.0;
		byte[] array = new byte[48];
		array[0] = 27;
		DateTime utcNow = DateTime.UtcNow;
		WriteTimestamp(array, 40, utcNow);
		try
		{
			byte[] array2;
			using (UdpClient udpClient = new UdpClient(server, 123))
			{
				udpClient.Client.ReceiveTimeout = 2500;
				udpClient.Send(array, array.Length);
				IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
				array2 = udpClient.Receive(ref remoteEP);
			}
			DateTime utcNow2 = DateTime.UtcNow;
			if (array2.Length < 48)
			{
				return false;
			}
			if ((array2[0] & 7) != 4)
			{
				return false;
			}
			int num = array2[1];
			if (num == 0 || num >= 16)
			{
				return false;
			}
			DateTime dateTime = ReadTimestamp(array2, 32);
			DateTime dateTime2 = ReadTimestamp(array2, 40);
			if (dateTime2 < utcNow - TimeSpan.FromHours(24.0) || dateTime2 > utcNow2 + TimeSpan.FromHours(24.0))
			{
				return false;
			}
			offset = TimeSpan.FromTicks((dateTime - utcNow + (dateTime2 - utcNow2)).Ticks / 2);
			delayMs = (utcNow2 - utcNow).TotalMilliseconds - (dateTime2 - dateTime).TotalMilliseconds;
			if (delayMs < 0.0)
			{
				delayMs = 0.0;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void WriteTimestamp(byte[] buf, int at, DateTime utc)
	{
		double num = (utc - NtpEpoch).TotalSeconds;
		if (num < 0.0)
		{
			num = 0.0;
		}
		ulong num2 = (ulong)num;
		ulong num3 = (ulong)((num - (double)num2) * 4294967296.0);
		PutUInt64(buf, at, (num2 << 32) | num3);
	}

	private static DateTime ReadTimestamp(byte[] buf, int at)
	{
		ulong num = ((ulong)buf[at] << 24) | ((ulong)buf[at + 1] << 16) | ((ulong)buf[at + 2] << 8) | buf[at + 3];
		ulong num2 = ((ulong)buf[at + 4] << 24) | ((ulong)buf[at + 5] << 16) | ((ulong)buf[at + 6] << 8) | buf[at + 7];
		DateTime ntpEpoch = NtpEpoch;
		return ntpEpoch.AddSeconds((double)num + (double)num2 / 4294967296.0);
	}

	private static void PutUInt64(byte[] buf, int at, ulong value)
	{
		buf[at] = (byte)(value >> 56);
		buf[at + 1] = (byte)(value >> 48);
		buf[at + 2] = (byte)(value >> 40);
		buf[at + 3] = (byte)(value >> 32);
		buf[at + 4] = (byte)(value >> 24);
		buf[at + 5] = (byte)(value >> 16);
		buf[at + 6] = (byte)(value >> 8);
		buf[at + 7] = (byte)value;
	}
}
