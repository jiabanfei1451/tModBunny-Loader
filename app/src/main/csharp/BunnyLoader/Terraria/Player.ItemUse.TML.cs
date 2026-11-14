using Terraria.DataStructures;
using Terraria.ModLoader.Core;

namespace Terraria;

public sealed partial class Player
{
    public bool ItemAnimationEndingOrEnded => itemAnimation <= 1;
    public int ItemUsesThisAnimation => PlayerItemUseState.Uses(this);
    public bool HasAmmo(Item item) => HasAmmo(item, true);
    public IEntitySource GetSource_ItemUse_WithPotentialAmmo(Item item, int ammoItemId, string? context = null) => GetProjectileSource_Item_WithPotentialAmmo(item, ammoItemId);

    /// <summary>
    /// O tML devolve <c>EntitySource_ItemUse</c>; aqui o jogo tem o mesmo
    /// conceito em <c>GetProjectileSource_Item</c> (sem o <c>context</c>, que
    /// no tML é só rótulo de diagnóstico).
    /// </summary>
    public IEntitySource GetSource_ItemUse(Item item, string? context = null) => GetProjectileSource_Item(item);

    public bool PickAmmo(Item item, out int projectile, out float speed, out int damage, out float knockback, out int usedAmmoItemId, bool dontConsume = false)
    {
        projectile = item.shoot;
        speed = item.shootSpeed;
        damage = GetWeaponDamage(item);
        knockback = GetWeaponKnockback(item, item.knockBack);
        bool canShoot = true;
        PickAmmo(item, ref projectile, ref speed, ref canShoot, ref damage, ref knockback, out usedAmmoItemId, dontConsume);
        return canShoot;
    }
}
