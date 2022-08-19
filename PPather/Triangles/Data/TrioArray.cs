
namespace PPather.Triangles.Data
{
    public struct TrioArray<T>
    {
        private const int SIZE = 1024; // Max size if SIZE*SIZE = 16M

        // Jagged array
        // pointer chasing FTL

        // SIZE*(SIZE*3)
        private T[][] arrays;

        public TrioArray()
        {
            arrays = new T[SIZE][];
        }

        private static void getIndices(int index, out int i0, out int i1)
        {
            i1 = index % SIZE;
            index /= SIZE;
            i0 = index % SIZE;
        }

        private void allocateAt(int i0)
        {
            T[] a1 = arrays[i0];
            if (a1 == null)
            {
                arrays[i0] = new T[SIZE * 3];
            }
        }

        public void SetSize(int new_size)
        {
            getIndices(new_size, out int i0, out _);
            for (int i = i0 + 1; i < SIZE; i++)
                arrays[i] = null;
        }

        public void Set(int index, T x, T y, T z)
        {
            getIndices(index, out int i0, out int i1);
            allocateAt(i0);
            T[] innermost = arrays[i0];
            i1 *= 3;
            innermost[i1 + 0] = x;
            innermost[i1 + 1] = y;
            innermost[i1 + 2] = z;
        }

        public void Get(int index, out T x, out T y, out T z)
        {
            getIndices(index, out int i0, out int i1);

            T[] a1 = arrays[i0];
            if (a1 == null)
            {
                x = default;
                y = default;
                z = default;
                return;
            }

            T[] innermost = arrays[i0];
            i1 *= 3;
            x = innermost[i1 + 0];
            y = innermost[i1 + 1];
            z = innermost[i1 + 2];
        }
    }

}
