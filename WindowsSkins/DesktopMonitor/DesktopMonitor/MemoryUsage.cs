namespace DesktopMonitor;

internal sealed class MemoryUsage
{
	public double UsedPercent { get; }

	public double UsedGb { get; }

	public double TotalGb { get; }

	public MemoryUsage(double usedPercent, double usedGb, double totalGb)
	{
		UsedPercent = usedPercent;
		UsedGb = usedGb;
		TotalGb = totalGb;
	}
}
