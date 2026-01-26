using nietras.SeparatedValues;

namespace ReadDBC_CSV;

internal static class SepReaderHeaderExtensions
{
    public static int IndexOf(this SepReaderHeader sep, string key1, string key2)
    {
        return
            sep.TryIndexOf(key1, out var colIndex)
            ? colIndex
            : sep.IndexOf(key2);
    }

    public static int IndexOf(this SepReaderHeader sep, string key, int index)
    {
        return
            sep.TryIndexOf(key, out var colIndex)
            ? colIndex
            : sep.IndexOf(sep.ColNames[index]);
    }
}