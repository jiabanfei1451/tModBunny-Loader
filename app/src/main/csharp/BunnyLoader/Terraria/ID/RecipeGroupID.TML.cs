using BunnyLoader.Interop;

namespace Terraria.ID;

/// <summary>
/// Ids dos grupos de receita da VANILLA (<c>RecipeGroupID</c> do tML/jogo).
///
/// O tipo não existe neste dump — a versão de Android registra os grupos
/// direto com <c>RecipeGroup.Register()</c>, sem guardar o id em constantes.
/// Como a ORDEM de registro é interna, o id é descoberto em tempo de execução
/// pela "isca": procura-se o grupo nativo que contém um item conhecido dele
/// (<see cref="RecipeGroup.Contains"/>), e é esse índice que devolvemos.
///
/// É o que mods do PC fazem com <c>RecipeGroupID.Wood</c> em
/// <c>Recipe.Create(...).AddRecipeGroup(...)</c>: sem isso o método inteiro
/// não carrega (TypeLoadException no tipo) e as receitas do mod não existem.
/// </summary>
public static class RecipeGroupID
{
    /// <summary>Item que só existe em cada grupo vanilla, usado de isca.</summary>
    const int IronBarProbe = 22;     // Iron Bar (o grupo tem Iron e Lead)
    const int SquirrelProbe = 2015;  // Squirrel
    const int WoodProbe = 9;         // Wood
    const int SandProbe = 169;       // Sand Block
    const int FragmentProbe = 3456;  // Fragmento (grupo dos 4 pilares)
    const int PressureTrapProbe = 1351;
    const int BirdProbe = 2015 + 1;  // Bird

    public static int IronBar => Find(IronBarProbe);
    public static int Squirrels => Find(SquirrelProbe);
    public static int Wood => Find(WoodProbe);
    public static int Sand => Find(SandProbe);
    public static int Fragments => Find(FragmentProbe);
    public static int PressureTrap => Find(PressureTrapProbe);
    public static int Birds => Find(BirdProbe);

    /// <summary>Id do grupo nativo que contém <paramref name="probeItemId"/>.</summary>
    internal static int Find(int probeItemId)
    {
        // Já registrado por nós (grupos de mod) e achado antes: cache.
        if (Cached.TryGetValue(probeItemId, out int known)) return known;

        var groups = new GameDictionary(RecipeGroup.recipeGroups);
        if (groups.IsNull) return 0;

        int count = RecipeGroup.nextRecipeGroupIndex;
        for (int id = 0; id < count; id++)
        {
            if (!groups.TryGetReference(id, out nint pointer) || pointer == 0) continue;
            if (!new RecipeGroup(pointer).Contains(probeItemId)) continue;
            Cached[probeItemId] = id;
            return id;
        }
        return 0;
    }

    static readonly System.Collections.Generic.Dictionary<int, int> Cached = new();
}
