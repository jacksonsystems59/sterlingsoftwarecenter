using System.Diagnostics;
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
    public static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        bool startupTest = e.Args.Length == 2 && e.Args[0] == "--startup-test";
        bool uiTest = e.Args.Length == 2 && e.Args[0] == "--ui-tests";
        SmokeMode = startupTest || uiTest || e.Args.Length == 2 && e.Args[0] == "--smoke-test";
        if (!SmokeMode)
        {
            instance = new Mutex(true, "Local\\SterlingSoftwareCentre", out bool first);
            if (!first) { MessageBox.Show("Sterling Software Centre is already open in this session."); Shutdown(); return; }
        }
        DispatcherUnhandledException += (_, args) => { Directory.CreateDirectory(Storage.Home); File.AppendAllText(Path.Combine(Storage.Home, "crash.log"), args.Exception + Environment.NewLine); };
        try
        {
            if (uiTest) { await WindowWorkflowChecks.Run(e.Args[1]); Shutdown(0); return; }
            MainWindow window;
            double splashSeconds = 0;
            if (!SmokeMode || startupTest)
            {
                var splash = new SplashWindow(); MainWindow = splash; splash.Show();
                var timer = Stopwatch.StartNew(); var minimumDisplay = Task.Delay(TimeSpan.FromSeconds(4));
                await Dispatcher.InvokeAsync(() => splash.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (startupTest) Render(splash, e.Args[1] + ".splash.png");
                window = new MainWindow();
                await Task.WhenAll(minimumDisplay, window.InitializeAsync(splash.SetStatus));
                if (window.IsVisible) throw new InvalidOperationException("Main window was shown during splash preparation.");
                splashSeconds = timer.Elapsed.TotalSeconds;
                MainWindow = window; window.Show(); splash.Close();
            }
            else { window = new MainWindow(); MainWindow = window; window.Show(); }
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            await Dispatcher.InvokeAsync(() => window.UpdateLayout(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (e.Args.Length == 4 && e.Args[0] == "--after-update" && e.Args[2] == "--receipt") window.VerifyUpdatedVersion(e.Args[1], e.Args[3]);
            if (SmokeMode)
            {
                Render(window, e.Args[1]);
                Storage.Save(e.Args[1] + ".json", new { WpfRender = true, SplashSeconds = splashSeconds, Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription });
                if (startupTest && splashSeconds < 3.9) throw new InvalidOperationException("Splash display was shorter than four seconds.");
                Shutdown(0);
            }
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Storage.Home); File.WriteAllText(Path.Combine(Storage.Home, "startup-error.log"), ex.ToString());
            if (SmokeMode && e.Args.Length > 1) File.WriteAllText(e.Args[1] + ".error.txt", ex.ToString());
            if (!SmokeMode) MessageBox.Show("Sterling could not start: " + ex.Message);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
