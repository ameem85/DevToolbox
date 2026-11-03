#nullable enable
using System.Globalization;
using System.Resources;

namespace DevToolbox.App.Resources.Strings;

public static class AppStrings
{
    private static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager("DevToolbox.App.Resources.Strings.AppStrings", typeof(AppStrings).Assembly);

    public static CultureInfo? Culture { get; set; }

    public static string GetString(string key) =>
        ResourceManager.GetString(key, Culture) ?? key;

    public static string App_Title => GetString(nameof(App_Title));
    public static string App_Subtitle => GetString(nameof(App_Subtitle));
    public static string Nav_Dashboard => GetString(nameof(Nav_Dashboard));
    public static string Nav_FloatingOverlay => GetString(nameof(Nav_FloatingOverlay));
    public static string Nav_NetworkProxy => GetString(nameof(Nav_NetworkProxy));
    public static string Nav_OrientationHelper => GetString(nameof(Nav_OrientationHelper));

    public static string Dashboard_HeaderTitle => GetString(nameof(Dashboard_HeaderTitle));
    public static string Dashboard_HeaderSubtitle => GetString(nameof(Dashboard_HeaderSubtitle));
    public static string Dashboard_QuickLaunchOverlay => GetString(nameof(Dashboard_QuickLaunchOverlay));
    public static string Dashboard_QuickToggleProxy => GetString(nameof(Dashboard_QuickToggleProxy));
    public static string Dashboard_QuickCycleOrientation => GetString(nameof(Dashboard_QuickCycleOrientation));
    public static string Dashboard_RegisteredModulesHeader => GetString(nameof(Dashboard_RegisteredModulesHeader));
    public static string Dashboard_OpenModuleButton => GetString(nameof(Dashboard_OpenModuleButton));
    public static string Dashboard_ToggleModuleButton => GetString(nameof(Dashboard_ToggleModuleButton));

    public static string Module_FloatingOverlay_Title => GetString(nameof(Module_FloatingOverlay_Title));
    public static string Module_FloatingOverlay_Description => GetString(nameof(Module_FloatingOverlay_Description));
    public static string Module_NetworkProxy_Title => GetString(nameof(Module_NetworkProxy_Title));
    public static string Module_NetworkProxy_Description => GetString(nameof(Module_NetworkProxy_Description));
    public static string Module_OrientationHelper_Title => GetString(nameof(Module_OrientationHelper_Title));
    public static string Module_OrientationHelper_Description => GetString(nameof(Module_OrientationHelper_Description));
    public static string Module_LogcatReader_Title => GetString(nameof(Module_LogcatReader_Title));
    public static string Module_LogcatReader_Description => GetString(nameof(Module_LogcatReader_Description));
    public static string Module_DeviceInspector_Title => GetString(nameof(Module_DeviceInspector_Title));
    public static string Module_DeviceInspector_Description => GetString(nameof(Module_DeviceInspector_Description));
    public static string Module_DeepLinkTester_Title => GetString(nameof(Module_DeepLinkTester_Title));
    public static string Module_DeepLinkTester_Description => GetString(nameof(Module_DeepLinkTester_Description));

    public static string Overlay_PageTitle => GetString(nameof(Overlay_PageTitle));
    public static string Overlay_SectionPermissions => GetString(nameof(Overlay_SectionPermissions));
    public static string Overlay_CheckPermissionButton => GetString(nameof(Overlay_CheckPermissionButton));
    public static string Overlay_RequestPermissionButton => GetString(nameof(Overlay_RequestPermissionButton));
    public static string Overlay_GrantViaAdbButton => GetString(nameof(Overlay_GrantViaAdbButton));
    public static string Overlay_SectionControls => GetString(nameof(Overlay_SectionControls));
    public static string Overlay_ShowButton => GetString(nameof(Overlay_ShowButton));
    public static string Overlay_HideButton => GetString(nameof(Overlay_HideButton));
    public static string Overlay_PinTopMostLabel => GetString(nameof(Overlay_PinTopMostLabel));
    public static string Overlay_OpacityLabel => GetString(nameof(Overlay_OpacityLabel));
    public static string Overlay_QuickActionsHeader => GetString(nameof(Overlay_QuickActionsHeader));
    public static string Overlay_RunQuickActionButton => GetString(nameof(Overlay_RunQuickActionButton));

    public static string Proxy_PageTitle => GetString(nameof(Proxy_PageTitle));
    public static string Proxy_ListenerSectionTitle => GetString(nameof(Proxy_ListenerSectionTitle));
    public static string Proxy_PortLabel => GetString(nameof(Proxy_PortLabel));
    public static string Proxy_StartButton => GetString(nameof(Proxy_StartButton));
    public static string Proxy_StopButton => GetString(nameof(Proxy_StopButton));
    public static string Proxy_AdbReverseButton => GetString(nameof(Proxy_AdbReverseButton));
    public static string Proxy_TestSimulatorSectionTitle => GetString(nameof(Proxy_TestSimulatorSectionTitle));
    public static string Proxy_SimulateProfileButton => GetString(nameof(Proxy_SimulateProfileButton));
    public static string Proxy_SimulateCheckoutButton => GetString(nameof(Proxy_SimulateCheckoutButton));
    public static string Proxy_SimulateCatalogButton => GetString(nameof(Proxy_SimulateCatalogButton));
    public static string Proxy_RulesSectionTitle => GetString(nameof(Proxy_RulesSectionTitle));
    public static string Proxy_AddRuleSectionTitle => GetString(nameof(Proxy_AddRuleSectionTitle));
    public static string Proxy_NewRuleNamePlaceholder => GetString(nameof(Proxy_NewRuleNamePlaceholder));
    public static string Proxy_NewRuleUrlPlaceholder => GetString(nameof(Proxy_NewRuleUrlPlaceholder));
    public static string Proxy_NewRulePayloadPlaceholder => GetString(nameof(Proxy_NewRulePayloadPlaceholder));
    public static string Proxy_AddRuleButton => GetString(nameof(Proxy_AddRuleButton));
    public static string Proxy_DeleteRuleButton => GetString(nameof(Proxy_DeleteRuleButton));
    public static string Proxy_TrafficSectionTitle => GetString(nameof(Proxy_TrafficSectionTitle));
    public static string Proxy_ClearTrafficButton => GetString(nameof(Proxy_ClearTrafficButton));

    public static string Orientation_PageTitle => GetString(nameof(Orientation_PageTitle));
    public static string Orientation_TargetModeSection => GetString(nameof(Orientation_TargetModeSection));
    public static string Orientation_UseRemoteAdbLabel => GetString(nameof(Orientation_UseRemoteAdbLabel));
    public static string Orientation_RefreshDevicesButton => GetString(nameof(Orientation_RefreshDevicesButton));
    public static string Orientation_QuickControlsSection => GetString(nameof(Orientation_QuickControlsSection));
    public static string Orientation_PortraitButton => GetString(nameof(Orientation_PortraitButton));
    public static string Orientation_LandscapeButton => GetString(nameof(Orientation_LandscapeButton));
    public static string Orientation_ReversePortraitButton => GetString(nameof(Orientation_ReversePortraitButton));
    public static string Orientation_ReverseLandscapeButton => GetString(nameof(Orientation_ReverseLandscapeButton));
    public static string Orientation_SensorButton => GetString(nameof(Orientation_SensorButton));
    public static string Orientation_UnlockButton => GetString(nameof(Orientation_UnlockButton));
    public static string Orientation_DisplayOverrideSection => GetString(nameof(Orientation_DisplayOverrideSection));
    public static string Orientation_PresetPhoneButton => GetString(nameof(Orientation_PresetPhoneButton));
    public static string Orientation_PresetRuggedButton => GetString(nameof(Orientation_PresetRuggedButton));
    public static string Orientation_PresetTabletButton => GetString(nameof(Orientation_PresetTabletButton));
    public static string Orientation_ResetDisplayButton => GetString(nameof(Orientation_ResetDisplayButton));

    public static string QuickAction_HideOverlay => GetString(nameof(QuickAction_HideOverlay));
    public static string QuickAction_ToggleProxy => GetString(nameof(QuickAction_ToggleProxy));
    public static string QuickAction_ForcePortrait => GetString(nameof(QuickAction_ForcePortrait));
    public static string QuickAction_ForceLandscape => GetString(nameof(QuickAction_ForceLandscape));
    public static string QuickAction_CycleOrientation => GetString(nameof(QuickAction_CycleOrientation));
    public static string Hud_FloatingTitle => GetString(nameof(Hud_FloatingTitle));
}
