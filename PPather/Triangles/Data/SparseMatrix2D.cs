using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PPather.Triangles.Data;

public class SparseMatrix2D<T>
{
    private readonly Dictionary<int, T> dict;

    public int Count => dict.Count;

    public Dictionary<int, T> Dict
    {
        get => dict;
    }

    public SparseMatrix2D(int initialCapacity)
    {
        dict = new(initialCapacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetKey(int x, int y)
    {
        return (y << 16) ^ x;
    }

    public bool ContainsKey(int x, int y)
    {
        return dict.ContainsKey(GetKey(x, y));
    }

    public bool TryGetValue(int x, int y, out T r)
    {
        return dict.TryGetValue(GetKey(x, y), out r);
    }

    public void Add(int key, T val)
    {
        dict[key] = val;
    }

    public void Add(int x, int y, T val)
    {
        dict[GetKey(x, y)] = val;
    }

    public void Remove(int x, int y)
    {
        dict.Remove(GetKey(x, y));
    }

    public void Clear()
    {
        dict.Clear();
    }

    public ICollection<T> GetAllElements()
    {
        return dict.Values;
    }
}
