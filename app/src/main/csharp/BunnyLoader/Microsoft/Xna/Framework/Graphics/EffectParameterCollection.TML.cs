using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectParameterCollection.get_Item(string)</c>. O ProxyGen gera este
/// tipo (esta em types.txt), mas para indexadores ele so emite os ganchos
/// (<c>event hook_get_Item_string</c>), nunca o acessador de instancia que o IL
/// do mod chama. O Luminance faz
/// <c>effect.Parameters["mainColor"].SetValue(...)</c>, e o parametro pode nao
/// existir no "shader" — por isso o indice cria e memoriza um parametro vazio
/// em vez de devolver null (o SetValue seguinte estouraria NRE).
///
/// Tudo gerenciado: nenhum Effect real e criado neste port.
/// </summary>
public sealed partial class EffectParameterCollection
{
    readonly Dictionary<string, EffectParameter> byName = new(StringComparer.Ordinal);

    public EffectParameter this[string name]
    {
        get
        {
            string key = name ?? string.Empty;
            lock (byName)
            {
                if (!byName.TryGetValue(key, out EffectParameter? parameter))
                {
                    parameter = new EffectParameter(key);
                    byName[key] = parameter;
                }
                return parameter;
            }
        }
    }
}
