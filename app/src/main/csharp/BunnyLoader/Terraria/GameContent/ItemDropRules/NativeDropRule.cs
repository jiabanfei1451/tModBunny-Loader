using System;
using System.Collections.Generic;
using BunnyLoader;
using BunnyLoader.Interop;
using Terraria.ModLoader.Core;
using N = BunnyLoader.Interop.DropRules;

namespace Terraria.GameContent.ItemDropRules;

public class NativeDropRule : IItemDropRule
{
    readonly GameHandle handle;
    readonly Type nativeType;
    List<IItemDropRuleChainAttempt>? chains;

    protected internal NativeDropRule(IntPtr pointer, Type nativeType)
    {
        handle = new GameHandle(pointer);
        this.nativeType = nativeType;
    }

    internal IntPtr Prototype => handle.Target;
    internal bool UsesNativeImplementation => GetType() == nativeType;
    internal List<IItemDropRuleChainAttempt>? EditedChains => chains;
    static bool ChainsUnavailable;

    public List<IItemDropRuleChainAttempt> ChainedRules
    {
        get
        {
            if (chains == null)
            {
                // `IItemDropRule.ChainedRules` é uma propriedade com implementação
                // padrão da INTERFACE no tML. No jogo il2cpp não existe corpo
                // nativo executável para `get_ChainedRules` — o GameInterop
                // devolvia `sem corpo executavel` e a chamada inteira de
                // `ModifyNPCLoot` (FargowiltasSouls / Fargowiltas) era abortada.
                // Um rule recém-criado nunca tem cadeias, então lista vazia é
                // o estado correto; se o jogo um dia expuser o getter, ainda
                // tentamos ler primeiro.
                try { chains = DropRuleInterop.ReadChains(new N.IItemDropRule(Prototype).ChainedRules); }
                catch (Exception e)
                {
                    // ja' documentado acima: no il2cpp este getter nao tem corpo.
                    // Reportar em toda regra enchia o log (25 mil linhas) sem
                    // informacao nova, entao avisa uma vez e segue com lista vazia.
                    if (!ChainsUnavailable)
                    {
                        ChainsUnavailable = true;
                        Log.Info($"NativeDropRule: 'IItemDropRule.ChainedRules' sem corpo nativo ({e.GetType().Name}); usando lista vazia");
                    }
                    chains = new List<IItemDropRuleChainAttempt>();
                }
                DropRuleInterop.Watch(this);
            }
            return chains;
        }
    }

    public virtual bool CanDrop(DropAttemptInfo info)
    {
        using var scope = new ItemLootHooks.AttemptScope(info);
        return new N.IItemDropRule(Prototype).CanDrop(DropRuleInterop.ToNative(info));
    }

    public virtual ItemDropAttemptResult TryDroppingItem(DropAttemptInfo info)
    {
        using var scope = new ItemLootHooks.AttemptScope(info);
        return DropRuleInterop.ToManaged(new N.IItemDropRule(Prototype).TryDroppingItem(DropRuleInterop.ToNative(info)));
    }

    public virtual void ReportDroprates(List<DropRateInfo> drops, DropRateInfoChainFeed ratesInfo)
    {
        DropRuleInterop.Sync(this, Prototype);
        DropRuleInterop.Report(Prototype, drops, ratesInfo);
    }
}
