using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;

namespace DeviceLookup.Core.ViewModels;

public sealed class AboutViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly Func<Task> _checkForUpdates;

    public AboutViewModel(AppServices services, Func<Task> checkForUpdates)
    {
        _s = services;
        _checkForUpdates = checkForUpdates;
        OpenLogCommand = new RelayCommand(_ =>
        {
            Directory.CreateDirectory(AppLogger.Folder);
            _s.Platform.OpenPath(AppLogger.Folder);
        });
        OpenProjectCommand = new RelayCommand(_ => _s.Platform.OpenPath(AppInfo.ProjectUrl));
        CheckUpdatesCommand = new AsyncRelayCommand(_ => _checkForUpdates());
    }

    public RelayCommand OpenLogCommand { get; }
    public RelayCommand OpenProjectCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }

    public string VersionText => $"Version {AppInfo.Version}";
    public string ChangelogText => Changelog.Text;
    public string Footer => $"© {DateTime.Now.Year} Device Lookup. Not affiliated with Lenovo or Dell.";
}
