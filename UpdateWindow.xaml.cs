using System.Windows;

namespace RetroGamingCollection;

public partial class UpdateWindow : Window
{
    private readonly UpdateService _service;
    private readonly AvailableUpdate _update;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _closed;
    public string? InstallerPath { get; private set; }
    public UpdateWindow(UpdateService service, AvailableUpdate update)
    {
        InitializeComponent();
        _service=service; _update=update;
        VersionText.Text=$"Version {AppVersion.Current.Display} → {update.Version.Display}";
        NotesBox.Text=update.Notes;
        Closed += (_,_) => { _closed=true; _cancellation.Cancel(); };
    }
    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled=false;
        DownloadProgress.Visibility=Visibility.Visible;
        DownloadStatus.Text="Downloading update...";
        try
        {
            var progress=new Progress<double>(value => { if (!_closed) { DownloadProgress.Value=value; DownloadStatus.Text=$"Downloading and verifying... {value:0}%"; } });
            InstallerPath=await _service.DownloadAsync(_update,progress,_cancellation.Token);
            if (!_closed) DialogResult=true;
        }
        catch (OperationCanceledException) { if (!_closed) DownloadStatus.Text="Download cancelled. Nothing was installed."; }
        catch (Exception ex)
        {
            if (!_closed) { DownloadStatus.Text=$"Update could not be downloaded: {ex.Message}"; InstallButton.IsEnabled=true; }
        }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
