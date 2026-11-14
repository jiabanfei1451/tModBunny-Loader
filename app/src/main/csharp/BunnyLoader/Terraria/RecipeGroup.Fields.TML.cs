using System.Collections.Generic;

namespace Terraria;

/// <summary>
/// <c>RecipeGroup.IconicItemId</c> é do tML (o jogo não tem esse campo — o
/// dump só traz <c>ValidItems</c>/<c>Items</c>/<c>DecraftItemId</c>).
///
/// O tML usa o valor como ícone do grupo na UI de receitas. Como os proxies
/// são recriados a cada acesso, o valor é guardado por endereço do grupo.
/// </summary>
public sealed partial class RecipeGroup
{
    static readonly Dictionary<nint, int> Iconic = new();

    public int IconicItemId
    {
        get => Iconic.TryGetValue(Pointer, out int id) ? id : 0;
        set => Iconic[Pointer] = value;
    }

    public int GetIconicItemId() => IconicItemId;
}
