using CommandLine;

using SharedLib;

namespace HeadlessServer;

public sealed class RunOptions
{
    [Value(0,
        MetaName = "ClassConfig file",
        Required = true,
        HelpText = "ClassConfiguration file found in 'Json\\class\\'\nexample: Warrior_1.json")]
    public string? ClassConfig { get; set; }

    [Option('m',
        "mode",
        Required = false,
        Default = StartupConfigPathing.Types.Local,
        HelpText = $"Navigation services: \n{nameof(StartupConfigPathing.Types.Local)}\n{nameof(StartupConfigPathing.Types.RemoteV1)}\n{nameof(StartupConfigPathing.Types.RemoteV3)}")]
    public StartupConfigPathing.Types Mode { get; set; }

    [Option('p',
        "pid",
        Required = false,
        Default = -1,
        HelpText = $"World of Warcraft Process Id")]
    public int Pid { get; set; }

    [Option('r',
        "reader",
        Required = false,
        Default = AddonDataProviderType.DXGI,
        HelpText = $"Screen reader backend." +
        $"'{nameof(AddonDataProviderType.DXGI)}': DirectX based works from Win8.")]
    public AddonDataProviderType Reader { get; set; }

    [Option("hostv1",
        Required = false,
        Default = "localhost",
        HelpText = $"Navigation Remote V1 host")]
    public string? Hostv1 { get; set; }

    [Option("portv1",
        Required = false,
        Default = 5001,
        HelpText = $"Navigation Remote V1 port")]
    public int Portv1 { get; set; }

    [Option("hostv3",
        Required = false,
        Default = "127.0.0.1",
        HelpText = $"Navigation Remote V3 host")]
    public string? Hostv3 { get; set; }

    [Option("portv3",
        Required = false,
        Default = 47111,
        HelpText = $"Navigation Remote V3 port")]
    public int Portv3 { get; set; }

    [Option('d', "diag",
        Required = false,
        Default = false,
        HelpText = $"Capture Screenshot for Diagnostics")]
    public bool Diagnostics { get; set; }

    [Option('o', "overlay",
        Required = false,
        Default = false,
        HelpText = $"Show NpcNameFinder Overlay")]
    public bool OverlayEnabled { get; set; }

    [Option('t', "otargeting",
        Required = false,
        Default = false,
        HelpText = $"Show NpcNameFinder Overlay for Targeting")]
    public bool OverlayTargeting { get; set; }

    [Option('s', "oskinning",
        Required = false,
        Default = false,
        HelpText = $"Show NpcNameFinder Overlay for Skinning")]
    public bool OverlaySkinning { get; set; }

    [Option('v', "otargetvsadd",
        Required = false,
        Default = false,
        HelpText = $"Show NpcNameFinder Overlay for Target vs Add")]
    public bool OverlayTargetVsAdd { get; set; }

    [Option('n', "viz",
        Required = false,
        Default = false,
        HelpText = $"Disable PathVisualization in RemoteV1")]
    public bool PathVisualizer { get; set; }

    [Option("loadonly",
        Required = false,
        Default = false,
        HelpText = $"Loads the class profile then exists")]
    public bool LoadOnly { get; set; }
}
