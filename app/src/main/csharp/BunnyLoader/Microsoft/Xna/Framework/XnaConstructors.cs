using System;

namespace Microsoft.Xna.Framework
{
    public partial struct Vector2
    {
        public Vector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public Vector2(float value) : this(value, value) { }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);

        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);

        public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);

        public static Vector2 operator *(float s, Vector2 a) => new(a.X * s, a.Y * s);

        public static Vector2 operator /(Vector2 a, float s) => new(a.X / s, a.Y / s);

        public static Vector2 operator *(Vector2 a, Vector2 b) => new(a.X * b.X, a.Y * b.Y);

        public static Vector2 operator /(Vector2 a, Vector2 b) => new(a.X / b.X, a.Y / b.Y);

        public static Vector2 operator -(Vector2 a) => new(-a.X, -a.Y);

        public static bool operator ==(Vector2 a, Vector2 b) => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(Vector2 a, Vector2 b) => a.X != b.X || a.Y != b.Y;

        public override readonly bool Equals(object? obj) => obj is Vector2 other && this == other;

        public override readonly int GetHashCode() => HashCode.Combine(X, Y);

        public readonly float LengthValue => MathF.Sqrt(X * X + Y * Y);

        public override readonly string ToString() => $"{{X:{X} Y:{Y}}}";
    }

    public partial struct Vector3
    {
        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector3 operator *(Vector3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

        public static Vector3 operator *(float s, Vector3 a) => new(a.X * s, a.Y * s, a.Z * s);

        public static Vector3 operator /(Vector3 a, float s) => new(a.X / s, a.Y / s, a.Z / s);

        public override readonly string ToString() => $"{{X:{X} Y:{Y} Z:{Z}}}";
    }

    public partial struct Vector4
    {
        public Vector4(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    public partial struct Point
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public partial struct Rectangle
    {
        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public override readonly string ToString() => $"{{X:{X} Y:{Y} Width:{Width} Height:{Height}}}";
    }
}

namespace Microsoft.Xna.Framework
{
    /// <summary>
    /// No dump deste port o tipo mora em <c>Microsoft.Xna.Framework.Graphics</c>
    /// (o ProxyGen reescreve o namespace do proxy para o nome do PC, veja
    /// <c>TypeMap.ProxyNamespace</c>) — a declaração parcial tem de morar no
    /// MESMO namespace do arquivo gerado, senão viram dois tipos.
    /// </summary>
    public partial struct Color
    {
        public Color(int r, int g, int b) : this(r, g, b, 255) { }

        public Color(int r, int g, int b, int a)
        {
            R = (byte)Math.Clamp(r, 0, 255);
            G = (byte)Math.Clamp(g, 0, 255);
            B = (byte)Math.Clamp(b, 0, 255);
            A = (byte)Math.Clamp(a, 0, 255);
        }

        public Color(float r, float g, float b) : this(r, g, b, 1f) { }

        public static Color operator *(Color value, float scale) =>
            new((int)(value.R * scale), (int)(value.G * scale), (int)(value.B * scale), (int)(value.A * scale));

        public static Color operator *(float scale, Color value) => value * scale;

        public static bool operator ==(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;

        public static bool operator !=(Color a, Color b) => !(a == b);

        public override readonly bool Equals(object? obj) => obj is Color other && this == other;

        public override readonly int GetHashCode() => R | G << 8 | B << 16 | A << 24;

        public Color(float r, float g, float b, float a)
            : this((int)(r * 255f + 0.5f), (int)(g * 255f + 0.5f), (int)(b * 255f + 0.5f), (int)(a * 255f + 0.5f)) { }

        /// <summary>
        /// FNA: <c>public Color(Vector3 vector) : this(vector.X, vector.Y, vector.Z)</c>
        /// (alfa 255). O dump do port tem o construtor
        /// (<c>refs/dump.cs:42022</c>) mas o ProxyGen nao o expoe — sem ele o mod
        /// nao compila nem JITa (<c>Color..ctor(Vector3)</c>).
        /// </summary>
        public Color(Vector3 vector) : this(vector.X, vector.Y, vector.Z) { }

        /// <summary>FNA: <c>public Color(Vector4 vector) : this(vector.X, vector.Y, vector.Z, vector.W)</c>.</summary>
        public Color(Vector4 vector) : this(vector.X, vector.Y, vector.Z, vector.W) { }

        public override readonly string ToString() => $"{{R:{R} G:{G} B:{B} A:{A}}}";
    }
}

namespace Microsoft.Xna.Framework
{
    /// <summary>
    /// O <c>GameTime</c> da FNA tem construtores de verdade; aqui o proxy só
    /// nasce de um ponteiro nativo. Sem eles o código do mod não compila:
    /// <c>exampleCoinUserInterface.Draw(Main.spriteBatch, new GameTime())</c>.
    /// </summary>
    public partial class GameTime
    {
        public GameTime() : this(BunnyLoader.Interop.GameInterop.NewObject(Class)) { }

        public GameTime(TimeSpan totalGameTime, TimeSpan elapsedGameTime)
            : this() { }

        public GameTime(TimeSpan totalGameTime, TimeSpan elapsedGameTime, bool isRunning)
            : this() { }
    }
}
