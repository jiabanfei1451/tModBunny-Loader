using System;
using Terraria.ModLoader;

namespace Terraria.ID;

/// <summary>
/// Parte escrita à mão de <see cref="ItemID.Sets"/>.
///
/// <c>SortingPriorityBossSpawns</c> é uma tabela do tModLoader que diz em que
/// ordem os itens aparecem na aba "gameplay" do menu do Journey Mode. O dump do
/// Android 1.4.5.8 desta build NÃO tem esse campo (tem vários outros
/// <c>SortingPriority*</c>, mas não esse), então o ProxyGen não gerou nada e o
/// mod (Fargowiltas escreve <c>ItemID.Sets.SortingPriorityBossSpawns[Type] = n</c>
/// no SetStaticDefaults) morria com <c>MissingFieldException</c> — 99 métodos
/// travados por causa de um array.
///
/// É um array PURO do tML (o jogo não usa), então basta ter a forma certa:
/// indexado por <c>Item.type</c>, default <c>-1</c>, mesmos pares do contrato
/// (1.4.4.9 <c>patches/tModLoader/Terraria/ID/ItemID.cs.patch:578</c>).
///
/// v35: no contrato a tabela vem de <c>Factory.CreateIntSet</c>, isto é, tem
/// <c>_size == ItemLoader.ItemCount</c> DEPOIS do ResizeArrays — inclui os IDs
/// de mod (que começam em <c>ItemID.Count</c>, 6196 nesta build). A versão
/// anterior era <c>int[6000]</c>: nem os IDs vanilla cabiam, e TODOS os
/// <c>SetStaticDefaults</c> dos summons estouravam
/// <c>IndexOutOfRangeException</c> — daí itens em branco / textura nula.
/// </summary>
public sealed partial class ItemID
{
    public sealed partial class Sets
    {
        /// <summary>
        /// Folga sobre <c>ItemID.Count</c>: o mod escreve nesta tabela com o
        /// próprio <c>Type</c>, que é <c>&gt;= ItemID.Count</c>. Contrato usa
        /// <c>ItemLoader.ItemCount</c>; <see cref="GrowForModContent"/> ajusta
        /// ao número real assim que o conteúdo é registrado.
        /// </summary>
        static readonly int InitialSlots = ItemID.Count + 8192;

        /// <summary>Prioridade de ordenação de itens de invocação de chefe/evento. Default <c>-1</c>.</summary>
        public static int[] SortingPriorityBossSpawns = BuildSortingPriorityBossSpawns();

        /// <summary>
        /// Contrato 1.4.4.9: <c>BannerEffect[]</c> (ItemID.cs.patch). O dump
        /// expõe como array nativo (<c>GameArray&lt;BannerEffect&gt;</c>, um
        /// struct), e o IlPatcher reescrevia o <c>stsfld</c> do
        /// <c>Fargowiltas.Load()</c> para um setter que pedia esse struct —
        /// recebendo um array gerenciado, o JIT recusava o método INTEIRO.
        /// Sendo um CAMPO REAL do tipo que o mod espera, o `stsfld` liga
        /// direto e o Load() roda até o fim. A geração da propriedade nativa
        /// está suprimida com `!` em ProxyGen/types.txt.
        /// </summary>
        public static BannerEffect[] BannerStrength = new BannerEffect[InitialSlots];

        /// <summary>
        /// Chamado por <c>SetFactory.GrowToContent()</c> (antes de
        /// <c>SetStaticDefaults</c>). Cresce as duas tabelas escritas à mão
        /// até <c>ItemLoader.ItemCount</c> + folga, preservando o que o mod
        /// já escreveu. Sem isto um item de mod com Type alto ainda cairia
        /// fora do array.
        /// </summary>
        internal static void GrowForModContent()
        {
            int wanted = ItemLoader.ItemCount + 4096;
            if (SortingPriorityBossSpawns.Length < wanted)
            {
                var grown = new int[wanted];
                Array.Fill(grown, -1);
                Array.Copy(SortingPriorityBossSpawns, grown, SortingPriorityBossSpawns.Length);
                SortingPriorityBossSpawns = grown;
            }
            if (BannerStrength.Length < wanted)
            {
                var grown = new BannerEffect[wanted];
                Array.Copy(BannerStrength, grown, BannerStrength.Length);
                BannerStrength = grown;
            }
        }

        static int[] BuildSortingPriorityBossSpawns()
        {
            var set = new int[InitialSlots];
            Array.Fill(set, -1);
            (int Id, int Priority)[] pairs =
            {
                (43, 1), (560, 2), (70, 3), (1331, 3), (361, 4), (5120, 5), (1133, 5),
                (4988, 6), (5334, 7), (544, 8), (556, 9), (557, 10), (2495, 11),
                (2673, 12), (602, 13), (1844, 14), (1958, 15), (1293, 16), (2767, 17),
                (4271, 18), (3601, 19), (1291, 20), (109, 21), (29, 22), (50, 23),
                (3199, 24), (3124, 25), (5437, 26), (5358, 27), (5359, 28),
                (5360, 29), (5361, 30), (4263, 31), (4819, 32),
            };
            foreach (var (id, priority) in pairs)
                if ((uint)id < (uint)set.Length) set[id] = priority;
            return set;
        }
    }
}
