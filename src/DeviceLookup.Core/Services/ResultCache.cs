using System.Text.Json;
using DeviceLookup.Core.Models;

namespace DeviceLookup.Core.Services;

/// <summary>Small JSON file cache of successful lookups, keyed by serial number.</summary>
public sealed class ResultCache
{
    private sealed class Entry
    {
        public string Serial { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Product { get; set; } = "";
        public string Model { get; set; } = "";
        public string Mtm { get; set; } = "";
        public string WarrantyName { get; set; } = "";
        public string WarrantyNote { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public DateOnly? Start { get; set; }
        public DateOnly? End { get; set; }
        public DateTime StoredAtUtc { get; set; }
    }

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, Entry>? _items;
    private int _unsaved;

    public ResultCache(string path) => _path = path;

    public int Count { get { lock (_gate) return Load().Count; } }

    public bool TryGet(string serial, int maxAgeDays, out DeviceResult result)
    {
        result = new DeviceResult();
        Entry? e;
        lock (_gate) Load().TryGetValue(serial, out e);
        if (e == null || DateTime.UtcNow - e.StoredAtUtc > TimeSpan.FromDays(Math.Max(1, maxAgeDays))) return false;

        result.SerialNumber = e.Serial; result.Manufacturer = e.Manufacturer; result.Product = e.Product;
        result.Model = e.Model; result.Mtm = e.Mtm; result.WarrantyName = e.WarrantyName;
        result.WarrantyNote = e.WarrantyNote; result.ImageUrl = e.ImageUrl;
        result.WarrantyStart = e.Start; result.WarrantyEnd = e.End;
        result.Status = LookupStatus.Found;
        return true;
    }

    public void Store(DeviceResult r)
    {
        if (r.Status != LookupStatus.Found) return;
        var flush = false;
        lock (_gate)
        {
            Load()[r.SerialNumber] = new Entry
            {
                Serial = r.SerialNumber, Manufacturer = r.Manufacturer, Product = r.Product, Model = r.Model,
                Mtm = r.Mtm, WarrantyName = r.WarrantyName, WarrantyNote = r.WarrantyNote, ImageUrl = r.ImageUrl,
                Start = r.WarrantyStart, End = r.WarrantyEnd, StoredAtUtc = DateTime.UtcNow
            };
            flush = ++_unsaved >= 25;
        }
        if (flush) Flush();
    }

    public void Clear()
    {
        lock (_gate) { _items = new(); _unsaved = 0; }
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception ex) { AppLogger.Error("Clear cache", ex); }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_items == null || _unsaved == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_items));
                _unsaved = 0;
            }
            catch (Exception ex) { AppLogger.Error("Save cache", ex); }
        }
    }

    private Dictionary<string, Entry> Load()
    {
        if (_items != null) return _items;
        try
        {
            _items = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Read cache (starting empty)", ex);
            _items = new();
        }
        return _items;
    }
}
