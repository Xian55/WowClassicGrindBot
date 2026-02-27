namespace Core;

public sealed class NullAddonDataProvider : IAddonDataProvider
{
    public int[] Data { get; }

    public NullAddonDataProvider(int dataSize)
    {
        Data = new int[dataSize];
    }

    public void UpdateData() { }

    public void InitFrames(DataFrame[] frames) { }

    public void Dispose() { }
}
