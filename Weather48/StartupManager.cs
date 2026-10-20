using Microsoft.Win32;

namespace XiaoPuHeWeather;

public sealed class StartupManager
{
    private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DefaultValueName = "XiaoPuHeWeather";
    private readonly string _runKeyPath;
    private readonly string _valueName;

    public StartupManager(string? runKeyPath = null, string? valueName = null)
    {
        _runKeyPath = runKeyPath ?? DefaultRunKeyPath;
        _valueName = valueName ?? DefaultValueName;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, false);
        return key?.GetValue(_valueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existingKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, true);
            existingKey?.DeleteValue(_valueName, false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath, true)
            ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表。");
        key.SetValue(_valueName, BuildCommand(), RegistryValueKind.String);
    }

    private static string BuildCommand()
    {
        var executablePath = Compat.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            executablePath = Application.ExecutablePath;
        }

        if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyPath = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(assemblyPath))
            {
                return $"\"{executablePath}\" \"{assemblyPath}\"";
            }
        }

        return $"\"{executablePath}\"";
    }
}
