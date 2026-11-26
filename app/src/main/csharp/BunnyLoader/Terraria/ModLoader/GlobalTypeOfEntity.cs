using System;

namespace Terraria.ModLoader;

public abstract class GlobalType<TEntity, TGlobal> : GlobalType where TGlobal : GlobalType<TEntity, TGlobal>
{
    public virtual bool AppliesToEntity(TEntity entity, bool lateInstantiation) => true;

    /// <summary>
    /// Contrato 1.4.4.9 (`GlobalType.cs:128`): <c>public virtual void SetDefaults(TEntity entity) { }</c>.
    /// Sem ele, todo <c>GlobalNPC/GlobalItem/GlobalProjectile</c> que chama
    /// <c>base.SetDefaults(...)</c> — ou que e' despachado pelo
    /// <c>GlobalType`2</c> — morre com
    /// <c>MissingMethodException: GlobalType`2.SetDefaults(!0)</c> e o JIT
    /// recusa o metodo inteiro (87 ocorrencias no FargowiltasSouls).
    /// </summary>
    public virtual void SetDefaults(TEntity entity)
    {
    }

    public virtual TGlobal NewInstance(TEntity target)
    {
        if (!InstancePerEntity) return (TGlobal)this;
        var instance = (TGlobal)Activator.CreateInstance(GetType(), true)!;
        instance.Mod = Mod;
        instance.StaticIndex = StaticIndex;
        return instance;
    }

    public virtual TGlobal Clone(TEntity? from, TEntity to)
    {
        if (!InstancePerEntity) throw new InvalidOperationException($"{FullName}: Clone is only for InstancePerEntity globals");
        return (TGlobal)MemberwiseClone();
    }
}
