using System.Drawing.Text;

namespace XiaoPuHeWeather;

public static class FontProvider
{
    public const string RequestedFamilyName = "方正宋黑_GBK";

    private static readonly Lazy<string> ResolvedFamilyName = new(ResolveFamilyName, true);

    public static string FamilyName => ResolvedFamilyName.Value;

    public static bool IsRequestedFamilyAvailable =>
        string.Equals(FamilyName, RequestedFamilyName, StringComparison.Ordinal);

    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font(FamilyName, size, style, GraphicsUnit.Point);
    }

    private static string ResolveFamilyName()
    {
        using var installedFonts = new InstalledFontCollection();
        var familyName = installedFonts.Families
            .Select(family => family.Name)
            .FirstOrDefault(name => string.Equals(name, RequestedFamilyName, StringComparison.Ordinal));
        return familyName ?? "Microsoft YaHei UI";
    }
}
