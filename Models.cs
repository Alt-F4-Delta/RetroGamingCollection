using System.IO;

namespace RetroGamingCollection;

public enum GamePlatform { Snes, Genesis, Dos }

public sealed class GameEntry
{
    public required string Title { get; init; }
    public required string Platform { get; init; }
    public string DisplayTitle => $"[{Platform}] {Title}";
    public int Year { get; init; }
    public required string Confidence { get; init; }
    public string Status => IsInstalled ? "Available" : FilePath is null ? "Not found" : "Drive disconnected or file missing";
    public string? FilePath { get; set; }
    public string? Sha256 { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public int PlayCount { get; set; }
    public bool IsInstalled => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
    public GamePlatform? DetectedPlatform { get; set; }
    public string? ArchiveMember { get; set; }
    public string? InternalTitle { get; set; }
}

public sealed class LauncherSettings
{
    public string? RetroArchPath { get; set; }
    public string? SnesCorePath { get; set; }
    public string? GenesisCorePath { get; set; }
    public string? DosBoxPath { get; set; }
    public string? ExternalVolumeId { get; set; }
    public string? ExternalRoot { get; set; }
    public List<string> LibraryPaths { get; set; } = [];
    public string LanguageFilter { get; set; } = "Untagged";
    public bool UsaOnly { get; set; } = true;
    public bool Fullscreen { get; set; }
    public bool KeepAspectRatio { get; set; } = true;
    public bool IntegerScale { get; set; }
    public string SaveDirectory { get; set; } = "Saves";
    public string ScreenshotDirectory { get; set; } = "Screenshots";
}
