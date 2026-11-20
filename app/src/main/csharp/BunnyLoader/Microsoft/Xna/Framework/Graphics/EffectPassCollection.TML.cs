using System;
using System.Collections.Generic;

namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// FNA: <c>EffectPassCollection.get_Item(string)</c> (Luminance) e
/// <c>get_Item(int)</c> (FargowiltasSouls). Mesmo caso do
/// <see cref="EffectParameterCollection"/>: o ProxyGen so gera os ganchos.
/// Um passo inexistente e criado sob demanda para o <c>Apply()</c> seguinte
/// poder ser um no-op em vez de NRE.
/// </summary>
public sealed partial class EffectPassCollection
{
    readonly Dictionary<string, EffectPass> byName = new(StringComparer.Ordinal);
    readonly List<EffectPass> byIndex = new();

    public EffectPass this[string name]
    {
        get
        {
            string key = name ?? string.Empty;
            lock (byName)
            {
                if (!byName.TryGetValue(key, out EffectPass? pass))
                {
                    pass = new EffectPass(0);
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
                while (byIndex.Count <= index) byIndex.Add(new EffectPass(0));
                return index < 0 ? new EffectPass(0) : byIndex[index];
            }
        }
    }
}
