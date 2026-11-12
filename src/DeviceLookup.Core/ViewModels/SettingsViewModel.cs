using DeviceLookup.Core.Models;
using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;

namespace DeviceLookup.Core.ViewModels;

/// <summary>Every change is saved straight away, so there is no Save button to forget.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _s;
    private bool _testing;

    public SettingsViewModel(AppServices services)
    {
        _s = services;
        TestCommand = new AsyncRelayCommand(_ => TestAsync(), _ => !_testing);
        ClearCacheCommand = new RelayCommand(_ => ClearCache());
    }

    public int[] ConcurrencyOptions { get; } = { 1, 2, 3, 4, 6, 8, 10 };
    public int[] CacheDayOptions { get; } = { 1, 3, 7, 14, 30, 90 };
    public AsyncRelayCommand TestCommand { get; }
    public RelayCommand ClearCacheCommand { get; }

    public bool LenovoEnabled { get => _s.Settings.LenovoEnabled; set => Change(() => _s.Settings.LenovoEnabled = value); }
    public bool DellEnabled { get => _s.Settings.DellEnabled; set => Change(() => _s.Settings.DellEnabled = value); }
    public bool UseCache { get => _s.Settings.UseCache; set => Change(() => _s.Settings.UseCache = value); }
    public int ConcurrentLookups { get => _s.Settings.ConcurrentLookups; set => Change(() => _s.Settings.ConcurrentLookups = value); }
    public int CacheDays { get => _s.Settings.CacheDays; set => Change(() => _s.Settings.CacheDays = value); }
    public string DellClientId { get => _s.Settings.DellClientId; set => Change(() => _s.Settings.DellClientId = value.Trim()); }
    public string DellClientSecret { get => _s.Settings.DellClientSecret; set => Change(() => _s.Settings.DellClientSecret = value); }

    public bool IsPublicMode { get => Mode == DellMode.PublicOnly; set { if (value) Mode = DellMode.PublicOnly; } }
    public bool IsApiPreferred { get => Mode == DellMode.ApiPreferred; set { if (value) Mode = DellMode.ApiPreferred; } }
    public bool IsApiOnly { get => Mode == DellMode.ApiOnly; set { if (value) Mode = DellMode.ApiOnly; } }

    private DellMode Mode
    {
        get => _s.Settings.DellMode;
        set
        {
            if (_s.Settings.DellMode == value) return;
            _s.Settings.DellMode = value;
            _s.SaveSettings();
            OnPropertyChanged(nameof(IsPublicMode));
            OnPropertyChanged(nameof(IsApiPreferred));
            OnPropertyChanged(nameof(IsApiOnly));
        }
    }

    public string CacheInfo => $"{_s.Cache?.Count ?? 0} devices cached · {_s.Images?.Count ?? 0} pictures";

    private void Change(Action apply)
    {
        apply();
        _s.SaveSettings();
    }

    private async Task TestAsync()
    {
        if (_s.Dell == null) return;
        _testing = true;
        var toast = _s.Toasts.Progress("Testing Dell API…", "Signing in with your credentials.");
        try
        {
            var (ok, message) = await _s.Dell.TestConnectionAsync(CancellationToken.None);
            _s.Toasts.Finish(toast, ok ? ToastKind.Success : ToastKind.Error,
                ok ? "Dell API connected" : "Dell API test failed", message, ok ? 5 : 9);
        }
        finally { _testing = false; }
    }

    private void ClearCache()
    {
        _s.Cache?.Clear();
        _s.Images?.Clear();
        OnPropertyChanged(nameof(CacheInfo));
        _s.Toasts.Success("Cache cleared");
    }
}
