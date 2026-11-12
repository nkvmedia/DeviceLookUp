using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;
using DeviceLookup.Core.ViewModels;
using DeviceLookup.Services;
using Velopack;

namespace DeviceLookup;

public partial class App : Application
{
    // Must run before anything else: lets Velopack handle install/update/uninstall launches.
    static App() => VelopackApp.Build().Run();

    private MainViewModel? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Ui.Context = SynchronizationContext.Current;

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("Unhandled UI exception", args.Exception);
            _vm?.Toasts.Error("Something went wrong", "The error was written to the log. You can keep working.");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        AppLogger.Info($"Starting Device Lookup {AppInfo.Version}");
        var services = AppServices.Create(new WpfPlatform(), new VelopackUpdateService(), new DpapiProtector());
        _vm = new MainViewModel(services);
        var window = new MainWindow { DataContext = _vm };
        MainWindow = window;
        window.Show();

        var cmdLine = e.Args;
        var shots = Array.IndexOf(cmdLine, "--screenshots");
        if (shots >= 0 && shots + 1 < cmdLine.Length)
            _ = TakeScreenshotsAsync(window, _vm, cmdLine[shots + 1]);
        else
            _ = _vm.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Bulk.StopCommand.Execute(null);
        base.OnExit(e);
    }

    /// <summary>
    /// "DeviceLookup.exe --screenshots out" renders every screen with sample data to PNG files,
    /// so the real UI can be compared with the design without anyone running the app by hand.
    /// </summary>
    private async Task TakeScreenshotsAsync(MainWindow window, MainViewModel vm, string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            vm.Single.ShowDemo(DemoData.T14(), "PF4Q9WYF");
            vm.Bulk.LoadRows(DemoData.BulkRows(), "laptops_inventory.csv");
            vm.Bulk.SetElapsed("2m 14s");
            vm.Scan.LoadDemo(DemoData.ScanRows());

            var pages = new (string Name, ObservableObject Page)[]
            {
                ("1-single", vm.Single), ("2-bulk", vm.Bulk), ("3-scan", vm.Scan), ("4-settings", vm.Settings), ("5-about", vm.About)
            };
            foreach (var (name, page) in pages)
            {
                vm.Navigate(page);
                await Settle();
                Capture(window, Path.Combine(folder, name + ".png"));
            }

            vm.Navigate(vm.Single);
            vm.Toasts.Progress("Looking up PF4Q9WYF…", "Please wait while we retrieve device information.");
            vm.Toasts.Show(ToastKind.Success, "Lookup completed", "Lenovo ThinkPad T14 Gen 3");
            vm.Toasts.Show(ToastKind.Error, "Lookup failed", "Service tag not found or unavailable.");
            vm.Toasts.Show(ToastKind.Info, "Bulk lookup completed", "115 found · 0 failed · 1 not found");
            await Settle();
            Capture(window, Path.Combine(folder, "6-notifications.png"));
            AppLogger.Info("Screenshots written to " + folder);
        }
        catch (Exception ex) { AppLogger.Error("Screenshot mode failed", ex); }
        finally { Shutdown(); }
    }

    private static async Task Settle()
    {
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Capture(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.UpdateLayout();
        var width = (int)Math.Ceiling(root.ActualWidth);
        var height = (int)Math.Ceiling(root.ActualHeight);
        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
