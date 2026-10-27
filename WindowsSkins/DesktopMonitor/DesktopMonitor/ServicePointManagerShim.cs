using System.Net;

namespace DesktopMonitor;

internal static class ServicePointManagerShim
{
	public static void EnableModernTls()
	{
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		}
		catch
		{
		}
	}
}
