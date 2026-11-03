# DevToolbox Companion (.NET 10 MAUI)

Cross-platform desktop and mobile developer & QA companion toolkit supporting **Android**, **iOS**, and **Windows** from a modular .NET 10 MAUI architecture.

## Solution Structure

```text
DevToolbox/
├── DevToolbox.slnx
├── Settings.XamlStyler.json
├── src/
│   ├── DevToolbox.Core/                          # Pure .NET 10 platform-agnostic contracts, proxy engine & plugin container
│   │   ├── Abstractions/
│   │   │   ├── Plugins/
│   │   │   │   ├── IDevToolModule.cs             # Plug-and-play module contract + OverlayQuickAction
│   │   │   │   └── IDevToolRegistry.cs           # Module discovery, ordering & lifecycle coordinator
│   │   │   └── Services/
│   │   │       ├── IHighFrequencyUpdateSink.cs   # Direct IoC update sink for high-frequency streams
│   │   │       ├── IOverlayService.cs            # System / In-App floating overlay contract
│   │   │       ├── INetworkProxyService.cs       # Local HTTP/HTTPS proxy, mock, rewrite & fault-injection contract
│   │   │       ├── IOrientationStateService.cs   # Screen orientation & display override contract
│   │   │       └── IAdbBridgeService.cs          # Desktop-to-Android ADB companion bridge contract
│   │   ├── DependencyInjection/
│   │   │   └── ServiceCollectionExtensions.cs    # AddDevToolboxCore() and AddDevToolModule<TModule>()
│   │   ├── Modules/
│   │   │   ├── FloatingOverlayToolModule.cs      # Tool 1: Floating Widget / Overlay Engine
│   │   │   ├── NetworkProxyToolModule.cs         # Tool 2: Network Proxy & Mocking Tool
│   │   │   ├── OrientationHelperToolModule.cs    # Tool 3: Orientation & State Helper
│   │   │   └── FutureToolModules.cs              # Plug-and-play templates: Logcat, Device Info, Deep-Link Tester
│   │   └── Services/
│   │       ├── DevToolRegistry.cs
│   │       ├── HighFrequencyUpdateSink.cs
│   │       ├── LocalNetworkProxyEngine.cs        # TCP/HTTP proxy with Map Local, Rewrite, Latency & Error Injection
│   │       └── AdbBridgeService.cs               # ADB appops, rotation, wm size/density, and reverse tunnel runner
│   └── DevToolbox.App/                           # .NET 10 MAUI Cross-Platform Dashboard & Native Platform Engines
│       ├── Platforms/
│       │   ├── Android/Services/
│       │   │   ├── AndroidOverlayService.cs      # IWindowManager + TYPE_APPLICATION_OVERLAY + SYSTEM_ALERT_WINDOW
│       │   │   └── AndroidOrientationStateService.cs
│       │   ├── Windows/Services/
│       │   │   ├── WindowsOverlayService.cs      # WinUI 3 AppWindow OverlappedPresenter + Win32 SetWindowPos(HWND_TOPMOST)
│       │   │   └── WindowsOrientationStateService.cs
│       │   └── iOS/Services/
│       │       ├── IosOverlayService.cs          # UIWindow (UIWindowLevel.Alert + 1) in-app floating HUD
│       │       └── IosOrientationStateService.cs # UIWindowScene.RequestGeometryUpdate (iOS 16+)
│       ├── Resources/Strings/
│       │   ├── AppStrings.resx                   # Localized UI strings
│       │   └── AppStrings.Designer.cs
│       ├── ViewModels/
│       │   ├── DashboardViewModel.cs
│       │   ├── OverlayToolViewModel.cs
│       │   ├── NetworkProxyViewModel.cs
│       │   └── OrientationToolViewModel.cs
│       └── Views/
│           ├── DashboardPage.xaml
│           ├── OverlayToolPage.xaml
│           ├── NetworkProxyPage.xaml
│           ├── OrientationToolPage.xaml
│           └── FloatingHudPage.cs
└── tests/
    └── DevToolbox.Core.Tests/
        └── DevToolboxArchitectureTests.cs        # xUnit verification of plugin registry, proxy rules & IoC sink
```

## Building & Running

```powershell
# Run unit tests
dotnet test tests/DevToolbox.Core.Tests/DevToolbox.Core.Tests.csproj

# Build Windows Desktop application
dotnet build src/DevToolbox.App/DevToolbox.App.csproj -f net10.0-windows10.0.19041.0

# Build Android application
dotnet build src/DevToolbox.App/DevToolbox.App.csproj -f net10.0-android
```

## Pre-Deployment Android Permission Provisioning

Before or during Android deployment, grant the system overlay permission via ADB `appops`:

```powershell
adb shell appops set com.devtoolbox.companion SYSTEM_ALERT_WINDOW allow
```
