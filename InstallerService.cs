using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace SpotifyLyricsOverlay;

public static class InstallerService
{
    private const string AppName = "Spotify Lyrics Overlay";
    private const string AppId = "SpotifyLyricsOverlay";
    public static readonly string AppVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.5";

    public static bool HandleInstallArguments(string[] args)
    {
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe))
            return false;

        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppId);
        var installExe = Path.Combine(installDir, "SpotifyLyricsOverlay.exe");

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            Uninstall(installDir, installExe);
            return true;
        }

        var isSetupName = Path.GetFileNameWithoutExtension(currentExe)
            .Contains("Setup", StringComparison.OrdinalIgnoreCase);

        var isInstallFlag = args.Contains("--install", StringComparer.OrdinalIgnoreCase);

        if (isSetupName || isInstallFlag)
        {
            Install(currentExe, installDir, installExe);
            return true;
        }

        return false;
    }

    public static void Install(string sourceExe, string installDir, string installExe)
    {
        try
        {
            var currentPid = Environment.ProcessId;
            foreach (var proc in Process.GetProcessesByName("SpotifyLyricsOverlay"))
            {
                if (proc.Id != currentPid)
                {
                    try { proc.Kill(); proc.WaitForExit(1000); } catch { }
                }
            }

            Directory.CreateDirectory(installDir);

            if (!string.Equals(sourceExe, installExe, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourceExe, installExe, overwrite: true);

                // Copy Lyrics folder if adjacent to setup exe
                var sourceDir = Path.GetDirectoryName(sourceExe);
                if (!string.IsNullOrWhiteSpace(sourceDir))
                {
                    var sourceLyrics = Path.Combine(sourceDir, "Lyrics");
                    var targetLyrics = Path.Combine(installDir, "Lyrics");
                    var appDataLyrics = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "SpotifyLyricsOverlay", "Lyrics");

                    if (Directory.Exists(sourceLyrics))
                    {
                        Directory.CreateDirectory(targetLyrics);
                        Directory.CreateDirectory(appDataLyrics);

                        foreach (var file in Directory.GetFiles(sourceLyrics))
                        {
                            var fileName = Path.GetFileName(file);
                            try { File.Copy(file, Path.Combine(targetLyrics, fileName), true); } catch { }
                            try { File.Copy(file, Path.Combine(appDataLyrics, fileName), true); } catch { }
                        }
                    }
                }
            }

            CreateShortcuts(installDir, installExe);
            RegisterUninstall(installDir, installExe);

            MessageBox.Show(
                $"{AppName} berhasil diinstall!\n\nShortcut telah ditambahkan ke Desktop dan Start Menu.",
                "Instalasi Berhasil",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Process.Start(new ProcessStartInfo(installExe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Gagal menginstall {AppName}:\n{ex.Message}",
                "Instalasi Gagal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void Uninstall(string installDir, string installExe)
    {
        var result = MessageBox.Show(
            $"Apakah Anda yakin ingin menghapus (uninstall) {AppName}?",
            "Konfirmasi Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{AppName}.lnk");
            if (File.Exists(desktopShortcut))
                File.Delete(desktopShortcut);

            var startMenuShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                $"{AppName}.lnk");
            if (File.Exists(startMenuShortcut))
                File.Delete(startMenuShortcut);

            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}",
                throwOnMissingSubKey: false);

            MessageBox.Show(
                $"{AppName} berhasil dihapus dari komputer Anda.",
                "Uninstall Selesai",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            var cmd = $"/c timeout /t 1 /nobreak & rmdir /s /q \"{installDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Terjadi kesalahan saat uninstall:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void CreateShortcuts(string installDir, string installExe)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;

            dynamic shell = Activator.CreateInstance(shellType)!;

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var desktopShortcut = shell.CreateShortcut(Path.Combine(desktopPath, $"{AppName}.lnk"));
            desktopShortcut.TargetPath = installExe;
            desktopShortcut.WorkingDirectory = installDir;
            desktopShortcut.IconLocation = $"{installExe},0";
            desktopShortcut.Save();

            var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var startMenuShortcut = shell.CreateShortcut(Path.Combine(startMenuPath, $"{AppName}.lnk"));
            startMenuShortcut.TargetPath = installExe;
            startMenuShortcut.WorkingDirectory = installDir;
            startMenuShortcut.IconLocation = $"{installExe},0";
            startMenuShortcut.Save();
        }
        catch { }
    }

    private static void RegisterUninstall(string installDir, string installExe)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}");
            if (key != null)
            {
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayIcon", installExe);
                key.SetValue("DisplayVersion", AppVersion);
                key.SetValue("Publisher", "Local");
                key.SetValue("InstallLocation", installDir);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("UninstallString", $"\"{installExe}\" --uninstall");
            }
        }
        catch { }
    }
}
