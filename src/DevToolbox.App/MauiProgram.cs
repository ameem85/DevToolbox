using CommunityToolkit.Maui;
using DevToolbox.App.ViewModels;
using DevToolbox.App.Views;
using DevToolbox.Core.Abstractions.Services;
using DevToolbox.Core.DependencyInjection;
using DevToolbox.Core.Modules;
using Microsoft.Extensions.Logging;

namespace DevToolbox.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        // Register Core modular architecture, MITM certificate authority, proxy engine, ADB bridge, and core modules
        builder.Services.AddDevToolboxCore();

        // Register additional developer & QA tool modules into the plug-and-play registry
        builder.Services.AddDevToolModule<LogcatReaderToolModule>();
        builder.Services.AddDevToolModule<DeviceInfoInspectorToolModule>();
        builder.Services.AddDevToolModule<DeepLinkTesterToolModule>();

        // Register platform-specific Overlay & Orientation engines
#if ANDROID
        builder.Services.AddSingleton<IOverlayService, Platforms.Android.Services.AndroidOverlayService>();
        builder.Services.AddSingleton<IOrientationStateService, Platforms.Android.Services.AndroidOrientationStateService>();
#elif WINDOWS
        builder.Services.AddSingleton<IOverlayService, Platforms.Windows.Services.WindowsOverlayService>();
        builder.Services.AddSingleton<IOrientationStateService, Platforms.Windows.Services.WindowsOrientationStateService>();
#elif IOS
        builder.Services.AddSingleton<IOverlayService, Platforms.iOS.Services.IosOverlayService>();
        builder.Services.AddSingleton<IOrientationStateService, Platforms.iOS.Services.IosOrientationStateService>();
#endif

        // Register MVVM ViewModels and Views
        builder.Services.AddSingleton<DashboardViewModel>();
        builder.Services.AddSingleton<OverlayToolViewModel>();
        builder.Services.AddSingleton<NetworkProxyViewModel>();
        builder.Services.AddSingleton<OrientationToolViewModel>();
        builder.Services.AddSingleton<LogcatToolViewModel>();
        builder.Services.AddSingleton<DeviceInspectorViewModel>();
        builder.Services.AddSingleton<DeepLinkTesterViewModel>();

        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<OverlayToolPage>();
        builder.Services.AddTransient<NetworkProxyPage>();
        builder.Services.AddTransient<OrientationToolPage>();
        builder.Services.AddTransient<LogcatToolPage>();
        builder.Services.AddTransient<DeviceInspectorPage>();
        builder.Services.AddTransient<DeepLinkTesterPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
