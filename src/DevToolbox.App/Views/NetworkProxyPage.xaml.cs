using DevToolbox.App.ViewModels;

namespace DevToolbox.App.Views;

public partial class NetworkProxyPage : ContentPage
{
    public NetworkProxyPage(NetworkProxyViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
