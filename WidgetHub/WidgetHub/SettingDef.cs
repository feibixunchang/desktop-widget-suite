using System;

namespace WidgetHub;

public class SettingDef
{
	public string Key = "";

	public string Label = "";

	public string Kind = "num";

	public int Min;

	public int Max = 100;

	public int Step = 1;

	public string[] Options = Array.Empty<string>();

	public int ComboOffset;
}
