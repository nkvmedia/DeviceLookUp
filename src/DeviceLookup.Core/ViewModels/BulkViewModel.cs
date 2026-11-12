using System.Collections.ObjectModel;
using System.Diagnostics;
using DeviceLookup.Core.Models;
using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;

namespace DeviceLookup.Core.ViewModels;

public sealed class BulkViewModel : ObservableObject
{
    private readonly AppServices _s;
    private CancellationTokenSource? _cts;
    private int _vendorIndex, _filterIndex, _found, _failed, _notFound, _done, _total;
    private string _search = "", _fileName = "No file loaded", _importInfo = "", _elapsed = "0s";
    private bool _failuresOnly, _running;
    private double _progress;

    public BulkViewModel(AppServices services)
    {
        _s = services;
        ImportCommand = new RelayCommand(_ => Import(), _ => !_running);
        StartCommand = new AsyncRelayCommand(_ => RunAsync(r => r.Status == LookupStatus.Pending), _ => !_running && Rows.Any(r => r.Status == LookupStatus.Pending));
        RetryCommand = new AsyncRelayCommand(_ => RunAsync(r => r.Status == LookupStatus.Failed), _ => !_running && Rows.Any(r => r.Status == LookupStatus.Failed));
        StopCommand = new RelayCommand(_ => _cts?.Cancel(), _ => _running);
        ExportCommand = new RelayCommand(_ => Export(), _ => Rows.Count > 0);
        ClearCommand = new RelayCommand(_ => ClearAll(), _ => !_running && Rows.Count > 0);
    }

    public ObservableCollection<DeviceResult> Rows { get; } = new();
    public ObservableCollection<DeviceResult> VisibleRows { get; } = new();
    public string[] Vendors { get; } = { "Auto (Lenovo / Dell)", "Lenovo", "Dell" };
    public string[] StatusFilters { get; } = { "All", "Found", "Failed", "Not Found", "Pending" };

    public RelayCommand ImportCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ClearCommand { get; }

    public int VendorIndex { get => _vendorIndex; set => Set(ref _vendorIndex, value); }
    public string FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public string ImportInfo { get => _importInfo; private set => Set(ref _importInfo, value); }
    public string ElapsedText { get => _elapsed; private set => Set(ref _elapsed, value); }
    public int FoundCount { get => _found; private set => Set(ref _found, value); }
    public int FailedCount { get => _failed; private set => Set(ref _failed, value); }
    public int NotFoundCount { get => _notFound; private set => Set(ref _notFound, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressPercentText => $"{Math.Round(_progress)}%";
    public string ProgressText => _total == 0 ? "" : _running ? $"Processing {_done} / {_total}…" : $"Processed {_done} / {_total}";
    public bool HasRows => Rows.Count > 0;

    public bool IsRunning
    {
        get => _running;
        private set
        {
            if (!Set(ref _running, value)) return;
            OnPropertyChanged(nameof(ProgressText));
            RaiseCommands();
        }
    }

    public string SearchText { get => _search; set { if (Set(ref _search, value)) ApplyFilter(); } }
    public int FilterIndex { get => _filterIndex; set { if (Set(ref _filterIndex, value)) ApplyFilter(); } }
    public bool FailuresOnly { get => _failuresOnly; set { if (Set(ref _failuresOnly, value)) ApplyFilter(); } }

    // ---- actions ----
    private void Import()
    {
        var path = _s.Platform.PickOpenFile("CSV files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*");
        if (path == null) return;
        try
        {
            var import = CsvService.Import(path);
            Rows.Clear();
            foreach (var r in import.Rows) Rows.Add(r);
            FileName = Path.GetFileName(path);
            ImportInfo = $"({import.Rows.Count} rows)";
            _total = import.Rows.Count;
            _done = 0;
            ResetStats();
            ApplyFilter();
            RaiseCommands();
            OnPropertyChanged(nameof(HasRows));
            _s.Toasts.Info("CSV imported", $"{import.Rows.Count} serial numbers ready. Press Start lookup.");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("CSV import failed", ex);
            _s.Toasts.Error("Couldn't import that file", ex.Message);
        }
    }

    private async Task RunAsync(Func<DeviceResult, bool> pick)
    {
        var work = Rows.Where(pick).ToList();
        if (work.Count == 0) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var vendor = (VendorChoice)Math.Max(0, _vendorIndex);
        var degree = Math.Clamp(_s.Settings.ConcurrentLookups, 1, 10);
        var sw = Stopwatch.StartNew();

        foreach (var r in work) { r.Status = LookupStatus.Pending; r.Error = ""; }
        _total = Rows.Count;
        _done = Rows.Count - work.Count;
        IsRunning = true;
        UpdateProgress(sw);
        var toast = _s.Toasts.Progress("Bulk lookup running…", $"{work.Count} devices");

        try
        {
            await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = degree, CancellationToken = ct },
                async (row, token) =>
                {
                    Ui.Run(() => row.Status = LookupStatus.Looking);
                    var res = await _s.Lookup.LookupAsync(row.SerialNumber, vendor, token).ConfigureAwait(false);
                    Ui.Run(() =>
                    {
                        row.ApplyFrom(res);
                        Interlocked.Increment(ref _done);
                        UpdateProgress(sw);
                    });
                });
            _s.Toasts.Finish(toast, ToastKind.Info, "Bulk lookup completed",
                $"{FoundCount} found · {FailedCount} failed · {NotFoundCount} not found");
        }
        catch (OperationCanceledException)
        {
            _s.Toasts.Finish(toast, ToastKind.Info, "Bulk lookup stopped", $"{_done} of {_total} processed");
        }
        finally
        {
            foreach (var r in Rows.Where(r => r.Status == LookupStatus.Looking).ToList()) r.Status = LookupStatus.Pending;
            sw.Stop();
            _s.Lookup.FlushCache();
            _cts?.Dispose();
            _cts = null;
            UpdateProgress(sw);
            IsRunning = false;
            ApplyFilter();
        }
    }

    private void UpdateProgress(Stopwatch sw)
    {
        FoundCount = Rows.Count(r => r.Status == LookupStatus.Found);
        FailedCount = Rows.Count(r => r.Status == LookupStatus.Failed);
        NotFoundCount = Rows.Count(r => r.Status == LookupStatus.NotFound);
        Progress = _total == 0 ? 0 : Math.Min(100, _done * 100.0 / _total);
        ElapsedText = FormatElapsed(sw.Elapsed);
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercentText));
    }

    public static string FormatElapsed(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds:00}s" : $"{t.Seconds}s";

    private void ResetStats()
    {
        FoundCount = FailedCount = NotFoundCount = 0;
        Progress = 0;
        ElapsedText = "0s";
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercentText));
    }

    public void ApplyFilter()
    {
        var q = _search.Trim();
        var wanted = _filterIndex switch
        {
            1 => LookupStatus.Found, 2 => LookupStatus.Failed, 3 => LookupStatus.NotFound, 4 => LookupStatus.Pending, _ => (LookupStatus?)null
        };
        var shown = Rows.Where(r =>
            (!_failuresOnly || r.Status is LookupStatus.Failed or LookupStatus.NotFound)
            && (wanted == null || r.Status == wanted)
            && (q.Length == 0 || Matches(r, q))).ToList();

        VisibleRows.Clear();
        foreach (var r in shown) VisibleRows.Add(r);
    }

    private static bool Matches(DeviceResult r, string q) =>
        new[] { r.SerialNumber, r.Manufacturer, r.Product, r.Model, r.Mtm, r.WarrantyLabel, r.StatusText }
            .Any(v => v.Contains(q, StringComparison.OrdinalIgnoreCase));

    private void Export()
    {
        var path = _s.Platform.PickSaveFile($"DeviceLookup-{DateTime.Now:yyyyMMdd-HHmm}.csv", "CSV file (*.csv)|*.csv");
        if (path == null) return;
        try
        {
            CsvService.Export(Rows, path);
            _s.Toasts.Success("Export complete", Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Error("CSV export failed", ex);
            _s.Toasts.Error("Couldn't save the file", ex.Message);
        }
    }

    private void ClearAll()
    {
        Rows.Clear();
        VisibleRows.Clear();
        _total = _done = 0;
        FileName = "No file loaded";
        ImportInfo = "";
        ResetStats();
        RaiseCommands();
        OnPropertyChanged(nameof(HasRows));
    }

    private void RaiseCommands()
    {
        ImportCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Used by screenshot mode and tests.</summary>
    public void LoadRows(IEnumerable<DeviceResult> rows, string fileName)
    {
        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);
        FileName = fileName;
        ImportInfo = $"({Rows.Count} rows)";
        _total = _done = Rows.Count;
        FoundCount = Rows.Count(r => r.Status == LookupStatus.Found);
        FailedCount = Rows.Count(r => r.Status == LookupStatus.Failed);
        NotFoundCount = Rows.Count(r => r.Status == LookupStatus.NotFound);
        Progress = 100;
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressPercentText));
        OnPropertyChanged(nameof(HasRows));
        ApplyFilter();
        RaiseCommands();
    }

    public void SetElapsed(string text) => ElapsedText = text;
}
