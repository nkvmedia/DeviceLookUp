using DeviceLookup.Core.Services;
using Velopack;
using Velopack.Sources;

namespace DeviceLookup.Services;

/// <summary>Checks GitHub Releases for a newer build and restarts into it.</summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly UpdateManager _manager = new(new GithubSource(AppInfo.ProjectUrl, null, false));
    private UpdateInfo? _pending;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<string?> CheckAsync(CancellationToken ct)
    {
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAndRestartAsync(Action<int> progress, CancellationToken ct)
    {
        if (_pending == null) return;
        await _manager.DownloadUpdatesAsync(_pending, progress);
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
