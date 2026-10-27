using System.Drawing;

namespace WeatherWidget48;

public sealed class AppSettings
{
    public string LocationName { get; set; } = GeoLocation.Default.Name;

    public string Admin1 { get; set; } = GeoLocation.Default.Admin1;

    public string Admin2 { get; set; } = GeoLocation.Default.Admin2;

    public string Country { get; set; } = GeoLocation.Default.Country;

    public double Latitude { get; set; } = GeoLocation.Default.Latitude;

    public double Longitude { get; set; } = GeoLocation.Default.Longitude;

    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public bool PositionLocked { get; set; }

    /// <summary>
    /// 用户是否自己横向摆过位置。
    /// false（默认）= 挂件自动水平居中在屏幕垂直中线；
    /// true = 用户拖到哪就待在哪，系统不再把它拽回中线。
    /// （拖动时强制居中会让挂件"乱颤"，横向永远拖不动。）
    /// </summary>
    public bool FreeHorizontalPosition { get; set; }

    public bool TopMost { get; set; }

    public int FirstLineFontSize { get; set; } = 24;

    public int SecondLineFontSize { get; set; } = 12;

    public string FirstLineColor { get; set; } = "#42A5F5";

    public string SecondLineColor { get; set; } = "#FFFFFF";

    public string BackgroundColor { get; set; } = "#20242C";

    public int OpacityPercent { get; set; } = 94;

  /// <summary>
  /// 有全屏/最大化窗口在前台时自动隐藏。
  /// 用可空类型：老设置里没有这一项（null）时按"开启"处理。
  /// </summary>
  public bool? HideOnFullscreen { get; set; }

  /// <summary>
  /// 贴在桌面底层（桌面图标后面）。默认 true —— 用户要的是挂件只待在桌面层、
  /// 像桌面图标那样被别的窗口盖住，而不是压在所有程序上面。
  /// </summary>
  public bool DesktopLayer { get; set; } = true;

    public GeoLocation ToGeoLocation()
    {
        return new GeoLocation(LocationName, Admin1, Admin2, Country, Latitude, Longitude);
    }

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }

    public void Normalize()
    {
        LocationName = NormalizeText(LocationName, GeoLocation.Default.Name, 80);
        Admin1 = NormalizeText(Admin1, string.Empty, 80);
        Admin2 = NormalizeText(Admin2, string.Empty, 80);
        Country = NormalizeText(Country, string.Empty, 80);

        if (!Compat.IsFinite(Latitude) || Latitude is < -90 or > 90)
        {
            Latitude = GeoLocation.Default.Latitude;
        }

        if (!Compat.IsFinite(Longitude) || Longitude is < -180 or > 180)
        {
            Longitude = GeoLocation.Default.Longitude;
        }

        if (WindowX.HasValue != WindowY.HasValue)
        {
            WindowX = null;
            WindowY = null;
        }

        TopMost = false;
        FirstLineFontSize = Compat.Clamp(FirstLineFontSize, 12, 48);
        SecondLineFontSize = Compat.Clamp(SecondLineFontSize, 8, 30);
        OpacityPercent = Compat.Clamp(OpacityPercent, 0, 100);
        FirstLineColor = NormalizeColor(FirstLineColor, "#42A5F5");
        SecondLineColor = NormalizeColor(SecondLineColor, "#FFFFFF");
        BackgroundColor = NormalizeColor(BackgroundColor, "#20242C");
    }

    private static string NormalizeText(string? value, string fallback, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Substring(0, Math.Min(normalized.Length, maximumLength));
    }

    private static string NormalizeColor(string value, string fallback)
    {
        return ColorHtml.TryParse(value, out _) ? ColorHtml.ToHex(value) : fallback;
    }
}

public static class ColorHtml
{
    public static bool TryParse(string? value, out Color color)
    {
        color = Color.Black;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            color = ColorTranslator.FromHtml(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public static string ToHex(string? color)
    {
        return TryParse(color, out var parsed) ? ToHex(parsed) : "#000000";
    }
}
