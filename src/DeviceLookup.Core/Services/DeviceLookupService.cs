using System.Diagnostics;
using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

/// <summary>Picks the right vendor, uses the cache, and falls back between vendors in Auto mode.</summary>
public sealed class DeviceLookupService : ILookupService
{
    private readonly ILookupProvider _lenovo;
    private readonly ILookupProvider _dell;
    private readonly Func<AppSettings> _settings;
    private readonly ResultCache? _cache;

    public DeviceLookupService(ILookupProvider lenovo, ILookupProvider dell, Func<AppSettings> settings, ResultCache? cache)
    {
        _lenovo = lenovo;
        _dell = dell;
        _settings = settings;
        _cache = cache;
    }

    public void FlushCache() => _cache?.Flush();

    public async Task<DeviceResult> LookupAsync(string input, VendorChoice vendor, CancellationToken ct)
    {
        var serial = SerialNormalizer.Normalize(input);
        var sw = Stopwatch.StartNew();
        var result = await LookupCoreAsync(serial, vendor, ct).ConfigureAwait(false);
        sw.Stop();
        result.LookupTime = DateTime.Now;
        result.Duration = sw.Elapsed;
        return result;
    }

    private async Task<DeviceResult> LookupCoreAsync(string serial, VendorChoice vendor, CancellationToken ct)
    {
        var s = _settings();
        if (!SerialNormalizer.IsValid(serial))
            return Fail(serial, "", "That doesn't look like a serial number or service tag.");

        if (s.UseCache && _cache != null && _cache.TryGet(serial, s.CacheDays, out var cached)
            && (vendor == VendorChoice.Auto || string.Equals(cached.Manufacturer, vendor.ToString(), StringComparison.OrdinalIgnoreCase)))
            return cached;

        var order = new List<ILookupProvider>();
        switch (vendor)
        {
            case VendorChoice.Lenovo: order.Add(_lenovo); break;
            case VendorChoice.Dell: order.Add(_dell); break;
            default:
                if (SerialNormalizer.LooksLikeDell(serial)) { order.Add(_dell); order.Add(_lenovo); }
                else { order.Add(_lenovo); order.Add(_dell); }
                break;
        }

        DeviceResult? firstFailure = null;
        var skippedDell = false;
        foreach (var provider in order)
        {
            var isDell = provider == _dell;
            if (!(isDell ? s.DellEnabled : s.LenovoEnabled))
            {
                if (vendor != VendorChoice.Auto) return Fail(serial, provider.Vendor, $"{provider.Vendor} lookup is turned off in Settings.");
                continue;
            }
            if (isDell && s.DellMode == DellMode.PublicOnly)
            {
                if (vendor != VendorChoice.Auto)
                    return Fail(serial, "Dell", "Dell's public lookup page can't be used by apps. Choose an API mode in Settings and add your TechDirect credentials.");
                skippedDell = true;
                continue;
            }
            if (!provider.IsConfigured)
            {
                if (vendor != VendorChoice.Auto)
                    return Fail(serial, provider.Vendor, "Dell API credentials are not set. Add them in Settings.");
                skippedDell = true;
                continue;
            }

            var r = await provider.LookupAsync(serial, ct).ConfigureAwait(false);
            if (r.Status == LookupStatus.Found)
            {
                if (s.UseCache) _cache?.Store(r);
                return r;
            }
            if (r.Status == LookupStatus.Failed) firstFailure ??= r;
        }

        if (firstFailure != null) return firstFailure;
        var nf = new DeviceResult { SerialNumber = serial, Manufacturer = "Unknown", Status = LookupStatus.NotFound };
        nf.Error = skippedDell && SerialNormalizer.LooksLikeDell(serial)
            ? "Not found at Lenovo. This looks like a Dell service tag. Add Dell API credentials in Settings to check Dell."
            : "Serial number not found.";
        return nf;
    }

    private static DeviceResult Fail(string serial, string vendor, string message) => new()
    {
        SerialNumber = serial, Manufacturer = vendor, Status = LookupStatus.Failed, Error = message
    };
}
