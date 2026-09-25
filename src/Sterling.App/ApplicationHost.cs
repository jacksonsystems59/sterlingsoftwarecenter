using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sterling.Core;
namespace Sterling.App;
public partial class App : Application
{
    public static bool SmokeMode { get; private set; }
    Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SmokeMode = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
        if (!SmokeMode)
        {
            instance = new Mutex(true, "Local\\SterlingSoftwareCentre", out bool first);
            if (!first) { MessageBox.Show("Sterling Software Centre is already open in this session."); Shutdown(); return; }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            Directory.CreateDirectory(Storage.Home);
            File.AppendAllText(Path.Combine(Storage.Home, "crash.log"), args.Exception + Environment.NewLine);
        };
        try
        {
            var window = new MainWindow(); MainWindow = window; window.Show();
            if (SmokeMode)
            {
                await Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                string path = Path.GetFullPath(e.Args[1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
                File.WriteAllText(path + ".json", "{\"wpfRender\":true,\"framework\":\"" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + "\"}");
                Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Storage.Home); File.WriteAllText(Path.Combine(Storage.Home, "startup-error.log"), ex.ToString());
            if (!SmokeMode) MessageBox.Show("Sterling could not start: " + ex.Message);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
