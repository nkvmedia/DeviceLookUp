using DeviceLookup.Core.Models;
using DeviceLookup.Core.ViewModels;

namespace DeviceLookup.Core.Services;

/// <summary>Everything the view models need, wired in one place.</summary>
public sealed class AppServices
{
    public required AppSettings Settings { get; init; }
    public required SettingsStore Store { get; init; }
    public required ILookupService Lookup { get; init; }
    public required IPlatform Platform { get; init; }
    public required IUpdateService Updates { get; init; }
    public DellProvider? Dell { get; init; }
    public ResultCache? Cache { get; init; }
    public DeviceImageService? Images { get; init; }
    public ToastHost Toasts { get; } = new();

    public event Action? SettingsChanged;

    public void SaveSettings()
    {
        Store.Save(Settings);
        SettingsChanged?.Invoke();
    }

    public static AppServices Create(IPlatform platform, IUpdateService updates, ISecretProtector protector, string? dataDir = null)
    {
        var dir = dataDir ?? AppLogger.DataFolder;
        var store = new SettingsStore(Path.Combine(dir, "settings.json"), protector);
        var settings = store.Load();
        var cache = new ResultCache(Path.Combine(dir, "cache.json"));
        var lenovo = new LenovoProvider();
        var dell = new DellProvider(() => settings);
        return new AppServices
        {
            Settings = settings,
            Store = store,
            Cache = cache,
            Dell = dell,
            Images = new DeviceImageService(Path.Combine(dir, "images")),
            Lookup = new DeviceLookupService(lenovo, dell, () => settings, cache),
            Platform = platform,
            Updates = updates
        };
    }
}
