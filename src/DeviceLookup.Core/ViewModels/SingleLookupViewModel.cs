using DeviceLookup.Core.Models;
using DeviceLookup.Core.Mvvm;
using DeviceLookup.Core.Services;

namespace DeviceLookup.Core.ViewModels;

public sealed class SingleLookupViewModel : ObservableObject
{
    private readonly AppServices _s;
    private string _serial = "";
    private int _vendorIndex;
    private bool _busy;
    private DeviceResult? _result;

    public SingleLookupViewModel(AppServices services)
    {
        _s = services;
        LookupCommand = new AsyncRelayCommand(_ => LookupAsync(), _ => !_busy && _serial.Trim().Length > 0);
        CopyResultCommand = new RelayCommand(_ => CopyResult(), _ => _result != null);
        ClearCommand = new RelayCommand(_ => Clear());
        CopyFieldCommand = new RelayCommand(p =>
        {
            if (p is string text && text.Length > 0 && text != "-")
            {
                _s.Platform.SetClipboard(text);
                _s.Toasts.Info("Copied", text);
            }
        });
    }

    public string[] Vendors { get; } = { "Auto (Lenovo / Dell)", "Lenovo", "Dell" };
    public AsyncRelayCommand LookupCommand { get; }
    public RelayCommand CopyResultCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand CopyFieldCommand { get; }

    public int VendorIndex { get => _vendorIndex; set => Set(ref _vendorIndex, value); }

    public string Serial
    {
        get => _serial;
        set { if (Set(ref _serial, value)) LookupCommand.RaiseCanExecuteChanged(); }
    }

    public bool IsBusy
    {
        get => _busy;
        private set { if (Set(ref _busy, value)) OnPropertyChanged(nameof(ShowPlaceholder)); }
    }

    public DeviceResult? Result
    {
        get => _result;
        private set
        {
            if (!Set(ref _result, value)) return;
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ShowPlaceholder));
            CopyResultCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasResult => _result != null;
    public bool ShowPlaceholder => _result == null && !_busy;

    private async Task LookupAsync()
    {
        var serial = SerialNormalizer.Normalize(_serial);
        if (serial.Length == 0) return;

        IsBusy = true;
        Result = null;
        var toast = _s.Toasts.Progress($"Looking up {serial}…", "Please wait while we retrieve device information.");
        try
        {
            var r = await _s.Lookup.LookupAsync(serial, (VendorChoice)Math.Max(0, _vendorIndex), CancellationToken.None);
            Result = r;
            _s.Lookup.FlushCache();

            if (r.Status == LookupStatus.Found)
            {
                _s.Toasts.Finish(toast, ToastKind.Success, "Lookup completed", $"{r.Manufacturer} {r.Product}");
                _ = LoadImageAsync(r);
            }
            else
            {
                _s.Toasts.Finish(toast, ToastKind.Error,
                    r.Status == LookupStatus.NotFound ? "Device not found" : "Lookup failed", r.Error, 8);
            }
        }
        finally { IsBusy = false; }
    }

    private async Task LoadImageAsync(DeviceResult r)
    {
        if (_s.Images == null || string.IsNullOrEmpty(r.ImageUrl)) return;
        var path = await _s.Images.GetAsync(r.ImageUrl);
        if (path != null) Ui.Run(() => r.ImagePath = path);
    }

    private void CopyResult()
    {
        if (_result == null) return;
        _s.Platform.SetClipboard(_result.ToClipboardText());
        _s.Toasts.Info("Result copied");
    }

    private void Clear()
    {
        Result = null;
        Serial = "";
    }

    /// <summary>Used by screenshot mode.</summary>
    public void ShowDemo(DeviceResult r, string serial)
    {
        Serial = serial;
        Result = r;
    }
}
