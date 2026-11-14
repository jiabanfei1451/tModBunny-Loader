using Terraria.ModLoader;

namespace BunnyLoader;

/// <summary>
/// O `Mod` de um `.tmod` que só tem conteúdo.
///
/// No tModLoader 1.4.5 um mod NÃO precisa de uma classe derivada de `Mod` —
/// veja <c>Core/AssemblyManager.cs</c>:
/// <code>
///   modType = modTypes.SingleOrDefault() ?? typeof(Mod);
///   // Mods don't really need a class extending Mod, we can always just make one for them
/// </code>
/// Um mod que só declara um `ModItem` (como o mod de teste `mods`) cai nesse
/// caso. Aqui `Mod` é abstrato, então criamos este substituto concreto com o
/// nome que veio do container/Info — é por ele que o Autoload acha os
/// recursos e registra o conteúdo.
/// </summary>
internal sealed class SyntheticMod : Mod
{
    readonly string name;

    internal SyntheticMod(string name) => this.name = name;

    public override string Name => name;
}
