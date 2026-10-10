using Microsoft.Win32;

namespace Dictate.Desktop;

public static class DesktopStartup
{
    public static void Set(bool enabled)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The app executable could not be found.");
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) key.SetValue("Dictate", '"' + executable + '"');
            else key.DeleteValue("Dictate", false);
        }
        else
        {
            var directory = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart");
            var path = Path.Combine(directory, "dictate.desktop");
            if (!enabled) { File.Delete(path); return; }
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, "[Desktop Entry]\nType=Application\nName=Dictate\nExec=\"" + executable.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"\nTerminal=false\n");
        }
    }
}
