using System.Collections.ObjectModel;
using DeviceLookup.Core.Models;
using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;

namespace DeviceLookup.Core.ViewModels;

public sealed class ScanViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private string _scanText = "";
    private int _found, _notFound;

    public ScanViewModel(AppServices services)
    {
        _s = services;
        SubmitCommand = new RelayCommand(_ => Submit());
        ExportCommand = new RelayCommand(_ => Export(), _ => Scans.Count > 0);
        ClearCommand = new RelayCommand(_ => ClearList(), _ => Scans.Count > 0);
    }

    public ObservableCollection<DeviceResult> Scans { get; } = new();
    public RelayCommand SubmitCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ClearCommand { get; }

    public string ScanText { get => _scanText; set => Set(ref _scanText, value); }
    public int ScannedCount => Scans.Count;
    public int FoundCount { get => _found; private set => Set(ref _found, value); }
    public int NotFoundCount { get => _notFound; private set => Set(ref _notFound, value); }

    /// <summary>Scanners "type" the code and press Enter. Duplicates are ignored right away.</summary>
    private void Submit()
    {
        var serial = SerialNormalizer.Normalize(_scanText);
        ScanText = "";
        if (serial.Length == 0) return;

        if (!_seen.Add(serial))
        {
            _s.Toasts.Info("Already scanned", serial);
            return;
        }

        var row = new DeviceResult { SerialNumber = serial, Status = LookupStatus.Looking, LookupTime = DateTime.Now };
        Scans.Insert(0, row);
        Refresh();
        _ = ResolveAsync(row);
    }

    private async Task ResolveAsync(DeviceResult row)
    {
        try
        {
            var res = await _s.Lookup.LookupAsync(row.SerialNumber, VendorChoice.Auto, CancellationToken.None);
            var time = row.LookupTime;
            row.ApplyFrom(res);
            row.LookupTime = time;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Scan lookup crashed for {row.SerialNumber}", ex);
            row.Status = LookupStatus.Failed;
            row.Error = "Unexpected error. See the log.";
        }
        Refresh();
        _s.Lookup.FlushCache();
    }

    private void Refresh()
    {
        FoundCount = Scans.Count(r => r.Status == LookupStatus.Found);
        NotFoundCount = Scans.Count(r => r.Status is LookupStatus.NotFound or LookupStatus.Failed);
        OnPropertyChanged(nameof(ScannedCount));
        ExportCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }

    private void Export()
    {
        var path = _s.Platform.PickSaveFile($"Scans-{DateTime.Now:yyyyMMdd-HHmm}.csv", "CSV file (*.csv)|*.csv");
        if (path == null) return;
        try
        {
            CsvService.Export(Scans.Reverse(), path);
            _s.Toasts.Success("Export complete", Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("Scan export failed", ex);
            _s.Toasts.Error("Couldn't save the file", ex.Message);
        }
    }

    private void ClearList()
    {
        Scans.Clear();
        _seen.Clear();
        Refresh();
    }

    public void LoadDemo(IEnumerable<DeviceResult> rows)
    {
        Scans.Clear();
        foreach (var r in rows) { Scans.Add(r); _seen.Add(r.SerialNumber); }
        Refresh();
    }
}
