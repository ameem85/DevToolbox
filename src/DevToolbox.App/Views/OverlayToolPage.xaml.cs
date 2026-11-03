using DevToolbox.App.ViewModels;

namespace DevToolbox.App.Views;

public partial class OverlayToolPage : ContentPage
{
    public OverlayToolPage(OverlayToolViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
