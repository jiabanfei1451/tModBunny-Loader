using System;

namespace Terraria.ModLoader;

/// <summary>
/// Metade "bloco" da hierarquia do tML: <see cref="GlobalTile"/> (e, no tML,
/// também <c>GlobalWall</c>) derivam daqui.
///
/// No tML os métodos de colisão/explosão ficam em <c>GlobalBlockType</c> e não
/// em <c>GlobalTile</c>; como os mods são compilados contra o tML, o
/// <c>base.CanExplode(...)</c> deles vira <c>call GlobalBlockType::CanExplode</c>
/// — chamada NÃO virtual. Sem este tipo no shim, a classe do mod não carrega
/// (<c>TypeLoadException</c>).
/// </summary>
public abstract class GlobalBlockType : ModType
{
    public virtual bool CanPlace(int i, int j, int type) => true;

    public virtual bool CanExplode(int i, int j, int type) => true;

    public virtual bool CanKillTile(int i, int j, int type, ref bool blockDamaged) => true;

    public virtual bool CanDrop(int i, int j, int type) => true;

    public virtual void Drop(int i, int j, int type)
    {
    }

    public virtual void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem)
    {
    }

    public virtual void PlaceInWorld(int i, int j, int type, Item item)
    {
    }
}
