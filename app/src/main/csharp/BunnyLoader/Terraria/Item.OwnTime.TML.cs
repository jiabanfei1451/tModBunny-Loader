namespace Terraria;

public sealed partial class Item
{
    // O PC 1.4.5 tem `Item.ownTime` (frames de posse, para os itens que só
    // caem/podem ser pegos depois de um tempo). O dump deste jogo
    // (1.4.5.8.6) NÃO tem esse campo — um mod PC que faz `ldfld` nele
    // derrubaria o SetDefaults. Campo gerenciado em espelho: o mod lê e
    // escreve normalmente, mas a lógica nativa não o conhece (perde a
    // semântica de jogo, não o carregamento).
    public int ownTime;
}
