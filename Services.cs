using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetroGamingCollection;

public sealed class SettingsStore
{
    public static string DataDirectory => Path.Combine(AppPaths.DataRoot, "Configuration");
    private readonly string _path = Path.Combine(DataDirectory, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LauncherSettings Load() => Read<LauncherSettings>(_path) ?? new();
    public List<GameEntry> LoadLibrary() => Read<List<GameEntry>>(Path.Combine(DataDirectory, "library.json")) ?? [];
    private static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return default; }
    }
    public void Save(LauncherSettings settings) => Write(_path, settings);
    public void SaveLibrary(List<GameEntry> games) => Write(Path.Combine(DataDirectory, "library.json"), games);
    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Options));
        File.Move(path + ".tmp", path, true);
    }
}

public static class ExternalDrive
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder name, int length);
    public static string? Identity(string root)
    {
        var name = new StringBuilder(100);
        return GetVolumeNameForVolumeMountPoint(root, name, name.Capacity) ? name.ToString() : null;
    }
    public static void Reconnect(LauncherSettings settings, List<GameEntry> games)
    {
        if (settings.ExternalRoot is null || settings.ExternalVolumeId is null) return;
        var root = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName)
            .FirstOrDefault(r => string.Equals(Identity(r), settings.ExternalVolumeId, StringComparison.OrdinalIgnoreCase));
        if (root is null || root.Equals(settings.ExternalRoot, StringComparison.OrdinalIgnoreCase)) return;
        var old = settings.ExternalRoot;
        string Remap(string p) => p.StartsWith(old, StringComparison.OrdinalIgnoreCase) ? Path.Combine(root, p[old.Length..]) : p;
        settings.LibraryPaths = settings.LibraryPaths.Select(Remap).ToList();
        foreach (var game in games) if (game.FilePath is not null) game.FilePath = Remap(game.FilePath);
        settings.ExternalRoot = root;
    }
}

public sealed class RetroArchService
{
    public static string? DiscoverExecutable() => new[] {
        Path.Combine(AppContext.BaseDirectory,"Dependencies","RetroArch-Win64","retroarch.exe"),
        Path.Combine(AppContext.BaseDirectory,"RetroArch","retroarch.exe") }.FirstOrDefault(File.Exists);
    public static bool IsValid(string? executable, string? core) => File.Exists(executable) && File.Exists(core);
    public static string LocalPath(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    public Process? Launch(GameEntry game, LauncherSettings settings)
    {
        if (!game.IsInstalled) throw new FileNotFoundException("Connect the original library drive, then press RESCAN. The game file is unavailable.", game.FilePath);
        if (game.DetectedPlatform is null) throw new InvalidOperationException("This file's platform is unverified; it cannot be launched automatically.");
        if (game.DetectedPlatform == GamePlatform.Dos)
            throw new InvalidOperationException("DOS titles require an individually verified DOSBox mount and startup command. This file has not been configured.");
        var core = game.DetectedPlatform switch { GamePlatform.Genesis => settings.GenesisCorePath, GamePlatform.Snes => settings.SnesCorePath, _ => null };
        var executable = settings.RetroArchPath is null ? null : LocalPath(settings.RetroArchPath);
        core = core is null ? null : LocalPath(core);
        if (!IsValid(executable, core)) throw new InvalidOperationException("RetroArch or the required platform core is unavailable. Check SETTINGS.");
        var state = Path.Combine(AppPaths.DataRoot,"Runtime");
        var saves = Path.Combine(AppPaths.DataPath(settings.SaveDirectory),game.DetectedPlatform.Value.ToString());
        var shots = AppPaths.DataPath(settings.ScreenshotDirectory);
        Directory.CreateDirectory(state); Directory.CreateDirectory(saves); Directory.CreateDirectory(shots);
        Directory.CreateDirectory(Path.Combine(state,"System")); Directory.CreateDirectory(Path.Combine(state,"Cache"));
        var config = Path.Combine(state,"launcher.cfg");
        string Q(string p) => p.Replace('\\','/').Replace("\"", "");
        File.WriteAllText(config, $"""
            config_save_on_exit = "false"
            video_driver = "gl"
            audio_driver = "wasapi"
            input_driver = "sdl2"
            input_joypad_driver = "xinput"
            input_autodetect_enable = "true"
            joypad_autoconfig_dir = "{Q(Path.Combine(Path.GetDirectoryName(executable!)!,"autoconfig"))}"
            video_fullscreen = "{settings.Fullscreen.ToString().ToLowerInvariant()}"
            video_force_aspect = "{settings.KeepAspectRatio.ToString().ToLowerInvariant()}"
            video_scale_integer = "{settings.IntegerScale.ToString().ToLowerInvariant()}"
            video_windowed_scale = "3"
            menu_driver = "rgui"
            savestate_auto_save = "false"
            savefile_directory = "{Q(saves)}"
            savestate_directory = "{Q(saves)}"
            screenshot_directory = "{Q(shots)}"
            system_directory = "{Q(Path.Combine(state,"System"))}"
            cache_directory = "{Q(Path.Combine(state,"Cache"))}"
            core_options_path = "{Q(Path.Combine(state,"core-options.cfg"))}"
            content_history_path = "{Q(Path.Combine(state,"history.lpl"))}"
            content_favorites_path = "{Q(Path.Combine(state,"favorites.lpl"))}"
            input_player1_up = "up"
            input_player1_down = "down"
            input_player1_left = "left"
            input_player1_right = "right"
            input_player1_a = "x"
            input_player1_b = "z"
            input_player1_x = "s"
            input_player1_y = "a"
            input_player1_l = "q"
            input_player1_r = "w"
            input_player1_start = "enter"
            input_player1_select = "rshift"
            input_exit_emulator = "escape"
            quit_press_twice = "false"
            """);
        var start = new ProcessStartInfo { FileName = executable!, WorkingDirectory = state, UseShellExecute = false };
        start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
        start.ArgumentList.Add("-L"); start.ArgumentList.Add(core!); start.ArgumentList.Add(game.FilePath!);
        return Process.Start(start);
    }
}

public static class LibraryScanner
{
    public static readonly string[] Extensions = [".smc", ".sfc", ".zip", ".bin", ".md", ".gen"];
    public static bool Remembered(string title) => Regex.IsMatch(title, @"looney.*(b.ball|basket)|mortal.*kombat.?3|street.*fighter|aladdin|contra|spider.man|maximum.carnage|nba.*(jam|live.*95)|need.*speed", RegexOptions.IgnoreCase);
    public static Task<List<GameEntry>> ScanAsync(string root, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var result = new List<GameEntry>();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            try { var game = Inspect(file); if (game is not null) result.Add(game); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException) { }
        }
        return result;
    }, cancellationToken);
    public static GameEntry? Inspect(string file)
    {
        byte[] data = new byte[0x10200];
        int length; string? member = null;
        if (Path.GetExtension(file).Equals(".zip",StringComparison.OrdinalIgnoreCase))
        {
            using var archive = ZipFile.OpenRead(file);
            var entries = archive.Entries.Where(e => new[]{".sfc",".smc",".bin",".md",".gen"}.Contains(Path.GetExtension(e.FullName),StringComparer.OrdinalIgnoreCase)).ToList();
            if (entries.Count != 1 || entries[0].Length > 16 * 1024 * 1024) return null;
            member = entries[0].FullName;
            using var stream = entries[0].Open(); length = stream.ReadAtLeast(data,data.Length,false);
        }
        else { using var stream = File.OpenRead(file); length = stream.ReadAtLeast(data,data.Length,false); }
        GamePlatform? platform = null; string? title = null;
        if (length >= 0x180 && Encoding.ASCII.GetString(data,0x100,4)=="SEGA")
        { platform=GamePlatform.Genesis; title=Encoding.ASCII.GetString(data,0x150,48).Trim(); }
        else foreach (var offset in new[]{0x7fc0,0xffc0,0x81c0,0x101c0})
        {
            if (length<offset+32) continue;
            var checksum=BitConverter.ToUInt16(data,offset+30); var inverse=BitConverter.ToUInt16(data,offset+28);
            // Contra III's original header has a 22-character title; its final S occupies map mode.
            var contraHeader = data[offset+21] == 0x53 && Encoding.ASCII.GetString(data,offset,21) == "CONTRA3 THE ALIEN WAR";
            if ((checksum^inverse)==0xffff && checksum!=0 && (contraHeader || new byte[]{0,1,2,3,5,0x20,0x21,0x22,0x23,0x25,0x30,0x31,0x32,0x35}.Contains(data[offset+21])))
            { platform=GamePlatform.Snes; title=Encoding.ASCII.GetString(data,offset,21).Trim(); break; }
        }
        if (platform is null) return null;
        return new GameEntry { Title=Path.GetFileNameWithoutExtension(file), Platform=platform==GamePlatform.Snes ? "SNES" : "Genesis", DetectedPlatform=platform, FilePath=file, ArchiveMember=member,InternalTitle=title, Confidence="Platform verified from cartridge header. Region/revision from filename; childhood edition remains unconfirmed." };
    }
}
