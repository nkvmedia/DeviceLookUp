using System.Collections.ObjectModel;
using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;

namespace DeviceLookup.Core.ViewModels;

public sealed class NavItem
{
    public required string Title { get; init; }
    /// <summary>Segoe icon glyph (leave empty when IconPath is used).</summary>
    public string Glyph { get; init; } = "";
    /// <summary>Vector path for icons that have no glyph.</summary>
    public string IconPath { get; init; } = "";
    public required ObservableObject Page { get; init; }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _s;
    private NavItem _selected;
    private string? _pendingVersion;
    private bool _updating;
    private int _updatePercent;

    public MainViewModel(AppServices services)
    {
        _s = services;
        Single = new SingleLookupViewModel(services);
        Bulk = new BulkViewModel(services);
        Scan = new ScanViewModel(services);
        Settings = new SettingsViewModel(services);
        About = new AboutViewModel(services, () => CheckForUpdatesAsync(userInitiated: true));

        Items = new ObservableCollection<NavItem>
        {
            new() { Title = "Lookup", Glyph = "\uE721", Page = Single },
            new() { Title = "Bulk CSV", Glyph = "\uE14C", Page = Bulk },
            new() { Title = "Scan Mode", IconPath = "M1,2H2.5V14H1Z M4,2H5V14H4Z M7,2H9V14H7Z M11,2H12V14H11Z M14,2H15.5V14H14Z M17.5,2H19V14H17.5Z", Page = Scan },
            new() { Title = "Settings", Glyph = "\uE713", Page = Settings },
            new() { Title = "About", Glyph = "\uE946", Page = About },
        };
        _selected = Items[0];

        ApplyUpdateCommand = new AsyncRelayCommand(_ => ApplyUpdateAsync(), _ => _pendingVersion != null && !_updating);
        services.SettingsChanged += () => { OnPropertyChanged(nameof(LenovoOn)); OnPropertyChanged(nameof(DellOn)); };
    }

    public SingleLookupViewModel Single { get; }
    public BulkViewModel Bulk { get; }
    public ScanViewModel Scan { get; }
    public SettingsViewModel Settings { get; }
    public AboutViewModel About { get; }
    public ObservableCollection<NavItem> Items { get; }
    public ToastHost Toasts => _s.Toasts;
    public AsyncRelayCommand ApplyUpdateCommand { get; }

    public NavItem SelectedItem
    {
        get => _selected;
        set { if (value != null && Set(ref _selected, value)) OnPropertyChanged(nameof(CurrentPage)); }
    }

    public ObservableObject CurrentPage => _selected.Page;
    public string WindowTitle => $"Device Lookup v{AppInfo.Version}";

    public bool LenovoOn => _s.Settings.LenovoEnabled;
    public bool DellOn => _s.Settings.DellEnabled && _s.Settings.DellMode != Models.DellMode.PublicOnly && _s.Settings.DellConfigured;

    public bool HasUpdate => _pendingVersion != null;
    public string UpdateText => _updating ? $"Updating… {_updatePercent}%" : $"Update {_pendingVersion} available";

    public void Navigate(ObservableObject page) => SelectedItem = Items.First(i => i.Page == page);

    /// <summary>Called once the window is up. Failures are logged, never shown at startup.</summary>
    public async Task InitializeAsync()
    {
        try { await CheckForUpdatesAsync(userInitiated: false); }
        catch (Exception ex) { AppLogger.Error("Startup update check failed", ex); }
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (!_s.Updates.IsInstalled)
        {
            if (userInitiated)
                _s.Toasts.Info("Updates", "This copy wasn't installed with the installer, so it can't update itself.");
            return;
        }
        try
        {
            var version = await _s.Updates.CheckAsync(CancellationToken.None);
            _pendingVersion = version;
            OnPropertyChanged(nameof(HasUpdate));
            OnPropertyChanged(nameof(UpdateText));
            ApplyUpdateCommand.RaiseCanExecuteChanged();
            if (userInitiated)
            {
                if (version == null) _s.Toasts.Success("You're up to date", $"Version {AppInfo.Version}");
                else _s.Toasts.Info($"Update {version} is available", "Use the Update button in the sidebar.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update check failed", ex);
            if (userInitiated) _s.Toasts.Error("Couldn't check for updates", "Check your internet connection and try again.");
        }
    }

    private async Task ApplyUpdateAsync()
    {
        _updating = true;
        OnPropertyChanged(nameof(UpdateText));
        var toast = _s.Toasts.Progress("Downloading update…", $"Device Lookup {_pendingVersion}");
        try
        {
            await _s.Updates.DownloadAndRestartAsync(p =>
            {
                _updatePercent = p;
                Ui.Run(() => { toast.Message = $"{p}% downloaded"; OnPropertyChanged(nameof(UpdateText)); });
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update failed", ex);
            _s.Toasts.Finish(toast, ToastKind.Error, "Update failed", "Nothing was changed. Try again later.", 8);
        }
        finally
        {
            _updating = false;
            OnPropertyChanged(nameof(UpdateText));
        }
    }
}
