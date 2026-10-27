using System;
using System.Drawing;
using System.Drawing.Text;

namespace DesktopMonitor;

internal static class Fonts
{
	public const string RequestedFamilyName = "方正宋黑_GBK";

	private const string FallbackFamilyName = "Microsoft YaHei UI";

	private static readonly Lazy<string> ResolvedFamilyName = new Lazy<string>(Resolve, isThreadSafe: true);

	public static string FamilyName => ResolvedFamilyName.Value;

	public static Font Widget(float size)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		return new Font(FamilyName, size, (FontStyle)1, (GraphicsUnit)3);
	}

	public static Font Menu(float size)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Expected O, but got Unknown
		return new Font(FamilyName, size, (FontStyle)0, (GraphicsUnit)3);
	}

	private static string Resolve()
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Expected O, but got Unknown
		try
		{
			InstalledFontCollection val = new InstalledFontCollection();
			try
			{
				FontFamily[] families = ((FontCollection)val).Families;
				foreach (FontFamily val2 in families)
				{
					if (string.Equals(val2.Name, "方正宋黑_GBK", StringComparison.Ordinal))
					{
						return val2.Name;
					}
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch
		{
		}
		return "Microsoft YaHei UI";
	}
}
