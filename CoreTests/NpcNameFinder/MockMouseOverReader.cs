using Core;

namespace CoreTests;

internal sealed class MockMouseOverReader : IMouseOverReader
{
    public int MouseOverLevel => 0;

    public UnitClassification MouseOverClassification => UnitClassification.None;

    public int MouseOverId => 0;

    public int MouseOverGuid => 0;
}
