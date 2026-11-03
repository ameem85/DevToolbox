using DevToolbox.Core.Abstractions.Plugins;
using DevToolbox.Core.Abstractions.Services;
using DevToolbox.Core.Modules;
using DevToolbox.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevToolbox.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the DevToolbox core plugin registry, high-frequency direct-update sinks,
    /// MITM certificate authority, local network proxy engine, ADB bridge, logcat stream,
    /// device inspector, and built-in developer tool modules.
    /// </summary>
    public static IServiceCollection AddDevToolboxCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(typeof(IHighFrequencyUpdateSink<>), typeof(HighFrequencyUpdateSink<>));
        services.TryAddSingleton<IProxyCertificateService, ProxyCertificateService>();
        services.TryAddSingleton<IAdbBridgeService, AdbBridgeService>();
        services.TryAddSingleton<INetworkProxyService, LocalNetworkProxyEngine>();
        services.TryAddSingleton<ILogcatStreamService, LogcatStreamService>();
        services.TryAddSingleton<IDeviceInspectorService, DeviceInspectorService>();
        services.TryAddSingleton<IDevToolRegistry, DevToolRegistry>();

        services.AddDevToolModule<FloatingOverlayToolModule>();
        services.AddDevToolModule<NetworkProxyToolModule>();
        services.AddDevToolModule<OrientationHelperToolModule>();

        return services;
    }

    /// <summary>
    /// Plugs a new <see cref="IDevToolModule"/> implementation into the modular toolkit container.
    /// </summary>
    public static IServiceCollection AddDevToolModule<TModule>(this IServiceCollection services)
        where TModule : class, IDevToolModule
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TModule>();
        services.AddSingleton<IDevToolModule>(sp => sp.GetRequiredService<TModule>());
        return services;
    }
}
