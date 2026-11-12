using System.Security.Cryptography;
using System.Text;

namespace DeviceLookup.Core.Services;

/// <summary>Downloads a product picture once and keeps it on disk.</summary>
public sealed class DeviceImageService
{
    private const int MaxBytes = 3 * 1024 * 1024;
    private readonly string _folder;
    private readonly HttpClient _http;

    public DeviceImageService(string folder, HttpMessageHandler? handler = null)
    {
        _folder = folder;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public int Count => Directory.Exists(_folder) ? Directory.GetFiles(_folder).Length : 0;

    public async Task<string?> GetAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
        try
        {
            var file = Path.Combine(_folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".img");
            if (File.Exists(file)) return file;

            using var resp = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            if (resp.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length is 0 or > MaxBytes) return null;

            Directory.CreateDirectory(_folder);
            await File.WriteAllBytesAsync(file, bytes, ct).ConfigureAwait(false);
            return file;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLogger.Error($"Image download failed: {url}", ex);
            return null;
        }
    }

    public void Clear()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch (Exception ex) { AppLogger.Error("Clear image cache", ex); }
    }
}
