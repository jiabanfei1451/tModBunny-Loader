using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectPassCollection.get_Item(string)</c> (Luminance) e
/// <c>get_Item(int)</c> (FargowiltasSouls). Mesmo caso do
/// <see cref="EffectParameterCollection"/>: o ProxyGen so gera os ganchos.
/// Um passo inexistente e criado sob demanda para o <c>Apply()</c> seguinte
/// poder ser um no-op em vez de NRE.
///
/// Todo passo criado aqui carrega o <c>Owner</c> (o <see cref="Effect"/>),
/// que e' o que o <c>Apply()</c> usa para achar o <c>.fx</code>, os
/// parametros e a tecnica.
/// </summary>
public sealed partial class EffectPassCollection
{
    readonly Dictionary<string, EffectPass> byName = new(StringComparer.Ordinal);
    readonly List<EffectPass> byIndex = new();

    /// <summary>Efeito dono desta colecao (nulo quando veio de um ponteiro do jogo).</summary>
    internal Effect? Owner;

    public EffectPass this[string name]
    {
        get
        {
            string key = name ?? string.Empty;
            lock (byName)
            {
                if (!byName.TryGetValue(key, out EffectPass? pass))
                {
                    pass = Create(key);
                    byName[key] = pass;
                    byIndex.Add(pass);
                }
                return pass;
            }
        }
    }

    public EffectPass this[int index]
    {
        get
        {
            lock (byName)
            {
                while (byIndex.Count <= index) byIndex.Add(Create(""));
                return index < 0 ? Create("") : byIndex[index];
            }
        }
    }

    /// <summary>Quantos passos ja' foram pedidos (para o bridge saber se ha algo).</summary>
    internal int ManagedCount
    {
        get
        {
            lock (byName) return byIndex.Count;
        }
    }

    EffectPass Create(string name)
    {
        var pass = new EffectPass(0);
        pass.Owner = Owner;
        pass.PassName = name;
        return pass;
    }
}
