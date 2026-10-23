using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace DesktopLunar
{
    /// <summary>一次对时的结果。</summary>
    internal sealed class SyncResult
    {
        public bool Ok;
        public TimeSpan Offset;      // 网络时间 - 本机时间
        public string Server;
        public double DelayMs;       // 往返延迟，越小越可信
    }

    /// <summary>
    /// 网络对时（SNTP，UDP 123）。只算"网络时间 - 本机时间"的偏差，
    /// 显示时把本机时间加上这个偏差——不改系统时钟（那要管理员权限，也太激进）。
    ///
    /// 服务器按国内优先的顺序试，第一个通的就用；全部失败就继续用本机时间，
    /// 下一次定时对时（默认 30 分钟）或用户手动"立即对时"再试。
    /// </summary>
    internal static class TimeSync
    {
        private static readonly string[] Servers =
        {
            "ntp.aliyun.com",      // 阿里云公共 NTP，国内延迟最低
            "cn.pool.ntp.org",
            "pool.ntp.org",
            "time.windows.com",
        };

        private const int NtpPort = 123;
        private const int TimeoutMs = 2500;
        private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly object Gate = new object();
        private static Timer timer;

        /// <summary>网络时间与本机时间的偏差（网络 - 本机）。</summary>
        public static TimeSpan Offset { get; private set; }

        /// <summary>最近一次成功对时的本机时刻；null = 还没对过。</summary>
        public static DateTime? LastSyncAt { get; private set; }

        /// <summary>最近一次用的服务器。</summary>
        public static string LastServer { get; private set; }

        /// <summary>最近一次的往返延迟（毫秒）。</summary>
        public static double LastDelayMs { get; private set; }

        public static bool Synced
        {
            get { lock (Gate) { return LastSyncAt.HasValue; } }
        }

        /// <summary>显示用的当前时间：本机时间 + 网络偏差。</summary>
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

        /// <summary>人类可读的对时状态，给菜单项和提示条用。</summary>
        public static string StatusText
        {
            get
            {
                lock (Gate)
                {
                    if (LastSyncAt == null) { return "未对时"; }
                    double sec = Offset.TotalSeconds;
                    if (Math.Abs(sec) < 0.05) { return "已对时，无偏差"; }
                    string bias = Math.Abs(sec).ToString("0.###", CultureInfo.InvariantCulture) + " 秒";
                    return "已对时，本机" + (sec > 0 ? "慢 " : "快 ") + bias;
                }
            }
        }

        /// <summary>
        /// 启动定时对时：立即对一次，之后每 <paramref name="interval"/> 对一次。
        /// callback 在每次对时（成功或失败）后于线程池线程上调用，UI 线程要自己跳。
        /// </summary>
        public static void Start(TimeSpan interval, Action<SyncResult> callback)
        {
            lock (Gate)
            {
                if (timer != null) { return; }
                timer = new Timer(_ =>
                {
                    var r = SyncOnce();
                    try { callback(r); } catch { }
                }, null, TimeSpan.Zero, interval);
            }
        }

        /// <summary>
        /// 立即对一次时（有阻塞网络调用，必须放后台线程）。
        /// 按服务器顺序试，第一个通的算结果；全失败返回 Ok=false。
        /// </summary>
        public static SyncResult SyncOnce()
        {
            foreach (string server in Servers)
            {
                TimeSpan offset;
                double delayMs;
                if (Query(server, out offset, out delayMs))
                {
                    lock (Gate)
                    {
                        Offset = offset;
                        LastSyncAt = DateTime.Now;
                        LastServer = server;
                        LastDelayMs = delayMs;
                    }

                    return new SyncResult { Ok = true, Offset = offset, Server = server, DelayMs = delayMs };
                }
            }

            return new SyncResult { Ok = false };
        }

        /// <summary>单台服务器的 SNTP 查询。偏移用标准的四时间戳公式算，往返延迟折半抵掉。</summary>
        private static bool Query(string server, out TimeSpan offset, out double delayMs)
        {
            offset = TimeSpan.Zero;
            delayMs = 0;

            var send = new byte[48];
            send[0] = 0x1B;                        // LI=0, VN=3, Mode=3(client)
            var t1 = DateTime.UtcNow;              // 客户端发出时刻（本机）
            WriteTimestamp(send, 40, t1);          // Transmit Timestamp

            try
            {
                byte[] recv;
                using (var udp = new UdpClient(server, NtpPort))
                {
                    udp.Client.ReceiveTimeout = TimeoutMs;
                    udp.Send(send, send.Length);
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    recv = udp.Receive(ref from);
                }

                var t4 = DateTime.UtcNow;          // 客户端收到时刻（本机）
                if (recv.Length < 48) { return false; }
                if ((recv[0] & 0x07) != 4) { return false; }          // Mode 必须是 Server
                int stratum = recv[1];
                if (stratum == 0 || stratum >= 16) { return false; }  // 0/16 都是不可用

                var t2 = ReadTimestamp(recv, 32); // 服务器收到时刻
                var t3 = ReadTimestamp(recv, 40); // 服务器发出时刻
                if (t3 < t1 - TimeSpan.FromHours(24) || t3 > t4 + TimeSpan.FromHours(24))
                {
                    return false;                  // 差出一天，多半是假包/脏缓存，不敢信
                }

                offset = TimeSpan.FromTicks(((t2 - t1) + (t3 - t4)).Ticks / 2);
                delayMs = (t4 - t1).TotalMilliseconds - (t3 - t2).TotalMilliseconds;
                if (delayMs < 0) { delayMs = 0; }
                return true;
            }
            catch
            {
                return false;                      // DNS 失败/超时/端口不通，换下一台
            }
        }

        private static void WriteTimestamp(byte[] buf, int at, DateTime utc)
        {
            double total = (utc - NtpEpoch).TotalSeconds;
            if (total < 0) { total = 0; }
            ulong seconds = (ulong)total;
            ulong fraction = (ulong)((total - seconds) * 4294967296.0);
            PutUInt64(buf, at, (seconds << 32) | fraction);
        }

        private static DateTime ReadTimestamp(byte[] buf, int at)
        {
            ulong seconds = ((ulong)buf[at] << 24) | ((ulong)buf[at + 1] << 16)
                          | ((ulong)buf[at + 2] << 8) | buf[at + 3];
            ulong fraction = ((ulong)buf[at + 4] << 24) | ((ulong)buf[at + 5] << 16)
                           | ((ulong)buf[at + 6] << 8) | buf[at + 7];
            return NtpEpoch.AddSeconds(seconds + fraction / 4294967296.0);
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
}
