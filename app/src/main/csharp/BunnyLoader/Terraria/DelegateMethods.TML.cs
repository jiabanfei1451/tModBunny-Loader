namespace Terraria;

/// <summary>
/// <c>DelegateMethods.CastLight</c>, <c>CastLightOpen</c> e <c>CutTiles</c>
/// como MÉTODOS, que é a forma do 1.4.4.9 (PC).
///
/// No 1.4.5.8 do Android estes três são só CAMPOS
/// (<c>Utils.TileActionAttempt</c> — um ponteiro de função nativo), e o
/// gerado os expõe como propriedade. O mod, compilado contra o 1.4.4.9,
/// faz <c>ldftn Terraria.DelegateMethods::CastLight(int,int)</c> para montar
/// o delegate da própria closure
/// (<c>newobj Utils.TileActionAttempt::.ctor(object, IntPtr)</c>) e passa
/// para <c>Utils.PlotTileLine</c>. Como aqui não existe o método, o JIT
/// recusa o método inteiro:
/// <c>MissingMethodException: Method not found: 'Boolean
/// Terraria.DelegateMethods.CastLight(Int32, Int32)'</c> — 15 ocorrências
/// no FargowiltasSouls (NanoBlade, BaseDeathray, PrimeDeathray,
/// PlasmaDeathRay, TrueEyeS/L, RingDeathray, AbomPhoenix, MeteorEnchant…).
///
/// Não dá para simplesmente ler o campo delegado: o ponteiro nativo pode
/// estar zerado (o <c>.cctor</c> do dummy vem vazio). Por isso a
/// implementação desvia para as funções <c>*Func</c>/<c>*Imp</c> que o
/// próprio gerado já tem — elas chamam o método nativo por assinatura,
/// com o <c>MethodRef</c> resolvido pelo <c>GameInterop</c>.
///
/// O nome original é liberado pelo gerado via <c>!</c> em
/// <c>ProxyGen/types.txt</c> (o gerado passa a declarar <c>CastLight_</c>).
/// </summary>
public static partial class DelegateMethods
{
    /// <summary>Ilumina o tile (x, y). Era campo no 1.4.5.8, é método no 1.4.4.9.</summary>
    public static bool CastLight(int x, int y) => CastLightFunc(x, y);

    /// <summary>Idem, ignorando sólidos — variante usada pelos deathrays.</summary>
    public static bool CastLightOpen(int x, int y) => CastLightOpenFunc(x, y);

    /// <summary>Corta o tile (x, y) respeitando <c>tilecut_0</c>.</summary>
    public static bool CutTiles(int x, int y) => CutTilesImp(x, y);

    /// <summary>
    /// Busca um tile que NPCs evitam. O FargowiltasSouls não referencia hoje,
    /// mas fecha o grupo dos quatro campos delegados por consistência.
    /// </summary>
    public static bool SearchAvoidedByNPCs(int x, int y) => SearchAvoidedByNPCsImp(x, y);
}
