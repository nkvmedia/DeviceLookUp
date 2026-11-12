using System.Globalization;
using DeviceLookup.Core.Mvvm;

namespace DeviceLookup.Core.Models;

public enum LookupStatus { Pending, Looking, Found, NotFound, Failed }
public enum WarrantyState { Unknown, Active, ExpiringSoon, Expired }
public enum VendorChoice { Auto, Lenovo, Dell }

/// <summary>Lets tests pin "today" so warranty maths is deterministic.</summary>
public static class Clock
{
    public static Func<DateOnly> Today { get; set; } = () => DateOnly.FromDateTime(DateTime.Now);
}

public sealed class DeviceResult : ObservableObject
{
    public const int ExpiringSoonDays = 90;

    private string _serial = "", _manufacturer = "", _product = "", _model = "", _mtm = "";
    private string _warrantyName = "", _warrantyNote = "", _error = "", _imageUrl = "", _imagePath = "";
    private DateOnly? _start, _end;
    private LookupStatus _status = LookupStatus.Pending;
    private DateTime? _lookupTime;
    private TimeSpan? _duration;

    /// <summary>Columns from the imported CSV, kept so they survive an export.</summary>
    public Dictionary<string, string> OriginalColumns { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string SerialNumber { get => _serial; set => Set(ref _serial, value); }
    public string Manufacturer { get => _manufacturer; set => Set(ref _manufacturer, value); }
    public string Product { get => _product; set => Set(ref _product, value); }
    public string Model { get => _model; set => Set(ref _model, value); }
    public string Mtm { get => _mtm; set => Set(ref _mtm, value); }
    public string WarrantyName { get => _warrantyName; set => Set(ref _warrantyName, value); }
    public string WarrantyNote { get => _warrantyNote; set => Set(ref _warrantyNote, value); }
    public string Error { get => _error; set => Set(ref _error, value); }
    public string ImageUrl { get => _imageUrl; set => Set(ref _imageUrl, value); }
    public string ImagePath { get => _imagePath; set { if (Set(ref _imagePath, value)) OnPropertyChanged(nameof(HasImage)); } }
    public DateOnly? WarrantyStart { get => _start; set => Set(ref _start, value); }
    public DateOnly? WarrantyEnd { get => _end; set => Set(ref _end, value); }
    public DateTime? LookupTime { get => _lookupTime; set => Set(ref _lookupTime, value); }
    public TimeSpan? Duration { get => _duration; set => Set(ref _duration, value); }

    public LookupStatus Status
    {
        get => _status;
        set { if (Set(ref _status, value)) OnPropertyChanged(null); }
    }

    // ---- derived values the UI binds to ----
    public bool HasImage => !string.IsNullOrEmpty(_imagePath);

    public int? DaysRemaining => _end is { } e ? e.DayNumber - Clock.Today().DayNumber : null;

    public WarrantyState WarrantyState => DaysRemaining switch
    {
        null => WarrantyState.Unknown,
        < 0 => WarrantyState.Expired,
        <= ExpiringSoonDays => WarrantyState.ExpiringSoon,
        _ => WarrantyState.Active
    };

    public string WarrantyLabel => WarrantyState switch
    {
        WarrantyState.Active => "Active",
        WarrantyState.ExpiringSoon => $"Expires in {DaysRemaining} days",
        WarrantyState.Expired => "Expired",
        _ => string.IsNullOrWhiteSpace(_warrantyNote) ? "N/A" : _warrantyNote
    };

    public string WarrantyStartText => FormatDate(_start);
    public string WarrantyEndText => FormatDate(_end);

    public string RemainingText => _end is { } e ? FormatRemaining(Clock.Today(), e) : "-";

    public string StatusText => _status switch
    {
        LookupStatus.Found => "Found",
        LookupStatus.NotFound => "Not Found",
        LookupStatus.Failed => "Failed",
        LookupStatus.Looking => "Looking up…",
        _ => "Pending"
    };

    public string StatusTitle => _status switch
    {
        LookupStatus.Found => "Device found",
        LookupStatus.NotFound => "Device not found",
        LookupStatus.Failed => "Lookup failed",
        LookupStatus.Looking => "Looking up…",
        _ => "Pending"
    };

    public string TimeText => _lookupTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    public string DurationText => _duration is { } d
        ? $"{d.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} seconds" : "";

    public string DurationLabel => _duration == null ? "" : $"Lookup time: {DurationText}";

    public static string FormatDate(DateOnly? d) =>
        d?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "-";

    public static string FormatRemaining(DateOnly today, DateOnly end)
    {
        if (end < today) return $"Expired {today.DayNumber - end.DayNumber} days ago";
        var days = end.DayNumber - today.DayNumber;
        int years = 0, months = 0;
        var cursor = today;
        while (cursor.AddYears(1) <= end) { cursor = cursor.AddYears(1); years++; }
        while (cursor.AddMonths(1) <= end) { cursor = cursor.AddMonths(1); months++; }
        var parts = new List<string>();
        if (years > 0) parts.Add(years == 1 ? "1 year" : $"{years} years");
        if (months > 0) parts.Add(months == 1 ? "1 month" : $"{months} months");
        return parts.Count == 0 ? $"{days} days" : $"{string.Join(", ", parts)} ({days} days)";
    }

    /// <summary>Copies lookup output (not the imported CSV columns) from another result.</summary>
    public void ApplyFrom(DeviceResult o)
    {
        _serial = o._serial; _manufacturer = o._manufacturer; _product = o._product;
        _model = o._model; _mtm = o._mtm; _warrantyName = o._warrantyName;
        _warrantyNote = o._warrantyNote; _error = o._error; _imageUrl = o._imageUrl;
        _start = o._start; _end = o._end; _lookupTime = o._lookupTime; _duration = o._duration;
        if (!string.IsNullOrEmpty(o._imagePath)) _imagePath = o._imagePath;
        _status = o._status;
        OnPropertyChanged(null);
    }

    public string ToClipboardText()
    {
        var lines = new List<string>
        {
            $"Manufacturer: {Manufacturer}", $"Product: {Product}", $"Serial: {SerialNumber}"
        };
        if (!string.IsNullOrEmpty(Model)) lines.Add($"Model/Type: {Model}");
        if (!string.IsNullOrEmpty(Mtm)) lines.Add($"MTM: {Mtm}");
        lines.Add($"Warranty: {WarrantyLabel}");
        if (!string.IsNullOrEmpty(WarrantyName)) lines.Add($"Warranty plan: {WarrantyName}");
        if (_start != null) lines.Add($"Start: {WarrantyStartText}");
        if (_end != null) lines.Add($"End: {WarrantyEndText} ({RemainingText})");
        if (!string.IsNullOrEmpty(Error)) lines.Add($"Note: {Error}");
        return string.Join(Environment.NewLine, lines);
    }
}
