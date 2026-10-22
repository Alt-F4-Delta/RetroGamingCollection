using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace RetroGamingCollection;

public static class AppPaths
{
    public static bool IsInstalled => File.Exists(Path.Combine(AppContext.BaseDirectory, "installed.flag"));
    public static string DataRoot => Environment.GetEnvironmentVariable("RETROGAMING_DATA_DIRECTORY")
        ?? (IsInstalled ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroGamingCollection") : AppContext.BaseDirectory);
    public static string DataPath(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(DataRoot, path));

    public static void Initialize()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\RetroGamingCollection");
        if (!IsInstalled) { key.SetValue("PortableDirectory", AppContext.BaseDirectory); return; }
        var target = Path.Combine(DataRoot, "Configuration");
        if (File.Exists(Path.Combine(target, "settings.json"))) return;
        var source = key.GetValue("PortableDirectory") as string;
        if (!string.IsNullOrWhiteSpace(source)) ImportPortableConfiguration(source, target);
    }

    public static void ImportPortableConfiguration(string source, string target)
    {
        var config = Path.Combine(source, "Configuration");
        var sourceSettings = Path.Combine(config, "settings.json");
        if (!File.Exists(sourceSettings)) return;
        var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(sourceSettings));
        if (settings is null) return;
        string? Absolute(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(source, path));
        settings.RetroArchPath = Absolute(settings.RetroArchPath);
        settings.SnesCorePath = Absolute(settings.SnesCorePath);
        settings.GenesisCorePath = Absolute(settings.GenesisCorePath);
        settings.DosBoxPath = Absolute(settings.DosBoxPath);
        // Keep using existing saves in place; installers never move or overwrite them.
        settings.SaveDirectory = Absolute(settings.SaveDirectory)!;
        settings.ScreenshotDirectory = Absolute(settings.ScreenshotDirectory)!;
        Directory.CreateDirectory(target);
        var library = Path.Combine(config, "library.json");
        if (File.Exists(library) && !File.Exists(Path.Combine(target, "library.json"))) File.Copy(library, Path.Combine(target, "library.json"));
        var destination = Path.Combine(target, "settings.json");
        if (!File.Exists(destination)) File.WriteAllText(destination, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
