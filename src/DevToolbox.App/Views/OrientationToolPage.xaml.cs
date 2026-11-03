using DevToolbox.App.ViewModels;

namespace DevToolbox.App.Views;

public partial class OrientationToolPage : ContentPage
{
    public OrientationToolPage(OrientationToolViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
