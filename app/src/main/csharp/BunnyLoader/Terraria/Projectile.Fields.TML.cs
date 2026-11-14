namespace Terraria;

/// <summary>
/// <c>Projectile.identity</c> foi removido nesta versão do jogo (o dump só
/// tem <c>immunityIdentity</c>); o identificador estável passou a ser
/// <c>whoAmI</c>, que vem de <see cref="Entity"/>. Apelidar mantém os mods que
/// usam <c>identity</c> para indexar projéteis funcionando.
/// </summary>
public sealed partial class Projectile
{
    public ref int identity => ref whoAmI;

    public static ref int __ref_identity(global::BunnyLoader.Interop.IGameObject self) => ref __ref_whoAmI(self);

    public static void __set_identity(global::BunnyLoader.Interop.IGameObject self, int value) => __ref_whoAmI(self) = value;
}
