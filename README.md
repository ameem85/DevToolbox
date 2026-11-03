# DevToolbox Companion (.NET 10 MAUI)

Cross-platform desktop and mobile developer & QA companion toolkit supporting **Android**, **iOS**, and **Windows** from a modular .NET 10 MAUI architecture.

## Implemented Developer & QA Modules

1. **Floating Widget / Overlay Engine** ([`FloatingOverlayToolModule`](src/DevToolbox.Core/Modules/FloatingOverlayToolModule.cs) / [`OverlayToolPage`](src/DevToolbox.App/Views/OverlayToolPage.xaml)):
   - **Android**: Native `IWindowManager` overlay (`TYPE_APPLICATION_OVERLAY` / `SYSTEM_ALERT_WINDOW`) with drag-to-move touch handling and quick-action bar.
   - **Windows**: Dedicated compact Always-On-Top HUD window ([`FloatingHudPage`](src/DevToolbox.App/Views/FloatingHudPage.cs)) using WinUI 3 `OverlappedPresenter` + Win32 `SetWindowPos(HWND_TOPMOST)` and `WS_EX_LAYERED` alpha transparency.
   - **iOS**: In-app high-z-order `UIWindow` (`UIWindowLevel.Alert + 1`) floating overlay layer.
2. **Network Proxy & Mocking Engine** ([`NetworkProxyToolModule`](src/DevToolbox.Core/Modules/NetworkProxyToolModule.cs) / [`NetworkProxyPage`](src/DevToolbox.App/Views/NetworkProxyPage.xaml)):
   - Local HTTP/HTTPS MITM proxy server ([`LocalNetworkProxyEngine`](src/DevToolbox.Core/Services/LocalNetworkProxyEngine.cs)) with dynamic Root CA + per-host SAN leaf certificate generation ([`ProxyCertificateService`](src/DevToolbox.Core/Services/ProxyCertificateService.cs)) for `CONNECT` TLS 1.2/1.3 termination.
   - Supports **Map Local** payloads/files, **Header & URL Rewrite** rules, **Latency Injection**, and **HTTP Fault/Error Injection** (e.g., 503/429/500), plus `adb reverse` port binding and one-click Root CA `.cer`/`.pem` export & ADB push.
   - Streams captured traffic directly via [`IHighFrequencyUpdateSink<CapturedTrafficEntry>`](src/DevToolbox.Core/Abstractions/Services/IHighFrequencyUpdateSink.cs).
3. **Orientation & State Helper** ([`OrientationHelperToolModule`](src/DevToolbox.Core/Modules/OrientationHelperToolModule.cs) / [`OrientationToolPage`](src/DevToolbox.App/Views/OrientationToolPage.xaml)):
   - Forces Portrait, Landscape, Reverse Portrait, Reverse Landscape, or Sensor Auto-Rotate both locally on-device (`Activity.RequestedOrientation` / `UIWindowScene.RequestGeometryUpdate` / Desktop viewport resize) and remotely on connected Android devices via ADB (`settings put system user_rotation`, `wm size`, `wm density`).
4. **Logcat & Unified Stream Reader** ([`LogcatReaderToolModule`](src/DevToolbox.Core/Modules/FutureToolModules.cs) / [`LogcatToolPage`](src/DevToolbox.App/Views/LogcatToolPage.xaml)):
   - Real-time Android `adb logcat` and unified runtime diagnostic streaming via [`LogcatStreamService`](src/DevToolbox.Core/Services/LogcatStreamService.cs) and [`IHighFrequencyUpdateSink<LogcatEntryRecord>`](src/DevToolbox.Core/Abstractions/Services/IHighFrequencyUpdateSink.cs) with level, tag, and full-text filtering plus QA checkpoint markers.
5. **Device & Environment Inspector** ([`DeviceInfoInspectorToolModule`](src/DevToolbox.Core/Modules/FutureToolModules.cs) / [`DeviceInspectorPage`](src/DevToolbox.App/Views/DeviceInspectorPage.xaml)):
   - Live inspection of Host/OS, CPU ABI, .NET runtime, process memory, active network interfaces, and connected Android device `getprop` / `wm` telemetry with one-click Markdown report copy.
6. **Deep-Link & Intent Dispatcher** ([`DeepLinkTesterToolModule`](src/DevToolbox.Core/Modules/FutureToolModules.cs) / [`DeepLinkTesterPage`](src/DevToolbox.App/Views/DeepLinkTesterPage.xaml)):
   - Interactive custom URI scheme (`devtoolbox://...`, `myapp://...`) and HTTPS Universal / App Link launcher supporting both local OS `Launcher` execution and Android `adb shell am start -W -a android.intent.action.VIEW -d ...` with saved presets and execution logs.

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
