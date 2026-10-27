namespace DesktopMonitor;

internal sealed class DiskEntry
{
	public string Text { get; }

	public int ColorIndex { get; }

	public DiskEntry(string text, int colorIndex)
	{
		Text = text;
		ColorIndex = colorIndex;
	}
}
