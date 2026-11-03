using DevToolbox.App.ViewModels;

namespace DevToolbox.App.Views;

public partial class DeepLinkTesterPage : ContentPage
{
    public DeepLinkTesterPage(DeepLinkTesterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
