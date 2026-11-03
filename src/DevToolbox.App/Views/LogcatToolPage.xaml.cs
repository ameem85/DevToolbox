using DevToolbox.App.ViewModels;

namespace DevToolbox.App.Views;

public partial class LogcatToolPage : ContentPage
{
    public LogcatToolPage(LogcatToolViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
