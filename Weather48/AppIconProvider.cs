namespace XiaoPuHeWeather;

public static class AppIconProvider
{
    public const string EmbeddedResourceName = "XiaoPuHeWeather.Assets.weather.ico";

    public static Icon Load()
    {
        var stream = typeof(AppIconProvider).Assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is not null)
        {
            using (stream)
            {
                return new Icon(stream, 32, 32);
            }
        }

        var executablePath = Compat.ExecutablePath;
        if (!string.IsNullOrEmpty(executablePath))
        {
            var extracted = Icon.ExtractAssociatedIcon(executablePath);
            if (extracted is not null)
            {
                return extracted;
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
