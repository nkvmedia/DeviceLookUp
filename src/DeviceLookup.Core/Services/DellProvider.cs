using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

/// <summary>
/// Dell TechDirect "Warranty" API: OAuth2 client-credentials token, then asset-entitlements.
/// Written against Dell's documented response; verify against a live response once keys exist.
/// </summary>
public sealed class DellProvider : ILookupProvider
{
    private const string TokenUrl = "https://apigtwb2c.us.dell.com/auth/oauth/v2/token";
    private const string EntitlementsUrl = "https://apigtwb2c.us.dell.com/PROD/sbil/eapi/v5/asset-entitlements";

    private readonly HttpClient _http;
    private readonly Func<AppSettings> _settings;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private string _tokenFor = "";

    public DellProvider(Func<AppSettings> settings, HttpMessageHandler? handler = null, TimeSpan? retryDelay = null)
    {
        _settings = settings;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(400);
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public string Vendor => "Dell";
    public bool IsConfigured => _settings().DellConfigured;

    public async Task<DeviceResult> LookupAsync(string serial, CancellationToken ct)
    {
        var result = new DeviceResult { SerialNumber = serial, Manufacturer = "Dell" };
        try
        {
            if (!IsConfigured)
            {
                result.Status = LookupStatus.Failed;
                result.Error = "Dell API credentials are not set. Add them in Settings.";
                return result;
            }

            var token = await GetTokenAsync(ct).ConfigureAwait(false);
            var json = await GetEntitlementsAsync(serial, token, ct).ConfigureAwait(false);
            Parse(json, result);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLogger.Error($"Dell lookup failed for {serial}", ex);
            result.Status = LookupStatus.Failed;
            result.Error = ex is DellAuthException ? ex.Message : LenovoProvider.Friendly(ex);
            return result;
        }
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken ct)
    {
        if (!IsConfigured) return (false, "Enter a Client ID and Client Secret first.");
        try
        {
            await GetTokenAsync(ct, force: true).ConfigureAwait(false);
            return (true, "Dell API connection works.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return (false, ex is DellAuthException ? ex.Message : LenovoProvider.Friendly(ex));
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct, bool force = false)
    {
        var s = _settings();
        await _tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = s.DellClientId + "|" + s.DellClientSecret;
            if (!force && _token != null && key == _tokenFor && DateTime.UtcNow < _tokenExpiry) return _token;

            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = s.DellClientId,
                    ["client_secret"] = s.DellClientSecret,
                    ["grant_type"] = "client_credentials"
                })
            };
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new DellAuthException(resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.BadRequest
                    ? "Dell rejected the Client ID or Secret. Check them in Settings."
                    : $"Dell sign-in failed (HTTP {(int)resp.StatusCode}).");

            using var doc = JsonDocument.Parse(body);
            _token = doc.RootElement.GetProperty("access_token").GetString()
                     ?? throw new DellAuthException("Dell sign-in returned no token.");
            var seconds = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var n) ? n : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
            _tokenFor = key;
            return _token;
        }
        finally { _tokenGate.Release(); }
    }

    private async Task<string> GetEntitlementsAsync(string tag, string token, CancellationToken ct)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await HttpRetry.GetStringAsync(_http,
            $"{EntitlementsUrl}?servicetags={Uri.EscapeDataString(tag)}", _retryDelay, ct).ConfigureAwait(false);
    }

    /// <summary>Fills a result from Dell's asset-entitlements JSON.</summary>
    public static void Parse(string json, DeviceResult r)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var asset = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : default;
        if (asset.ValueKind != JsonValueKind.Object || Bool(asset, "invalid"))
        {
            r.Status = LookupStatus.NotFound;
            r.Error = "Service tag not found in Dell's database.";
            return;
        }

        var system = Str(asset, "systemDescription");
        var line = Str(asset, "productLineDescription");
        r.Product = !string.IsNullOrWhiteSpace(system) ? system : line;
        r.Model = line;
        r.Status = LookupStatus.Found;

        if (asset.TryGetProperty("entitlements", out var ents) && ents.ValueKind == JsonValueKind.Array)
        {
            var best = ents.EnumerateArray()
                .Select(e => (End: Date(Str(e, "endDate")), Start: Date(Str(e, "startDate")),
                              Name: FirstNonEmpty(Str(e, "serviceLevelDescription"), Str(e, "serviceLevelCode"))))
                .Where(x => x.End != null)
                .OrderByDescending(x => x.End)
                .Cast<(DateOnly? End, DateOnly? Start, string Name)?>()
                .FirstOrDefault();
            if (best is { } b)
            {
                r.WarrantyEnd = b.End;
                r.WarrantyStart = b.Start;
                r.WarrantyName = b.Name;
                return;
            }
        }
        r.WarrantyNote = "N/A";
    }

    private static string FirstNonEmpty(params string[] v) => v.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
    private static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Str(JsonElement e, string n) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static DateOnly? Date(string s) =>
        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? DateOnly.FromDateTime(d) : null;
}

public sealed class DellAuthException : Exception
{
    public DellAuthException(string message) : base(message) { }
}
