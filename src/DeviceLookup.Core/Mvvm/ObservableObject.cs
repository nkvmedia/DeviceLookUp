using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeviceLookup.Core.Mvvm;

/// <summary>Base class for anything the UI binds to.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Pass null to refresh every binding on the object.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Marshals work onto the UI thread. The WPF app sets <see cref="Context"/> at startup;
/// in tests it stays null and actions simply run inline.
/// </summary>
public static class Ui
{
    public static SynchronizationContext? Context { get; set; }

    public static void Run(Action action)
    {
        var ctx = Context;
        if (ctx == null || SynchronizationContext.Current == ctx) action();
        else ctx.Post(_ => action(), null);
    }
}
