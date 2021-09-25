using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Core
{
    [Flags]
    public enum PathRequestFlags
    {
        None = 0,
        ChaikinCurve = 1,
        CatmullRomSpline = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathRequestWithLocationRequest : IEquatable<PathRequestWithLocationRequest>
    {
        public PathRequestWithLocationRequest(int mapId, Vector3 a, Vector3 b, PathRequestFlags flags = PathRequestFlags.None)
        {
            Type = 3;
            A = a;
            B = b;
            MapId = mapId;
            Flags = flags;
        }

        public int Type { get; set; }

        public Vector3 A { get; set; }

        public Vector3 B { get; set; }

        public PathRequestFlags Flags { get; set; }

        public int MapId { get; set; }


        public override bool Equals(object obj)
        {
            throw new NotImplementedException();
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        public static bool operator ==(PathRequestWithLocationRequest left, PathRequestWithLocationRequest right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PathRequestWithLocationRequest left, PathRequestWithLocationRequest right)
        {
            return !(left == right);
        }

        public bool Equals(PathRequestWithLocationRequest other)
        {
            throw new NotImplementedException();
        }
    }
}
