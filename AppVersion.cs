using System.Reflection;
using System.Text.RegularExpressions;

namespace RetroGamingCollection;

public readonly record struct ReleaseVersion(int Counter) : IComparable<ReleaseVersion>
{
    public string Display => $"{Counter / 100}.{Counter % 100:00}";
    public int CompareTo(ReleaseVersion other) => Counter.CompareTo(other.Counter);
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        var match = Regex.Match(value ?? "", @"^v?(\d+)\.(\d{2})$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major) || major > (int.MaxValue - 99) / 100) return false;
        version = new(major * 100 + int.Parse(match.Groups[2].Value));
        return true;
    }
}

public static class AppVersion
{
    public const string Repository = "Alt-F4-Delta/RetroGamingCollection";
    public static ReleaseVersion Current
    {
        get
        {
            var text = typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
            return ReleaseVersion.TryParse(text, out var version) ? version : new(100);
        }
    }
}
