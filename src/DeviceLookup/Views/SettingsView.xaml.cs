using System.Windows;
using System.Windows.Controls;
using DeviceLookup.Core.ViewModels;

namespace DeviceLookup.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        // PasswordBox can't be data-bound, so it is synced by hand.
        Loaded += (_, _) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                _loading = true;
                SecretBox.Password = vm.DellClientSecret;
                _loading = false;
            }
        };
    }

    private void SecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && DataContext is SettingsViewModel vm) vm.DellClientSecret = SecretBox.Password;
    }
}
