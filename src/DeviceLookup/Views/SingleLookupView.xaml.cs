using System.Windows.Controls;

namespace DeviceLookup.Views;

public partial class SingleLookupView : UserControl
{
    public SingleLookupView()
    {
        InitializeComponent();
        Loaded += (_, _) => SerialBox.Focus();
    }
}
