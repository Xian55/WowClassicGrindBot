using SharedLib.Data;

namespace SharedLib;

public readonly record struct Item
{
    public int Entry { get; init; }
    public string Name { get; init; }
    public int Quality { get; init; }
    public int SellPrice { get; init; }
    public int TextureId { get; init; }
    public ItemClass ClassId { get; init; }
    public int SubclassId { get; init; }
}