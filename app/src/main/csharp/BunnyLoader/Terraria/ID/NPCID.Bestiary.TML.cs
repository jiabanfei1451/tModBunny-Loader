using System;
using System.Collections.Generic;
using BunnyLoader.Interop;
using Terraria.ModLoader;

namespace Terraria.ID;

public sealed partial class NPCID
{
    public static partial class Sets
    {
        public static BestiaryDrawOffsets NPCBestiaryDrawOffset { get; } = new();

        public partial struct NPCBestiaryDrawModifiers
        {
            public NPCBestiaryDrawModifiers()
            {
                Scale = 1f;
                PortraitScale = 1f;
            }
        }
    }

    /// <summary>
    /// O tML expõe <c>NPCID.Sets.NPCBestiaryDrawOffset</c> como
    /// <c>Dictionary&lt;int, NPCBestiaryDrawModifiers&gt;</c>, e o IL do mod
    /// chama <c>Dictionary.Add</c>/<c>set_Item</c> DIRETO nesse tipo.
    /// Enquanto isto era um wrapper próprio, o <c>callvirt</c> caía no
    /// Dictionary real com um receptor de outra classe: o JIT lia lixo no
    /// lugar de <c>_buckets</c> e o <c>Add</c> estourava
    /// <c>IndexOutOfRange</c> — era o que derrubava
    /// <c>Abominationn/Deviantt/LumberJack/Mutant/Squirrel/SuperDummy</c>.
    /// Herdando de Dictionary o tipo bate e as chamadas funcionam; o que
    /// ficou na tabela gerenciada é copiado para a nativa em
    /// <see cref="Flush"/> (o bestiary desenha lendo a nativa).
    /// </summary>
    public sealed class BestiaryDrawOffsets : Dictionary<int, Sets.NPCBestiaryDrawModifiers>
    {
        readonly Dictionary<int, string> modTextures = new();

        static GameDictionary Table => new(Sets.NPCBestiaryDrawOffset_);

        internal string? ModTexture(int npcNetId)
        {
            lock (modTextures)
                if (modTextures.TryGetValue(npcNetId, out string? texture)) return texture;
            return TryGetValue(npcNetId, out Sets.NPCBestiaryDrawModifiers value) ? value.CustomTexturePath : null;
        }

        /// <summary>
        /// Copia os modificadores para o dicionário nativo. Os caminhos de
        /// textura customizada são guardados à parte: o slot nativo é um
        /// ponteiro de string do il2cpp e um <c>string</c> gerenciado ali
        /// derrubaria o desenho.
        /// </summary>
        internal void Flush()
        {
            KeyValuePair<int, Sets.NPCBestiaryDrawModifiers>[] entries;
            try { entries = new List<KeyValuePair<int, Sets.NPCBestiaryDrawModifiers>>(this).ToArray(); }
            catch (InvalidOperationException) { return; }
            if (entries.Length == 0) return;
            GameDictionary table;
            try { table = Table; }
            catch (Exception) { return; }
            if (table.IsNull) return;
            foreach (KeyValuePair<int, Sets.NPCBestiaryDrawModifiers> pair in entries)
            {
                Sets.NPCBestiaryDrawModifiers value = pair.Value;
                string? texture = value.CustomTexturePath;
                lock (modTextures)
                {
                    if (texture != null && !texture.StartsWith("Images/", StringComparison.Ordinal))
                    {
                        modTextures[pair.Key] = texture;
                        value.CustomTexturePath = default;
                    }
                    else modTextures.Remove(pair.Key);
                }
                try { table.Set(pair.Key, value); }
                catch (Exception e) { BunnyLoader.Log.Warn($"NPCBestiaryDrawOffset[{pair.Key}]: {e.Message}"); }
            }
        }
    }
}
