using System;
using System.Runtime.CompilerServices;

namespace Core
{
    [Serializable]
    public struct Vector3 : IEquatable<Vector3>
    {
        public static readonly Vector3 Zero = new Vector3(0, 0, 0);

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3(Vector3 position) : this(position.X, position.Y, position.Z)
        {
        }

        public float X { get; set; }

        public float Y { get; set; }

        public float Z { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 FromArray(float[] array)
        {
            return new Vector3()
            {
                X = array[0],
                Y = array[1],
                Z = array[2]
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator -(Vector3 a, float b)
        {
            return new Vector3(a.X - b, a.Y - b, a.Z - b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Vector3 left, Vector3 right)
        {
            return !(left == right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(float a, Vector3 b)
        {
            return new Vector3(b.X * a, b.Y * a, b.Z * a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 a, float b)
        {
            return new Vector3(a.X * b, a.Y * b, a.Z * b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator *(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator /(Vector3 a, float b)
        {
            return new Vector3(a.X / b, a.Y / b, a.Z / b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 a, float b)
        {
            return new Vector3(a.X + b, a.Y + b, a.Z + b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 operator +(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        }

        /*
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Vector3 left, Vector3 right)
        {
            return left.X < right.X
                   && left.Y < right.Y
                   && left.Z < right.Z;
        }
        */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Vector3 left, Vector3 right)
        {
            return left.Equals(right);
        }

        /*
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Vector3 left, Vector3 right)
        {
            return left.X > right.X
                   && left.Y > right.Y
                   && left.Z > right.Z;
        }
        */

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(Vector3 vector)
        {
            X += vector.X;
            Y += vector.Y;
            Z += vector.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(float n)
        {
            X += n;
            Y += n;
            Z += n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Divide(Vector3 vector)
        {
            X = vector.X > 0 ? X / vector.X : 0;
            Y = vector.Y > 0 ? Y / vector.Y : 0;
            Z = vector.Z > 0 ? Z / vector.Z : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Divide(float n)
        {
            X = n > 0 ? X / n : 0;
            Y = n > 0 ? Y / n : 0;
            Z = n > 0 ? Z / n : 0;
        }

        public override bool Equals(object obj)
        {
            return obj.GetType() == typeof(Vector3)
                       && ((Vector3)obj).X == X
                       && ((Vector3)obj).Y == Y
                       && ((Vector3)obj).Z == Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistance(Vector3 b)
        {
            return Math.Sqrt(((X - b.X) * (X - b.X)) + ((Y - b.Y) * (Y - b.Y)) + ((Z - b.Z) * (Z - b.Z)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double GetDistance2D(Vector3 b)
        {
            return Math.Sqrt(((X - b.X) * (X - b.X)) + ((Y - b.Y) * (Y - b.Y)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            unchecked
            {
                return (int)(17 + (X * 23) + (Y * 23) + (Z * 23));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetMagnitude()
        {
            return Convert.ToSingle(Math.Sqrt((X * X) + (Y * Y) + (Z * Z)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetMagnitude2D()
        {
            return Convert.ToSingle(Math.Sqrt((X * X) + (Y * Y)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Limit(float limit)
        {
            X = X < 0f ? Math.Max(X, limit * -1) : Math.Min(X, limit);
            Y = Y < 0f ? Math.Max(Y, limit * -1) : Math.Min(Y, limit);
            Z = Z < 0f ? Math.Max(Z, limit * -1) : Math.Min(Z, limit);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Multiply(Vector3 vector)
        {
            X = vector.X > 0 ? X * vector.X : 0;
            Y = vector.Y > 0 ? Y * vector.Y : 0;
            Z = vector.Z > 0 ? Z * vector.Z : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Multiply(float n)
        {
            X = n > 0 ? X * n : 0;
            Y = n > 0 ? Y * n : 0;
            Z = n > 0 ? Z * n : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            Normalize(GetMagnitude());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize(float magnitude)
        {
            if (magnitude > 0)
            {
                X /= magnitude;
                Y /= magnitude;
                Z /= magnitude;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize2D()
        {
            Normalize2D(GetMagnitude2D());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize2D(float magnitude)
        {
            if (magnitude > 0)
            {
                X /= magnitude;
                Y /= magnitude;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Rotate(double degrees)
        {
            RotateRadians(degrees * (Math.PI / 180));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RotateRadians(double radians)
        {
            double ca = Math.Cos(radians);
            double sa = Math.Sin(radians);

            X = Convert.ToSingle(ca * X - sa * Y);
            Y = Convert.ToSingle(sa * X + ca * Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Subtract(float n)
        {
            X -= n;
            Y -= n;
            Z -= n;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Subtract(Vector3 vector)
        {
            X -= vector.X;
            Y -= vector.Y;
            Z -= vector.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float[] ToArray()
        {
            return new float[3] { X, Y, Z };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return $"X: {X}, Y: {Y}, Z: {Z}";
        }

        public bool Equals(Vector3 other)
        {
            throw new NotImplementedException();
        }
    }
}