namespace SharedLib;

public sealed class StartupConfigReader
{
    public const string Position = "Reader";

    public StartupConfigReader() { }

    public string Type { get; set; } = string.Empty;

    public bool UseGpu { get; set; } = true;

    public AddonDataProviderType ReaderType =>
        System.Enum.TryParse(Type, out AddonDataProviderType m)
        ? m
        : AddonDataProviderType.DXGI;
}
