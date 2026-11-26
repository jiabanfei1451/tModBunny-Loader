namespace Terraria.Utilities;

/// <summary>
/// O <c>UnifiedRandom</c> do PC tem construtor com semente de verdade
/// (<c>public UnifiedRandom(int seed)</c>). O ProxyGen so expoe o construtor
/// de ponteiro e a fabrica <see cref="New(int)"/>, entao o codigo do mod
/// (<c>new UnifiedRandom(seed)</c>) morria em
/// <c>MissingMethodException: Void UnifiedRandom..ctor(System.Int32)</c> —
/// 5 ocorrencias no sandbox (LightningArc, LunarCultistLightningArc,
/// CopperLightning, CelestialRuneLightningArc, PenetratorArc).
/// </summary>
public unsafe sealed partial class UnifiedRandom
{
    public UnifiedRandom(int seed) : this(New(seed).Pointer)
    {
    }
}
