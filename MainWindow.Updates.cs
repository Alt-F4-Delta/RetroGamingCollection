using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RetroGamingCollection;

public partial class MainWindow
{
    private readonly UpdateService _updates = new();
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_runningProcess is { HasExited:false })
        { System.Windows.MessageBox.Show("Close the running game before updating the launcher.","Check for updates"); return; }
        CheckUpdatesButton.IsEnabled=false;
        StatusText.Text="Checking GitHub releases...";
        try
        {
            var update=await _updates.CheckAsync(AppVersion.Current);
            if (update is null)
            {
                StatusText.Text=$"Version {AppVersion.Current.Display} is up to date.";
                System.Windows.MessageBox.Show($"You have the latest published version ({AppVersion.Current.Display}).","Check for updates");
                return;
            }
            StatusText.Text=$"Version {update.Version.Display} is available.";
            var dialog=new UpdateWindow(_updates,update) { Owner=this };
            if (dialog.ShowDialog()!=true || dialog.InstallerPath is null) return;
            if (!File.Exists(dialog.InstallerPath)) throw new FileNotFoundException("The verified installer is no longer available.");
            var start=new ProcessStartInfo(dialog.InstallerPath) { UseShellExecute=true };
            start.ArgumentList.Add("/UPDATE=1");
            if (AppPaths.IsInstalled) start.ArgumentList.Add("/DIR="+Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            if (Process.Start(start) is null) throw new InvalidOperationException("Windows did not open Setup.");
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusText.Text="Could not check or install updates.";
            System.Windows.MessageBox.Show($"{ex.Message}\n\nYour current installation has not been changed.","Update unavailable",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        finally { CheckUpdatesButton.IsEnabled=true; }
    }
}
