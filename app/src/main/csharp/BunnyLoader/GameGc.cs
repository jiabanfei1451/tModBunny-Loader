using System;

namespace BunnyLoader;

// Epoch de coletas do GC.
//
// O arquivo original deste helper se perdeu no merge com o upstream (o tipo era
// usado por EntityInstances, PlayerLoader, AccessorySlotHooks, BossBarLoader e
// ExtraJumpLoader, mas nao existia mais em nenhuma arvore). A funcao e' so dar
// um numero que muda quando o GC roda, para os caches de instancia saberem que
// os handles podem ter sido coletados sem consultar o runtime nativo a cada
// acesso. -1 continua significando "sem informacao" nos chamadores.
internal static class GameGc
{
    internal static long Collections() => GC.CollectionCount(0);
}
