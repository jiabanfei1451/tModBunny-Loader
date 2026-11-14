namespace Terraria.Localization;

public sealed partial class LocalizedText
{
    public override string ToString() => Pointer == 0 ? "" : Value ?? "";

    // tML 1.4.4.9: `NetworkText ToNetworkText()` e
    // `NetworkText ToNetworkText(params object[] args)`. O dump do 1.4.5 só
    // tem a de zero argumentos (gerada pelo ProxyGen), então todo mod que
    // manda texto formatado para a rede (`BaseSummon.Shoot`,
    // `PinkSlimeCrown.UseItem`) ficava sem JIT. Sem servidor, o texto sai
    // resolvido localmente — é o suficiente.
    public NetworkText ToNetworkText(params object?[] args) => NetworkText.FromKey(Key ?? "", args);
}
