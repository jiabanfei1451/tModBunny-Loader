using BunnyLoader.Interop;
using Microsoft.Xna.Framework;

namespace Terraria;

/// <summary>
/// O PC declara <c>Dust.NewDustPerfect(Vector2, int, Vector2?, int, Color,
/// float)</c>; o proxy troca <c>Vector2?</c> por <c>GameNullable&lt;Vector2&gt;</c>,
/// o que o IlPatcher le como ASSIN e o mod morre em MissingMethodException
/// (FargowiltasSouls usa em 5 pontos). A conversao implicita entre
/// <c>T?</c> e <c>GameNullable&lt;T&gt;</c> ja existe, entao basta um
/// encaminhamento.
/// </summary>
public unsafe sealed partial class Dust
{
    public static Dust NewDustPerfect(Vector2 Position, int Type, Vector2? Velocity, int Alpha = 0,
        Color newColor = default, float Scale = 1f) =>
        NewDustPerfect(Position, Type, (GameNullable<Vector2>)Velocity, Alpha, newColor, Scale);
}
