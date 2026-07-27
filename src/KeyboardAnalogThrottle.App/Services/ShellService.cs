using System.Diagnostics;
using System.IO;
using System.Windows;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;

namespace KeyboardAnalogThrottle.App.Services;

public interface IShellService
{
    void OpenConfigurationFile();

    void OpenConfigurationFolder();

    void ExitApplication();
}

/// <summary>
/// Opens user-owned configuration resources through the Windows shell.
/// </summary>
public sealed class ShellService : IShellService
{
    private static readonly string ConfigurationPath = JsonConfigurationService.GetDefaultConfigurationPath();

    public void OpenConfigurationFile() => Open(ConfigurationPath);

    public void OpenConfigurationFolder() => Open(Path.GetDirectoryName(ConfigurationPath)!);

    public void ExitApplication() => Application.Current?.Shutdown();

    private static void Open(string path) => Process.Start(new ProcessStartInfo(path)
    {
        UseShellExecute = true
    });
}
