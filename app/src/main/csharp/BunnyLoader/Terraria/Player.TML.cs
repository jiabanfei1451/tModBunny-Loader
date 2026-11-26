using System;
using System.Collections.Generic;
using Terraria.ID;
using Terraria.ModLoader;

namespace Terraria;

public sealed partial class Player
{
    public HashSet<int> NearbyModTorch => Terraria.ModLoader.Core.TileHooks.ModTorches(whoAmI);

    public bool HasBuff(int type) => FindBuffIndex(type) != -1;

    public const int LifeCrystalMax = 15;

    public const int LifeFruitMax = 20;

    public const int ManaCrystalMax = 9;

    public int ConsumedLifeCrystals
    {
        get => Math.Clamp((Math.Min(statLifeMax, 400) - 100) / 20, 0, LifeCrystalMax);
        set => statLifeMax = 100 + 20 * Math.Clamp(value, 0, LifeCrystalMax) + 5 * ConsumedLifeFruit;
    }

    public int ConsumedLifeFruit
    {
        get => Math.Clamp((statLifeMax - 400) / 5, 0, LifeFruitMax);
        set => statLifeMax = 100 + 20 * ConsumedLifeCrystals + 5 * Math.Clamp(value, 0, LifeFruitMax);
    }

    public int ConsumedManaCrystals
    {
        get => Math.Clamp((statManaMax - 20) / 20, 0, ManaCrystalMax);
        set => statManaMax = 20 + 20 * Math.Clamp(value, 0, ManaCrystalMax);
    }

    public void UseHealthMaxIncreasingItem(int increase)
    {
        increase = Math.Max(0, increase);
        statLifeMax2 += increase;
        statLife += increase;
        if (Main.myPlayer == whoAmI) HealEffect(increase);
    }

    public void UseManaMaxIncreasingItem(int increase)
    {
        increase = Math.Max(0, increase);
        statManaMax2 += increase;
        statMana += increase;
        if (Main.myPlayer == whoAmI) ManaEffect(increase);
    }

    public void AddImmuneTime(int cooldownCounterId, int immuneTime)
    {
        if (cooldownCounterId < 0) this.immuneTime += immuneTime;
        else hurtCooldowns[cooldownCounterId] += immuneTime;
    }

    public ModPlayer[] ModPlayers => PlayerLoader.Of(this);

    public T GetModPlayer<T>() where T : ModPlayer => GetModPlayer(ModContent.GetInstance<T>());

    public T GetModPlayer<T>(T baseInstance) where T : ModPlayer =>
        PlayerLoader.Of(this)[baseInstance.Index] as T ?? throw new KeyNotFoundException($"Instance of '{typeof(T).Name}' does not exist on the current player.");

    public bool TryGetModPlayer<T>(out T result) where T : ModPlayer => TryGetModPlayer(ModContent.GetInstance<T>(), out result);

    public bool TryGetModPlayer<T>(T baseInstance, out T result) where T : ModPlayer
    {
        ModPlayer[] instances = PlayerLoader.Of(this);
        if (baseInstance == null || baseInstance.Index >= instances.Length)
        {
            result = default!;
            return false;
        }
        result = (T)instances[baseInstance.Index];
        return true;
    }

    public string setBonus
    {
        get => Terraria.ModLoader.Core.ItemEquipHooks.GetSetBonus(this);
        set => Terraria.ModLoader.Core.ItemEquipHooks.SetSetBonus(this, value);
    }

    public ref StatModifier GetDamage<T>() where T : DamageClass => ref GetDamage(ModContent.GetInstance<T>());

    public ref StatModifier GetDamage(DamageClass damageClass) => ref DamageClassLoader.Stats(this, damageClass).damage;

    public ref float GetCritChance<T>() where T : DamageClass => ref GetCritChance(ModContent.GetInstance<T>());

    public ref float GetCritChance(DamageClass damageClass) => ref DamageClassLoader.Stats(this, damageClass).critChance;

    public ref float GetAttackSpeed<T>() where T : DamageClass => ref GetAttackSpeed(ModContent.GetInstance<T>());

    public ref float GetAttackSpeed(DamageClass damageClass) => ref DamageClassLoader.Stats(this, damageClass).attackSpeed;

    public ref float GetArmorPenetration<T>() where T : DamageClass => ref GetArmorPenetration(ModContent.GetInstance<T>());

    public ref float GetArmorPenetration(DamageClass damageClass) => ref DamageClassLoader.Stats(this, damageClass).armorPen;

    public ref StatModifier GetKnockback<T>() where T : DamageClass => ref GetKnockback(ModContent.GetInstance<T>());

    public ref StatModifier GetKnockback(DamageClass damageClass) => ref DamageClassLoader.Stats(this, damageClass).knockback;

    public StatModifier GetTotalDamage<T>() where T : DamageClass => GetTotalDamage(ModContent.GetInstance<T>());

    public StatModifier GetTotalDamage(DamageClass damageClass) => DamageClassLoader.Total(this, damageClass).Damage;

    public float GetTotalCritChance<T>() where T : DamageClass => GetTotalCritChance(ModContent.GetInstance<T>());

    public float GetTotalCritChance(DamageClass damageClass) => DamageClassLoader.Total(this, damageClass).Crit;

    public float GetTotalAttackSpeed<T>() where T : DamageClass => GetTotalAttackSpeed(ModContent.GetInstance<T>());

    public float GetTotalAttackSpeed(DamageClass damageClass) => DamageClassLoader.Total(this, damageClass).Speed;

    public float GetTotalArmorPenetration<T>() where T : DamageClass => GetTotalArmorPenetration(ModContent.GetInstance<T>());

    public float GetTotalArmorPenetration(DamageClass damageClass) => DamageClassLoader.Total(this, damageClass).Pen;

    public StatModifier GetTotalKnockback<T>() where T : DamageClass => GetTotalKnockback(ModContent.GetInstance<T>());

    public StatModifier GetTotalKnockback(DamageClass damageClass) => DamageClassLoader.Total(this, damageClass).Knockback;

    public float GetWeaponAttackSpeed(Item sItem)
    {
        float speed = GetTotalAttackSpeed(sItem.DamageType);
        var multiplier = ItemID.Sets.BonusMeleeSpeedMultiplier;
        int type = sItem.type;
        float bonus = !multiplier.IsNull && (uint)type < (uint)multiplier.Length ? multiplier[type] : 1f;
        return 1 + (speed - 1) * bonus;
    }

    public int GetWeaponArmorPenetration(Item sItem) => (int)(sItem.armorPenetration + GetTotalArmorPenetration(sItem.DamageType));

    /// <summary>
    /// A sobrecarga de 4 parâmetros é do tML (o jogo só tem 3). O último
    /// argumento é "veio de outro jogador", que aqui não muda o efeito.
    /// </summary>
    public void AddBuff(int type, int time, bool fromNetPvP, bool fromPlayer) => AddBuff(type, time, fromNetPvP);

    /// <summary>
    /// O jogo devolve <c>GameNullable&lt;Vector2&gt;</c> (o <c>Nullable</c> do
    /// Il2Cpp é um struct próprio). O contrato do 1.4.4.9 e o mod usam
    /// <c>Nullable&lt;Vector2&gt;</c>, então a geração é reservada em tipos.txt
    /// (<c>!Terraria.Player.HandPosition</c>) e a propriedade é reescrita aqui.
    /// </summary>
    public Vector2? HandPosition => (Vector2?)HandPosition_;

    /// <summary>
    /// Buffer de <see cref="adjTile"/>. Nome diferente de <c>adjTile_</c> porque
    /// esse é o nome que o ProxyGen dá à propriedade gerada quando o membro é
    /// reservado em tipos.txt.
    /// </summary>
    bool[] adjTileCache = new bool[Terraria.ModLoader.TileLoader.TileCount];

    /// <summary>
    /// O jogo expõe <c>ref GameArray&lt;bool&gt;</c> (ponteiro para o Il2CppArray),
    /// mas o contrato do 1.4.4.9 é <c>bool[]</c> e o mod escreve com
    /// <c>stelem.i1</c> — só funciona em array gerenciado. A geração é reservada
    /// em tipos.txt (<c>!Terraria.Player.adjTile</c>) e a propriedade refeita
    /// aqui, com o mesmo redimensionamento do contrato
    /// (<c>Player.cs.patch:965-972</c>).
    /// </summary>
    public bool[] adjTile
    {
        get
        {
            int count = Terraria.ModLoader.TileLoader.TileCount;
            if (adjTileCache.Length != count) Array.Resize(ref adjTileCache, count);
            return adjTileCache;
        }

        set => adjTileCache = value;
    }
}
