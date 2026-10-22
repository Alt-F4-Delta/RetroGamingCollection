using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace RetroGamingCollection;

public partial class MainWindow : Window
{
    private List<GameEntry> _catalog = [];
    private readonly SettingsStore _settingsStore = new();
    private readonly RetroArchService _retroArch = new();
    private LauncherSettings _settings;
    private Process? _runningProcess;
    private bool _scanning;
    private bool _filtersReady;

    public MainWindow()
    {
        InitializeComponent();
        Title=$"RetroGaming Collection v{AppVersion.Current.Display}";
        _settings = _settingsStore.Load();
        _catalog = _settingsStore.LoadLibrary();
        ExternalDrive.Reconnect(_settings, _catalog);
        _settings.RetroArchPath ??= RetroArchService.DiscoverExecutable();
        PopulateLanguageChoices();
        UsaOnlyBox.IsChecked = _settings.UsaOnly;
        _filtersReady = true;
        Loaded += async (_, _) =>
        {
            RefreshList();
            if (_catalog.Count == 0) await ScanConfiguredLibrariesAsync();
            else StatusText.Text = $"{_catalog.Count:N0} verified files registered. Connect your library drive to play; RESCAN refreshes files.";
        };
    }

    private void RefreshList()
    {
        if (GameList is null || SearchBox is null) return;
        var query = SearchBox.Text.Trim();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var selectedPath = (GameList.SelectedItem as GameEntry)?.FilePath;
        var language = LanguageFilterBox?.SelectedValue as string ?? "Untagged";
        var usaOnly = UsaOnlyBox?.IsChecked ?? true;
        GameList.ItemsSource = _catalog.Where(g => LibraryFilters.Matches(g, language, usaOnly) && terms.All(t => $"{g.Title} {g.Platform}".Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(g => LibraryScanner.Remembered(g.Title)).ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Platform).ToList();
        if (FilterCountText is not null) FilterCountText.Text = $"{GameList.Items.Count:N0} of {_catalog.Count:N0} files shown";
        if (GameList.Items.Count > 0)
            GameList.SelectedItem = GameList.Items.Cast<GameEntry>().FirstOrDefault(g => selectedPath is not null && g.FilePath == selectedPath) ?? GameList.Items[0];
        else if (DetailsPanel is not null)
        {
            DetailsPanel.Children.Clear();
            DetailsPanel.Children.Add(new TextBlock { Text = "NO MATCHING GAMES", FontSize = 24 });
            DetailsPanel.Children.Add(new TextBlock { Text = "Change the language filter, uncheck USA only, or clear your search.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,0) });
        }
    }
    private void PopulateLanguageChoices()
    {
        LanguageFilterBox.ItemsSource = LibraryFilters.Choices(_catalog);
        LanguageFilterBox.SelectedValue = _settings.LanguageFilter;
        if (LanguageFilterBox.SelectedIndex < 0) LanguageFilterBox.SelectedValue = "Untagged";
    }
    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();
    private void RegionFilter_Changed(object sender, RoutedEventArgs e) => ApplyFilters();
    private void ApplyFilters()
    {
        if (!_filtersReady) return;
        _settings.LanguageFilter = LanguageFilterBox.SelectedValue as string ?? "Untagged";
        _settings.UsaOnly = UsaOnlyBox.IsChecked == true;
        RefreshList();
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = $"Filter applied; unable to save preference: {ex.Message}"; }
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();
    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is not GameEntry game) return;
        DetailsPanel.Children.Clear();
        DetailsPanel.Children.Add(new TextBlock { Text = game.Title, FontSize = 32, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White });
        DetailsPanel.Children.Add(new TextBlock { Text = $"{game.Platform}  |  {game.Status}", Margin = new Thickness(0, 10, 0, 0), Foreground = System.Windows.Media.Brushes.LightSkyBlue });
        DetailsPanel.Children.Add(new TextBlock { Text = game.Confidence, Margin = new Thickness(0, 22, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 15 });
        var launch = new System.Windows.Controls.Button { Content = game.IsInstalled ? "LAUNCH GAME" : "FILE NOT IMPORTED", IsEnabled = game.IsInstalled, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 24, 0, 0) };
        launch.Click += (_, _) => Launch(game);
        DetailsPanel.Children.Add(launch);
        DetailsPanel.Children.Add(new TextBlock { Text = $"ROM header: {game.InternalTitle}\n\n{game.FilePath}\n{game.ArchiveMember}\n\nArrows: move | Enter: start | Right Shift: select\nSNES: Z/X/A/S = B/A/Y/X; Q/W = L/R\nGenesis: A/Z/X = A/B/C; Q/S/W = X/Y/Z\nEscape: quit; window Close also returns here\nF1: emulator menu for controls", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,20,0,0), FontSize = 13 });
    }
    private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if (GameList.SelectedItem is GameEntry game) Launch(game); }
    private void GameList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter && GameList.SelectedItem is GameEntry game) Launch(game); }
    private void Launch(GameEntry game)
    {
        if (_runningProcess is { HasExited: false }) { System.Windows.MessageBox.Show("A game is already running.", "RetroGaming", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        try
        {
            ExternalDrive.Reconnect(_settings, _catalog);
            _runningProcess = _retroArch.Launch(game, _settings);
            if (_runningProcess is null) throw new InvalidOperationException("Windows did not start RetroArch.");
            game.LastPlayedUtc = DateTime.UtcNow; game.PlayCount++; _settingsStore.Save(_settings); _settingsStore.SaveLibrary(_catalog);
            StatusText.Text = $"Playing {game.Title}. Close RetroArch to return here.";
            _ = WatchProcessAsync(_runningProcess, game);
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Unable to launch game", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async Task WatchProcessAsync(Process process, GameEntry game)
    {
        await process.WaitForExitAsync();
        await Dispatcher.InvokeAsync(() => { _runningProcess = null; StatusText.Text = process.ExitCode == 0 ? $"Returned from {game.Title}." : $"Emulator exited with code {process.ExitCode}. Check emulator configuration."; process.Dispose(); Activate(); GameList.Focus(); });
    }
    private async Task ScanConfiguredLibrariesAsync()
    {
        if (_scanning) return;
        _scanning = true;
        try
        {
            ExternalDrive.Reconnect(_settings, _catalog);
            StatusText.Text = "Checking cartridge headers and ZIP members...";
            foreach (var path in _settings.LibraryPaths.Where(Directory.Exists))
            {
                var found = await LibraryScanner.ScanAsync(path);
                var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
                var previous = _catalog.Where(g => g.FilePath is not null).ToDictionary(g => g.FilePath!, StringComparer.OrdinalIgnoreCase);
                foreach (var game in found) if (previous.TryGetValue(game.FilePath!, out var old)) { game.LastPlayedUtc=old.LastPlayedUtc; game.PlayCount=old.PlayCount; }
                _catalog.RemoveAll(g => g.FilePath?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);
                _catalog.AddRange(found);
            }
            _settingsStore.Save(_settings); _settingsStore.SaveLibrary(_catalog);
            _filtersReady = false;
            PopulateLanguageChoices();
            _filtersReady = true;
            RefreshList();
            var missing = _settings.LibraryPaths.Count(p => !Directory.Exists(p));
            StatusText.Text = $"{_catalog.Count:N0} verified library files. {missing} library folder(s) offline. Search a title or platform.";
        }
        catch (Exception ex) { StatusText.Text = $"Scan could not finish: {ex.Message}. Existing registrations retained."; }
        finally { _scanning = false; }
    }
    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanConfiguredLibrariesAsync();
    private async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "Choose a folder containing legally obtained game files" };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        if (!_settings.LibraryPaths.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase)) _settings.LibraryPaths.Add(dialog.SelectedPath);
        _settingsStore.Save(_settings);
        await ScanConfiguredLibrariesAsync();
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var executable = _settings.RetroArchPath ?? "Not configured";
        var snes = _settings.SnesCorePath ?? "Not configured";
        var genesis = _settings.GenesisCorePath ?? "Not configured";
        System.Windows.MessageBox.Show($"RetroArch: {executable}\nSNES core: {snes}\nGenesis core: {genesis}\n\nLibraries:\n{string.Join("\n", _settings.LibraryPaths)}\n\nEdit {SettingsStore.DataDirectory}\\settings.json to set paths, then restart.\nSaves and configuration are local to the launcher. Original drive saves are preserved.", "RetroArch Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}

static class Catalog
{
    public static List<GameEntry> Create() => [
        new() { Title = "Looney Tunes B-Ball", Platform = "SNES", Year = 1995, Confidence = "Screenshot-confirmed priority title." },
        new() { Title = "Mortal Kombat 3", Platform = "SNES (candidate)", Year = 1995, Confidence = "Screenshot-confirmed title; platform/version remains configurable." },
        new() { Title = "Street Fighter II: The World Warrior", Platform = "SNES", Year = 1992, Confidence = "Recalled title; edition not fully confirmed." },
        new() { Title = "Street Fighter II Turbo: Hyper Fighting", Platform = "SNES", Year = 1993, Confidence = "Recalled title; edition not fully confirmed." },
        new() { Title = "Super Street Fighter II: The New Challengers", Platform = "SNES", Year = 1994, Confidence = "Recalled title; edition not fully confirmed." },
        new() { Title = "Disney's Aladdin", Platform = "SNES / Genesis", Year = 1993, Confidence = "Recalled title; platform remains uncertain." },
        new() { Title = "Contra III: The Alien Wars", Platform = "SNES", Year = 1992, Confidence = "Recalled candidate title." },
        new() { Title = "Contra: Hard Corps", Platform = "Genesis", Year = 1994, Confidence = "Recalled candidate title." },
        new() { Title = "Spider-Man", Platform = "SNES / Genesis", Year = 1991, Confidence = "Recalled title; edition remains uncertain." },
        new() { Title = "Spider-Man and Venom: Maximum Carnage", Platform = "SNES / Genesis", Year = 1994, Confidence = "Recalled title; platform remains uncertain." },
        new() { Title = "NBA Jam", Platform = "SNES / Genesis", Year = 1993, Confidence = "Recalled title; platform remains uncertain." },
        new() { Title = "NBA Jam Tournament Edition", Platform = "SNES / Genesis", Year = 1995, Confidence = "Recalled title; platform remains uncertain." },
        new() { Title = "NBA Live 95", Platform = "SNES / Genesis", Year = 1994, Confidence = "Recalled title; platform remains uncertain." },
        new() { Title = "The Need for Speed", Platform = "DOS / Windows", Year = 1994, Confidence = "Recalled title; not an SNES release and not currently launchable." }];
}
