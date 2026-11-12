using System.Net;
using System.Text.Json;
using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

public sealed class LenovoProvider : ILookupProvider
{
    private const string Host = "https://pcsupport.lenovo.com";
    private readonly HttpClient _http;
    private readonly TimeSpan _retryDelay;

    public LenovoProvider(HttpMessageHandler? handler = null, TimeSpan? retryDelay = null)
    {
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(400);
        _http = new HttpClient(handler ?? new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All
        })
        { Timeout = TimeSpan.FromSeconds(20) };
    }

    public string Vendor => "Lenovo";
    public bool IsConfigured => true;

    public async Task<DeviceResult> LookupAsync(string serial, CancellationToken ct)
    {
        var result = new DeviceResult { SerialNumber = serial, Manufacturer = "Lenovo" };
        try
        {
            var product = await FindProductAsync(serial, ct).ConfigureAwait(false);
            if (product is not { } p)
            {
                result.Status = LookupStatus.NotFound;
                result.Error = "Serial number not found in Lenovo's database.";
                return result;
            }

            var id = Str(p, "Id");
            var name = Str(p, "Name");
            result.Product = name.Split(" (")[0].Trim();
            var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                result.Model = parts[^3];
                result.Mtm = parts[^2];
            }
            result.ImageUrl = FindImageUrl(p);
            result.Status = LookupStatus.Found;

            try { await ReadWarrantyAsync(id, result, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                AppLogger.Error($"Lenovo warranty failed for {serial}", ex);
                result.WarrantyNote = "Unavailable";
                result.Error = "Device found, but warranty details could not be read.";
            }
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            AppLogger.Error($"Lenovo lookup failed for {serial}", ex);
            result.Status = LookupStatus.Failed;
            result.Error = Friendly(ex);
            return result;
        }
    }

    private async Task<JsonElement?> FindProductAsync(string serial, CancellationToken ct)
    {
        var url = $"{Host}/gb/en/api/v4/mse/getproducts?productId={Uri.EscapeDataString(serial)}";
        // Lenovo sometimes answers with an empty list on the first try.
        for (var i = 1; i <= 3; i++)
        {
            var json = await HttpRetry.GetStringAsync(_http, url, _retryDelay, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var list = doc.RootElement;
            if (list.ValueKind == JsonValueKind.Object && list.TryGetProperty("value", out var v)) list = v;
            if (list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
                return list[0].Clone();
            if (i < 3) await Task.Delay(_retryDelay * i, ct).ConfigureAwait(false);
        }
        return null;
    }

    private async Task ReadWarrantyAsync(string productId, DeviceResult result, CancellationToken ct)
    {
        var url = $"{Host}/us/en/products/{productId.ToLowerInvariant()}/warranty";
        var html = await HttpRetry.GetStringAsync(_http, url, _retryDelay, ct).ConfigureAwait(false);
        var json = ExtractJson(html, "ds_warranties");
        if (json == null) { result.WarrantyNote = "Unavailable"; return; }

        using var doc = JsonDocument.Parse(json);
        var all = new List<(string Name, DateOnly? Start, DateOnly? End)>();
        foreach (var group in new[] { "ContractWarranties", "BaseWarranties", "UpmaWarranties" })
        {
            if (!doc.RootElement.TryGetProperty(group, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var w in arr.EnumerateArray())
                all.Add((Str(w, "Name"), ParseDate(Str(w, "Start")), ParseDate(Str(w, "End"))));
        }
        var best = all.Where(w => w.End != null).OrderByDescending(w => w.End).Cast<(string Name, DateOnly? Start, DateOnly? End)?>().FirstOrDefault();
        if (best is not { } b) { result.WarrantyNote = "N/A"; return; }
        result.WarrantyName = b.Name;
        result.WarrantyStart = b.Start;
        result.WarrantyEnd = b.End;
    }

    /// <summary>
    /// Pulls the JSON value assigned to a script variable out of a web page by matching
    /// braces (string-aware), so semicolons or braces inside text cannot break it.
    /// </summary>
    public static string? ExtractJson(string html, string variable)
    {
        var at = html.IndexOf(variable, StringComparison.Ordinal);
        if (at < 0) return null;
        var start = html.IndexOfAny(new[] { '{', '[' }, at + variable.Length);
        if (start < 0) return null;
        var depth = 0;
        var inString = false;
        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{' || c == '[') depth++;
            else if (c == '}' || c == ']')
            {
                depth--;
                if (depth == 0) return html.Substring(start, i - start + 1);
            }
        }
        return null;
    }

    private static string FindImageUrl(JsonElement p)
    {
        foreach (var key in new[] { "Image", "ImageUrl", "ProductImage", "Thumbnail" })
        {
            var s = Str(p, key);
            if (string.IsNullOrWhiteSpace(s)) continue;
            if (s.StartsWith("//")) return "https:" + s;
            if (s.StartsWith('/')) return Host + s;
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return s;
        }
        return "";
    }

    private static string Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return "";
        foreach (var prop in e.EnumerateObject())
            if (prop.NameEquals(name) || string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
        return "";
    }

    private static DateOnly? ParseDate(string s) =>
        DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? DateOnly.FromDateTime(d) : null;

    internal static string Friendly(Exception ex) => ex switch
    {
        TaskCanceledException => "The request timed out. Check your connection and try again.",
        HttpRequestException h when h.StatusCode == HttpStatusCode.TooManyRequests =>
            "Too many requests. Lower the number of concurrent lookups in Settings.",
        HttpRequestException h when h.StatusCode != null => $"The server answered with HTTP {(int)h.StatusCode}.",
        HttpRequestException => "Couldn't reach the server. Check your connection.",
        JsonException => "The server sent a response that couldn't be read.",
        _ => ex.Message
    };
}
