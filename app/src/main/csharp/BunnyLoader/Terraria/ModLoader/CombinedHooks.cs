namespace Terraria.ModLoader;

/// <summary>
/// <c>Terraria.ModLoader.CombinedHooks</c> — os encaminhadores estáticos que o
/// tML usa para chamar todos os hooks de todos os mods de uma vez.
///
/// Por que existe: o <c>FargowiltasSouls</c> faz
/// <c>ldtoken Terraria.ModLoader.CombinedHooks</c> no seu <c>.cctor</c>
/// (<c>FargowiltasSouls..cctor @IL_001A</c>) e o <c>Fargowiltas</c> chama
/// <c>CombinedHooks.CanUseItem(Player, Item)</c>. Sem o tipo, o construtor
/// estático estourava com <c>TypeLoadException</c> e o mod inteiro ficava de
/// fora — foi o bloqueio seguinte aos 69 tipos ausentes, invisível para o
/// <c>ildump --hard</c> porque só aparece como token dentro de um corpo.
///
/// Só entram os membros que algum mod realmente chama. Um método a mais aqui
/// seria um valor inventado mudando o jogo em silêncio; a falta dele é
/// <c>MissingMethodException</c> no ponto de chamada, que o reescritor isola e
/// o log mostra com nome e assinatura.
/// </summary>
public static class CombinedHooks
{
    /// <summary>
    /// tML 1.4.5: <c>PlayerLoader.CanUseItem(player, item) &amp;&amp; ItemLoader.CanUseItem(item, player)</c>.
    /// Aqui os dois lados já estão juntos em <see cref="Core.ItemHooks"/> — é o
    /// mesmo dispatch que roda no gancho nativo de <c>ItemCheck_CheckCanUse_Inner</c>,
    /// então o resultado é idêntico ao do caminho do jogo.
    /// </summary>
    public static bool CanUseItem(Player player, Item item) => Core.ItemHooks.CanUseItem(item, player);
}
