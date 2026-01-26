namespace SharedLib;

public sealed class StartupConfigPathing
{
    public const string Position = "Pathing";

    public enum Types
    {
        Local,
        RemoteV1,
        RemoteV3
    }

    public StartupConfigPathing() { }

    public StartupConfigPathing(string Mode,
        string hostv1, int portv1,
        string hostv3, int portv3,
        bool pathVisualizer
        )
    {
        this.Mode = Mode;

        this.hostv1 = hostv1;
        this.portv1 = portv1;

        this.hostv3 = hostv3;
        this.portv3 = portv3;

        this.PathVisualizer = pathVisualizer;
    }

    public Types Type =>
        System.Enum.TryParse(Mode, out Types m) ? m : Types.Local;

    public string Mode { get; set; } = string.Empty;

    public string hostv1 { get; set; } = string.Empty;
    public int portv1 { get; set; }

    public string hostv3 { get; set; } = string.Empty;
    public int portv3 { get; set; }

    public bool PathVisualizer { get; set; }
}
