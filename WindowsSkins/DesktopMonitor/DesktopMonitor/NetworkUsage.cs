namespace DesktopMonitor;

internal sealed class NetworkUsage
{
	public string Receive { get; }

	public string Send { get; }

	public NetworkUsage(string receive, string send)
	{
		Receive = receive;
		Send = send;
	}
}
