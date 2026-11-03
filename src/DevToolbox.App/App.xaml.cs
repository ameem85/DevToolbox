using DevToolbox.Core.Abstractions.Plugins;

namespace DevToolbox.App;

public partial class App : Application
{
    private readonly IDevToolRegistry _toolRegistry;

    public App(IDevToolRegistry toolRegistry)
    {
        InitializeComponent();
        _toolRegistry = toolRegistry;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        _ = _toolRegistry.InitializeAllAsync();
        return new Window(new AppShell())
        {
            Title = "DevToolbox Companion"
        };
    }
}
