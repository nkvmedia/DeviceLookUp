using System.Collections.ObjectModel;
using DeviceLookup.Core.Mvvm;

namespace DeviceLookup.Core.ViewModels;

public enum ToastKind { Info, Success, Error, Progress }

public sealed class ToastItem : ObservableObject
{
    private string _title = "", _message = "";
    private ToastKind _kind;

    public ToastKind Kind { get => _kind; set => Set(ref _kind, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
}

public sealed class ToastHost
{
    public ObservableCollection<ToastItem> Items { get; } = new();
    public RelayCommand DismissCommand { get; }

    public ToastHost() => DismissCommand = new RelayCommand(p => { if (p is ToastItem t) Dismiss(t); });

    public ToastItem Show(ToastKind kind, string title, string message, int autoCloseSeconds = 0)
    {
        var t = new ToastItem { Kind = kind, Title = title, Message = message };
        Ui.Run(() =>
        {
            Items.Add(t);
            while (Items.Count > 4) Items.RemoveAt(0);
        });
        if (autoCloseSeconds > 0) _ = CloseLater(t, autoCloseSeconds);
        return t;
    }

    public ToastItem Info(string title, string message = "") => Show(ToastKind.Info, title, message, 4);
    public ToastItem Success(string title, string message = "") => Show(ToastKind.Success, title, message, 5);
    public ToastItem Error(string title, string message = "") => Show(ToastKind.Error, title, message, 8);
    public ToastItem Progress(string title, string message = "") => Show(ToastKind.Progress, title, message);

    /// <summary>Turns a progress toast into its final state and lets it fade away.</summary>
    public void Finish(ToastItem t, ToastKind kind, string title, string message, int autoCloseSeconds = 5)
    {
        Ui.Run(() => { t.Kind = kind; t.Title = title; t.Message = message; });
        _ = CloseLater(t, autoCloseSeconds);
    }

    public void Dismiss(ToastItem t) => Ui.Run(() => Items.Remove(t));

    private async Task CloseLater(ToastItem t, int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        Dismiss(t);
    }
}
