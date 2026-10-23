using Microsoft.UI.Xaml;

namespace EdgeOpacity;

public partial class App : Application
{
    private Window? _window;
    private Mutex? _singleInstance;
    public App()
    {
        UnhandledException += (_, args) =>
        {
            WriteError(args.Exception);
            (_window as MainWindow)?.RestoreOnFailure();
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, "Local\\EdgeOpacity.v1", out bool firstInstance);
        if (!firstInstance)
        {
            var existing = Native.FindWindow(null, "Edge 透明度");
            if (existing != 0)
            {
                Native.ShowWindow(existing, 9);
                Native.SetForegroundWindow(existing);
            }
            Exit();
            return;
        }
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception error)
        {
            WriteError(error);
            throw;
        }
    }
    private static void WriteError(Exception error)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgeOpacity");
            Directory.CreateDirectory(folder);
            var details = new System.Text.StringBuilder($"{DateTimeOffset.Now:O}\n{error}\n");
            foreach (System.Collections.DictionaryEntry entry in error.Data)
                details.AppendLine($"{entry.Key}: {entry.Value}");
            File.AppendAllText(Path.Combine(folder, "error.log"), details + "\n");
        }
        catch { /* Error logging must not block restoring browser windows. */ }
    }
}
