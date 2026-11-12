using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

public interface ILookupProvider
{
    string Vendor { get; }
    bool IsConfigured { get; }
    Task<DeviceResult> LookupAsync(string serial, CancellationToken ct);
}

public interface ILookupService
{
    Task<DeviceResult> LookupAsync(string serial, VendorChoice vendor, CancellationToken ct);
    void FlushCache();
}

/// <summary>Everything that needs a real desktop: dialogs, clipboard, opening links.</summary>
public interface IPlatform
{
    string? PickOpenFile(string filter);
    string? PickSaveFile(string defaultName, string filter);
    void SetClipboard(string text);
    void OpenPath(string pathOrUrl);
}

public interface IUpdateService
{
    bool IsInstalled { get; }
    /// <summary>Returns the new version text, or null when already up to date.</summary>
    Task<string?> CheckAsync(CancellationToken ct);
    /// <summary>Downloads the update found by CheckAsync and restarts into it.</summary>
    Task DownloadAndRestartAsync(Action<int> progress, CancellationToken ct);
}

public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedText);
}

public sealed class NoUpdateService : IUpdateService
{
    public bool IsInstalled => false;
    public Task<string?> CheckAsync(CancellationToken ct) => Task.FromResult<string?>(null);
    public Task DownloadAndRestartAsync(Action<int> progress, CancellationToken ct) => Task.CompletedTask;
}

public static class SerialNormalizer
{
    public static string Normalize(string? input) =>
        string.Concat((input ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static bool IsValid(string s) =>
        s.Length is >= 4 and <= 24 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>Dell service tags are 7 letters/digits. Lenovo serials are usually 8.</summary>
    public static bool LooksLikeDell(string s) => s.Length == 7 && s.All(char.IsAsciiLetterOrDigit);
}
