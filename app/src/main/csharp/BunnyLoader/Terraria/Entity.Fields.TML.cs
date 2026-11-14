using BunnyLoader.Interop;

namespace Terraria;

/// <summary>
/// <c>Entity.active</c> não existe no dump deste jogo: cada derivado declara o
/// seu próprio <c>active</c> (<see cref="NPC"/>, <see cref="Projectile"/>,
/// <see cref="Player"/>). No PC o campo está em <c>Entity</c>, então o mod
/// compilado lá faz <c>ldfld Terraria.Entity::active</c> — sem esta ponte,
/// o IlPatcher não tem para onde reescrever e o acesso estoura em execução.
///
/// O desvio é por classe do objeto: o ponteiro da classe fica no cabeçalho do
/// objeto (offset 0) e <see cref="Item"/> não tem <c>active</c> nesta versão,
/// então cai num booleano morto (um item nunca fica "ativo").
/// </summary>
public unsafe sealed partial class Entity
{
    static int __oNpc = -1, __oProjectile = -1, __oPlayer = -1;

    static ref bool ActiveRef(IGameObject self)
    {
        nint klass = *(nint*)self.Pointer;
        if (klass == NPC.Class.Handle) return ref *(bool*)(self.Pointer + GameInterop.Offset(ref __oNpc, NPC.Class, "active"));
        if (klass == Projectile.Class.Handle) return ref *(bool*)(self.Pointer + GameInterop.Offset(ref __oProjectile, Projectile.Class, "active"));
        if (klass == Player.Class.Handle) return ref *(bool*)(self.Pointer + GameInterop.Offset(ref __oPlayer, Player.Class, "active"));
        return ref Dead;
    }

    static bool Dead;

    /// <summary>
    /// O <see cref="IlPatcher"/> procura uma PROPRIEDADE chamada <c>active</c>
    /// antes de tentar os acessadores: sem ela o campo fica em
    /// <c>campo sem propriedade no shim: Terraria.Entity::active</c> e o
    /// metodo inteiro e esvaziado. A Luminance le <c>Entity.active</c> em
    /// quase todo laco de projétil, entao valia a ponte tambem por este lado.
    /// </summary>
    public ref bool active => ref ActiveRef(this);

    public static ref bool __ref_active(IGameObject self) => ref ActiveRef(self);

    public static void __set_active(IGameObject self, bool value) => ActiveRef(self) = value;
}
