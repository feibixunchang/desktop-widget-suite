using System.Runtime.CompilerServices;

namespace XiaoPuHeWeather;

// net48 兼容垫片：把 net6+/net8 时代才有的 API 补上，
// 这样安宁市天气挂件可以跟其它 9 个挂件一样直接跑在 .NET Framework 4.8 上，
// 目标电脑不需要再装 .NET 运行时。
internal static class Compat
{
    /// <summary>double.IsFinite（net5+）的等价实现。</summary>
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    /// <summary>Math.Clamp（netcore 2.0+）的等价实现。</summary>
    public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

    public static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);

    /// <summary>string.Contains(string, StringComparison)（netcore 2.1+）的等价实现。</summary>
    public static bool ContainsOrdinal(this string text, string value)
        => text.IndexOf(value, StringComparison.Ordinal) >= 0;

    /// <summary>取当前进程可执行文件路径（Environment.ProcessPath 在 net5+ 才有）。</summary>
    public static string ExecutablePath
    {
        get
        {
            try
            {
                var path = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path!;
            }
            catch { }
            return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "XiaoPuHeWeather.exe";
        }
    }
}
