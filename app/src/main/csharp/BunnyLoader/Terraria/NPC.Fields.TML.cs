namespace Terraria;

/// <summary>
/// <c>NPC.netUpdate2</c> existe no tML/PC mas não neste dump (só
/// <see cref="Projectile"/> tem). Um mod que lê o campo espera "há
/// sincronização pendente?" — responder sempre <c>false</c> é o
/// comportamento seguro: nada é reenviado à toa.
/// </summary>
public sealed partial class NPC
{
    static bool __pendingNetUpdate;

    public ref bool netUpdate2 => ref __pendingNetUpdate;

    public static ref bool __ref_netUpdate2(global::BunnyLoader.Interop.IGameObject self) => ref __pendingNetUpdate;

    public static void __set_netUpdate2(global::BunnyLoader.Interop.IGameObject self, bool value) => __pendingNetUpdate = value;
}
