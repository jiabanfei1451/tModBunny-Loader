using System;
using BunnyLoader;
using BunnyLoader.Interop;

namespace Terraria.Graphics.Effects;

/// <summary>
/// O PC declara <c>GameEffect.Deactivate(object[] args)</c>; o proxy troca o
/// parametro por <c>GameArray&lt;GameObject&gt;</c>, entao o IlPatcher acusa
/// ASSIN e o mod morre em
/// <c>MissingMethodException: Void GameEffect.Deactivate(System.Object[])</c>.
///
/// Todos os chamadores do FargowiltasSouls (AbomSky/MoonLordSky/MutantSky/
/// MutantSky2 ::Update) passam <c>Array.Empty&lt;object&gt;()</c>, ou seja um
/// array de objetos VAZIO — que e exatamente o que montamos aqui. Arrays com
/// elementos exigiriam <c>box</c> de value types no runtime il2cpp, que o
/// host ainda nao expoe, entao nesse caso caimos no array vazio (os efeitos
/// de ceu ignoram os argumentos).
/// </summary>
public unsafe sealed partial class GameEffect
{
    static GameArray<GameObject> noArgs;

    public void Deactivate(object[]? args)
    {
        if (args is { Length: > 0 }) Log.Warn($"GameEffect.Deactivate: {args.Length} argumento(s) ignorado(s) no Android");

        if (noArgs.IsNull) noArgs = GameArray<GameObject>.New(new GameClass("System", "Object"), 0);
        Deactivate(noArgs);
    }
}
