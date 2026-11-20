using System;

namespace BunnyLoader.Interop;

/// <summary>
/// Rastreador de execucao injetado pelo IlPatcher nos metodos de um mod.
///
/// Existe por causa de um SIGSEGV dentro do proprio Mono (<c>libmonosgen-2.0.so
/// +0xC8758</c>, <c>si_addr = 0</c>): o JIT morre ao resolver uma referencia, a
/// pilha chega corrompida e o log nao diz QUAL chamada explodiu. Com uma linha
/// antes de cada <c>call/callvirt/newobj</c>, a ultima linha impressa antes do
/// crash E o culpado.
///
/// Custo: cada linha vira <c>ldstr</c> + <c>call Emit</c> no corpo do metodo,
/// entao o rastreamento e' limitado a uns poucos tipos (ver <see cref="Entries"/>)
/// e a um teto de linhas por processo.
/// </summary>
public static class IlTrace
{
    const int Budget = 20000;

    /// <summary>
    /// Alvos padrao: os unicos SetStaticDefaults do FargowiltasSouls que escrevem
    /// <c>NPCBestiaryDrawModifiers.CustomTexturePath</c>, mais os quatro campeoes
    /// que a carga ja passou sem estourar. Em v112 o alvo era
    /// <c>FargoExtensionMethods</c>, um metodo quente chamado milhares de vezes
    /// por segundo na inicializacao dos itens: ele consumiu as 4000 linhas em 1 ms
    /// e o crash (14:10:49) ficou sem nenhum rastro. Metodos pequenos e frios
    /// apenas.
    /// </summary>
    static readonly string[] Defaults =
    {
        "FargowiltasSouls.Content.Bosses.Champions.Earth.EarthChampion",
        "FargowiltasSouls.Content.Bosses.Champions.Nature.NatureChampion",
        "FargowiltasSouls.Content.Bosses.Champions.Life.LifeChampion",
        "FargowiltasSouls.Content.Bosses.Champions.Cosmos.CosmosChampion",
        "FargowiltasSouls.Content.Bosses.Champions.Terra.TerraChampion",
        "FargowiltasSouls.Content.Bosses.Champions.Spirit.SpiritChampion",
        "FargowiltasSouls.Content.Bosses.TrojanSquirrel.TrojanSquirrel",
        "FargowiltasSouls.Content.Bosses.Lifelight.LifeChallenger",
    };

    static string[]? entries;
    static string? fingerprint;
    static int emitted;
    static bool warned;
    static bool announced;

    /// <summary>
    /// Alvos no formato <c>Namespace.Tipo</c> (todos os metodos) ou
    /// <c>Namespace.Tipo::Metodo</c> (um so). <c>BL_TRACE=none</c> desliga;
    /// <c>BL_TRACE=A;B</c> troca a lista.
    /// </summary>
    internal static string[] Entries
    {
        get { Build(); return entries!; }
    }

    internal static bool Enabled
    {
        get { Build(); return entries!.Length > 0; }
    }

    /// <summary>
    /// Entra na chave do cache do IlPatcher: ligar ou desligar o rastreamento
    /// muda o IL gravado, entao a entrada antiga (sem as chamadas de log) nao
    /// pode ser reaproveitada.
    /// </summary>
    internal static string Fingerprint
    {
        get { Build(); return fingerprint!; }
    }

    internal static void Announce()
    {
        if (!Enabled || announced) return;
        announced = true;
        Log.Info("[trace] ativo em: " + string.Join(", ", entries!)
            + " (limite de " + Budget + " linhas; BL_TRACE=none desliga)");
    }

    internal static bool Match(string typeName, string methodName)
    {
        string[] list = Entries;
        if (list.Length == 0 || string.IsNullOrEmpty(typeName)) return false;
        foreach (string entry in list)
        {
            int split = entry.IndexOf("::", StringComparison.Ordinal);
            string type = split < 0 ? entry : entry.Substring(0, split);
            bool typeOk = string.Equals(typeName, type, StringComparison.Ordinal)
                || (typeName.Length > type.Length
                    && typeName.StartsWith(type, StringComparison.Ordinal)
                    && (typeName[type.Length] == '/' || typeName[type.Length] == '+'));
            if (!typeOk) continue;
            if (split < 0) return true;
            if (string.Equals(methodName, entry.Substring(split + 2), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>Chamado pelo IL injetado. Nunca lanca: dentro do metodo do mod.</summary>
    public static void Emit(string text)
    {
        try
        {
            if (emitted < Budget)
            {
                emitted++;
                Log.Info(text);
                return;
            }
            if (warned) return;
            warned = true;
            Log.Warn("[trace] limite de " + Budget + " linhas atingido; rastreamento silenciado ate a proxima carga");
        }
        catch { }
    }

    static void Build()
    {
        if (entries != null) return;
        var found = new System.Collections.Generic.List<string>();
        string? raw = null;
        try { raw = Environment.GetEnvironmentVariable("BL_TRACE"); } catch { }
        if (string.IsNullOrWhiteSpace(raw)) found.AddRange(Defaults);
        else
        {
            string text = raw!.Trim();
            if (!(string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "off", StringComparison.OrdinalIgnoreCase)
                || text == "0"))
            {
                foreach (string part in text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string item = part.Trim();
                    if (item.Length > 0) found.Add(item);
                }
            }
        }
        entries = found.ToArray();
        ulong hash = 14695981039346656037UL;
        foreach (string item in entries)
            foreach (char ch in item) { hash ^= (byte)ch; hash *= 1099511628211UL; }
        fingerprint = "t" + hash.ToString("x8");
    }
}
