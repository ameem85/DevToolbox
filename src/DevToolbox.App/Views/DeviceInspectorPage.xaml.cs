using DevToolbox.App.ViewModels;

namespace DevToolbox.App.Views;

public partial class DeviceInspectorPage : ContentPage
{
    public DeviceInspectorPage(DeviceInspectorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
