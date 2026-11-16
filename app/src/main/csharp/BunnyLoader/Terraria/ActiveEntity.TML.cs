using Terraria.ModLoader;

namespace Terraria;

// v101: ActiveEntityIterator<T> precisa saber se a entidade esta viva, mas os
// proxies gerados sao classes isoladas (nao herdam de Terraria.Entity), entao a
// liveness vem por interface explicita.

public unsafe sealed partial class Player : IActiveEntity
{
    bool IActiveEntity.IsActive => active;
}

public unsafe sealed partial class NPC : IActiveEntity
{
    bool IActiveEntity.IsActive => active;
}

public unsafe sealed partial class Projectile : IActiveEntity
{
    bool IActiveEntity.IsActive => active;
}

public unsafe sealed partial class WorldItem : IActiveEntity
{
    bool IActiveEntity.IsActive => active;
}
