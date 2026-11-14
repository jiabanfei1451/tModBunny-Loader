using System.Collections.Generic;

namespace Terraria.GameContent.ItemDropRules;

public interface IItemDropRule
{
    /// <summary>
    /// No tML este membro tem implementação padrão: quem não encadeia nada
    /// (as regras do Fargowiltas) simplesmente não o declara. Sem o corpo
    /// padrão o Mono resolvia a chamada para o slot abstrato da interface e
    /// estourava <c>MissingMethodException: sem corpo executavel</c> em
    /// <c>FargoGlobalItem.ModifyItemLoot</c>.
    /// </summary>
    List<IItemDropRuleChainAttempt> ChainedRules => new List<IItemDropRuleChainAttempt>();

    bool CanDrop(DropAttemptInfo info);

    ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info);

    void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo);
}
