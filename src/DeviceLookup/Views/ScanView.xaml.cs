using System.Windows;
using System.Windows.Controls;

namespace DeviceLookup.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
        // Keep the cursor in the scan box so a barcode scanner can fire at any moment.
        Loaded += (_, _) => ScanBox.Focus();
        IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) ScanBox.Focus(); };
    }
}
