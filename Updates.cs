using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RetroGamingCollection;

public sealed record AvailableUpdate(ReleaseVersion Version, string Notes, Uri InstallerUrl, long Size, string? Digest, Uri ChecksumsUrl);

public sealed class UpdateService
{
    private readonly HttpClient _client;
    private const long MaxInstallerBytes = 500L * 1024 * 1024;
    public UpdateService(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("RetroGamingCollection/" + AppVersion.Current.Display);
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<AvailableUpdate?> CheckAsync(ReleaseVersion current, CancellationToken cancellationToken = default)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _client.GetAsync($"https://api.github.com/repos/{AppVersion.Repository}/releases?per_page=100", requestTimeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("The public update repository is not available yet.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestTimeout.Token));
        var releases = new List<(ReleaseVersion Version, JsonElement Release)>();
        foreach (var release in json.RootElement.EnumerateArray())
            if (!release.GetProperty("draft").GetBoolean() && !release.GetProperty("prerelease").GetBoolean() &&
                ReleaseVersion.TryParse(release.GetProperty("tag_name").GetString(), out var version) && version.Counter > current.Counter)
                releases.Add((version, release));
        foreach (var item in releases.OrderByDescending(r => r.Version.Counter))
        {
            var filename = $"RetroGamingCollection-Setup-{item.Version.Display}-win-x64.exe";
            var assets = item.Release.GetProperty("assets").EnumerateArray().ToArray();
            var installer = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == filename);
            var sums = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt");
            // Unfinished/draft assets must never appear as an installable update.
            if (installer.ValueKind == JsonValueKind.Undefined || sums.ValueKind == JsonValueKind.Undefined) continue;
            var installerUrl = new Uri(installer.GetProperty("browser_download_url").GetString()!);
            var sumsUrl = new Uri(sums.GetProperty("browser_download_url").GetString()!);
            ValidateUrl(installerUrl, item.Version, filename);
            ValidateUrl(sumsUrl, item.Version, "SHA256SUMS.txt");
            var size = installer.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaxInstallerBytes) throw new InvalidDataException("The update installer has an invalid size.");
            var notes = string.Join("\n\n", releases.Where(r => r.Version.Counter <= item.Version.Counter).OrderByDescending(r => r.Version.Counter)
                .Select(r => $"Version {r.Version.Display}\n{(r.Release.TryGetProperty("body", out var body) ? body.GetString() : null) ?? "No release notes supplied."}"));
            if (notes.Length > 100_000) notes = notes[..100_000] + "\n(Additional notes are available on GitHub.)";
            var digest = installer.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
            return new(item.Version, notes, installerUrl, size, digest, sumsUrl);
        }
        return null;
    }

    private static void ValidateUrl(Uri uri, ReleaseVersion version, string name)
    {
        var expected = $"/{AppVersion.Repository}/releases/download/v{version.Display}/{name}";
        if (uri.Scheme != "https" || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !Uri.UnescapeDataString(uri.AbsolutePath).Equals(expected, StringComparison.Ordinal) || uri.Query.Length != 0)
            throw new InvalidDataException("The update asset does not belong to the expected GitHub release.");
    }

    public async Task<string> DownloadAsync(AvailableUpdate update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var name = $"RetroGamingCollection-Setup-{update.Version.Display}-win-x64.exe";
        ValidateUrl(update.InstallerUrl, update.Version, name);
        ValidateUrl(update.ChecksumsUrl, update.Version, "SHA256SUMS.txt");
        if (update.Size <= 0 || update.Size > MaxInstallerBytes) throw new InvalidDataException("Invalid installer size.");
        var checksumText = await _client.GetStringAsync(update.ChecksumsUrl, cancellationToken);
        if (checksumText.Length > 16_384) throw new InvalidDataException("Invalid checksum file.");
        var line = checksumText.Split('\n').Select(l => Regex.Match(l.Trim(), @"^([A-Fa-f0-9]{64})\s+\*?(.+)$"))
            .FirstOrDefault(m => m.Success && m.Groups[2].Value == name);
        if (line is null) throw new InvalidDataException("No checksum was published for this installer.");
        var expectedHash = line.Groups[1].Value;
        if (update.Digest is not null && !update.Digest.Equals("sha256:" + expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The GitHub asset digest does not match the release checksum.");
        var directory = Path.Combine(AppPaths.DataRoot, "Updates", update.Version.Display);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, name);
        var partial = destination + ".partial";
        try
        {
            using var response = await _client.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920]; long received = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    received += count;
                    if (received > update.Size || received > MaxInstallerBytes) throw new InvalidDataException("Installer download exceeded its published size.");
                    await output.WriteAsync(buffer.AsMemory(0,count), cancellationToken);
                    progress?.Report((double)received / update.Size * 100);
                }
                if (received != update.Size) throw new InvalidDataException("The installer download is incomplete.");
            }
            await using (var file = File.OpenRead(partial))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
                if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer verification failed. Nothing was installed.");
            }
            File.Move(partial, destination, true);
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
