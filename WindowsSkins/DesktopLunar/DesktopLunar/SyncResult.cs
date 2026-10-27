using System;

namespace DesktopLunar;

internal sealed class SyncResult
{
	public bool Ok;

	public TimeSpan Offset;

	public string Server;

	public double DelayMs;
}
