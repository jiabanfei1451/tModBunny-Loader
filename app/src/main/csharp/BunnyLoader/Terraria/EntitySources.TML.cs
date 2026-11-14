using Terraria.DataStructures;

namespace Terraria.DataStructures
{
    public sealed partial class EntitySource_ShakeTree
    {
        public EntitySource_ShakeTree(int x, int y) : this(New(x, y).Pointer)
        {
        }
    }

    public sealed partial class EntitySource_TileBreak
    {
        public EntitySource_TileBreak(int tileCoordsX, int tileCoordsY, string? context = null) : this(New(tileCoordsX, tileCoordsY).Pointer) { }
    }
}

namespace Terraria
{
    public sealed partial class Player
    {
        public IEntitySource GetSource_FromThis(string? context = null) => EntitySource_Parent.New(this);

        public IEntitySource GetSource_Buff(int buffIndex) => GetProjectileSource_Buff(buffIndex);

        public IEntitySource GetSource_Accessory(Item item, string? context = null) => GetProjectileSource_Accessory(item);

        public IEntitySource GetSource_Accessory_OnHurt(Item item, Entity attacker, string? context = null) => GetProjectileSource_Accessory(item);

        public IEntitySource GetSource_Accessory_OnHurt(Item item, PlayerDeathReason damageSource, string? context = null) => GetProjectileSource_Accessory(item);
    }

    public sealed partial class NPC
    {
        public IEntitySource GetSource_FromThis(string? context = null) => EntitySource_Parent.New(this);

        public IEntitySource GetSource_Loot(string? context = null) => GetItemSource_Loot();

        public IEntitySource GetSource_Death(string? context = null) => EntitySource_Parent.New(this);

        public IEntitySource GetSource_FromAI(string? context = null) => EntitySource_Parent.New(this);

        public IEntitySource GetSource_GiftOrReward(string? context = null) => EntitySource_Parent.New(this);
    }

    public sealed partial class Entity
    {
        public static IEntitySource GetSource_TownSpawn() => EntitySource_SpawnNPC.New();

        public static IEntitySource GetSource_NaturalSpawn() => EntitySource_SpawnNPC.New();

        /// <summary>
        /// No tML este método está em <c>Entity</c> e os derivados herdam; aqui
        /// cada proxy é uma classe solta, então ele existe em Player/NPC/
        /// Projectile — e também precisa existir aqui para quem chama por
        /// variável tipada como <c>Entity</c>.
        /// </summary>
        public IEntitySource GetSource_FromThis(string? context = null) => EntitySource_Parent.New(this);

        /// <summary>Idem: no tML é de <c>Entity</c>, e aqui só o NPC tinha o nativo.</summary>
        public IEntitySource GetSource_Loot(string? context = null) => EntitySource_Parent.New(this);
    }

    public sealed partial class Projectile
    {
        public IEntitySource GetSource_FromThis(string? context = null) => EntitySource_Parent.New(this);
    }
}
