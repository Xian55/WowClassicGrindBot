namespace PPather.Triangles.GameV2;

public readonly record struct Tri
{
    public readonly int a;
    public readonly int b;
    public readonly int c;

    public Tri(int a, int b, int c)
    {
        this.a = a;
        this.b = b;
        this.c = c;
    }
}
