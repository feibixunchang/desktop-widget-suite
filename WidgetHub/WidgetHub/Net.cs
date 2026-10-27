namespace WidgetHub;

internal static class Net
{
	public static int Clamp255(int v)
	{
		if (v >= 0)
		{
			if (v <= 255)
			{
				return v;
			}
			return 255;
		}
		return 0;
	}
}
