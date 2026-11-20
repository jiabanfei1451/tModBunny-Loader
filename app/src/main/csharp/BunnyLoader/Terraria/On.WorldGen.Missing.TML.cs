namespace Terraria;

/// <summary>
/// Formas que o dump do Android nao tem, referenciadas por mods:
/// <c>WorldGen.CanKillTile(int, int, ref bool)</c> so existe com outras
/// sobrecargas no dump, entao o HookGen nunca gerou esta dupla.
/// O <c>FargowiltasSouls</c> (PyramidGenSystem) guarda os dois delegates num
/// campo de closure e o tipo nao carregava sem eles.
///
/// A assinatura vem dos refs do mod:
/// <c>orig_CanKillTile_int_int_refBoolean.Invoke(int32, int32, bool&amp;)</c>
/// devolvendo <c>bool</c>. O evento fica vazio (so forma).
/// </summary>
public static partial class On_WorldGen
{
    public delegate bool orig_CanKillTile_int_int_refBoolean(int i, int j, ref bool blockDamaged);

    public delegate bool hook_CanKillTile_int_int_refBoolean(orig_CanKillTile_int_int_refBoolean orig, int i, int j, ref bool blockDamaged);

    public static event hook_CanKillTile_int_int_refBoolean? CanKillTile_int_int_refBoolean
    {
        add { }
        remove { }
    }
}
