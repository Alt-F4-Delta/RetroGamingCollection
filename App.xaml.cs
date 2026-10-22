namespace RetroGamingCollection;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        try { AppPaths.Initialize(); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { System.Windows.MessageBox.Show($"Existing configuration could not be imported: {ex.Message}\nYou can import your library from the launcher.", "Configuration import"); }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
