using static System.MathF;

namespace PPather.Triangles.Data
{
    public class SparseFloatMatrix2D<T> : SparseMatrix2D<T>
    {
        private const float offset = 100000f;
        private readonly float gridSize;

        public SparseFloatMatrix2D(float gridSize) : base(0)
        {
            this.gridSize = gridSize;
        }

        public SparseFloatMatrix2D(float gridSize, int initialCapazity)
            : base(initialCapazity)
        {
            this.gridSize = gridSize;
        }

        public float GridToLocal(int grid)
        {
            return (grid * gridSize) - offset;
        }

        public int LocalToGrid(float f)
        {
            return (int)((f + offset) / gridSize);
        }

        public (T[], int) GetAllInSquare(float min_x, float min_y,
                                      float max_x, float max_y)
        {
            int sx = LocalToGrid(min_x);
            int ex = LocalToGrid(max_x);

            int sy = LocalToGrid(min_y);
            int ey = LocalToGrid(max_y);

            T[] l = new T[(int)Ceiling(((ex - sx + 1) * gridSize) + ((ey - sy + 1) * gridSize))];
            int i = 0;
            for (int x = sx; x <= ex; x++)
            {
                for (int y = sy; y <= ey; y++)
                {
                    if (base.TryGetValue(x, y, out T t))
                    {
                        l[i++] = t ?? default;
                    }
                }
            }

            return (l, i);
        }

        public bool TryGetValue(float x, float y, out T t)
        {
            return base.TryGetValue(LocalToGrid(x), LocalToGrid(y), out t);
        }

        public void Add(float x, float y, T val)
        {
            base.Add(LocalToGrid(x), LocalToGrid(y), val);
        }
    }

}
