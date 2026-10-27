namespace Shared;

internal sealed class WidgetSettingsStore
{
	private readonly string path;

	public WidgetSettings Settings { get; private set; }

	public WidgetSettingsStore(string filePath)
	{
		path = filePath;
		Settings = JsonStore.Load<WidgetSettings>(path) ?? new WidgetSettings();
	}

	public void Save()
	{
		JsonStore.Save(path, Settings);
	}
}
