using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using BunnyLoader.Interop;
using BunnyLoader.Screen;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.ModLoader;

namespace BunnyLoader;

/// <summary>
/// <c>Mod.Assets</c> do tML e <see cref="AssetRepository"/>; este e o
/// repositorio concreto do shim, que resolve os assets no disco do mod.
/// Ver <see cref="AssetRepository"/> para o porque de derivar (assinatura de
/// <c>Mod::get_Assets</c> no IL do mod).
/// </summary>
public sealed class ModAssets : AssetRepository
{
    static readonly string[] Extensions = { ".png", ".json" };

    readonly Mod mod;

    readonly Dictionary<string, object> requested = new();

    readonly Dictionary<(string Name, string Extension), object> byName = new();

    readonly ConcurrentDictionary<string, string?> located = new(StringComparer.Ordinal);

    internal ModAssets(Mod mod) => this.mod = mod;

    // v42: quantas linhas de entrada do Request<T> ainda cabem (contador
    // proprio: o de baixo e' compartilhado com textura e some antes).
    static int entryProbeLeft = 16;

    // v36: quantas linhas de log de Request ainda cabem. O LoadShaders do
    // Luminance pode pedir centenas de assets; queremos so' as primeiras.
    // v41: este contador e' COMPARTILHADO por todos os mods, entao o orcamento
    // de 40 era gasto por textura dos outros mods antes do Luminance e o probe
    // "nao apareceu" foi lido como "Request nao foi chamado" — leitura errada.
    // O caminho de shader agora tem contador proprio (effectLogLeft).
    static int requestProbeLeft = 40;

    // v46: contador PROPRIO desta linha de diagnostico. O v45 gastava o
    // orcamento compartilhado com as texturas do CalamityModMusic (16 linhas
    // todas "Request<Texture2D>") e a ausencia de "Request<Effect>" no log foi
    // lida como "o override nao foi chamado" — o que pode ser so' contador
    // esgotado. Aqui a primeira linha e' incondicional e o tipo vai completo
    // (nome + assembly), porque a suspeita e' o `Effect` do mod ser outro tipo
    // (Assembly-CSharp) e nao o nosso shim.
    static int dispatchProbeLeft = 40;

    static readonly HashSet<string> dispatchSeen = new(StringComparer.Ordinal);

    public override Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad)
        where T : class
    {
        DispatchProbe<T>(assetName);
        // v42: primeira linha, antes de qualquer desvio. Se o Luminance morrer
        // em NullReferenceException SEM esta linha, o NRE acontece antes do nosso
        // override (calculo de assetName ou despacho da chamada virtual) — nao
        // dentro dele. A v41 provou o contrario do que eu supunha: nem o "[fx]
        // effect 1/6" nem o "[fx] asset de efeito" apareceram.
        if (System.Threading.Interlocked.Decrement(ref entryProbeLeft) >= 0)
            Log.Info($"[fx] Request<{typeof(T).Name}>({assetName}) mod={mod.Name} entrou");
        // v46: o caminho de shader nao pode derrubar o PostSetupContent do mod.
        // Se este override estourar (identidade de tipo, instanciacao de
        // `Asset<T>`, arquivo ausente), devolve stub e segue — antes a excecao
        // subia e o mod ficava com o registro de shaders vazio.
        if (AssetKinds.IsEffectType(typeof(T)))
        {
            try { return EffectAsset<T>(assetName, mode); }
            catch (Exception e)
            {
                Log.Error($"[fx] Request<{typeof(T).Name}>({assetName}) mod={mod.Name}: {e.GetType().Name} fora do EffectAsset: {e.Message}");
                return new Asset<T>($"{mod.Name}/{assetName}", null);
            }
        }
        // v46: o override nao pode derrubar o PostSetupContent do mod. Antes
        // `Asset<T>.CheckSupported()` e o "asset not found" subiam crus; o
        // Luminance morria no primeiro asset que nao existisse e ficava sem
        // registro de shaders (o NRE dele nao vem daqui, mas o silencio sim).
        try
        {
            Asset<T>.CheckSupported();
            string extension = Extension<T>();
            lock (requested)
            {
                if (byName.TryGetValue((assetName, extension), out object? seen)) return ((Asset<T>)seen).OnRequest(mode, false);
            }
            string relative = Relative(assetName);
            string key = relative + extension;
            Asset<T> result;
            bool created = false;
            lock (requested)
            {
                if (!requested.TryGetValue(key, out object? have))
                {
                    string? file = Find(relative, extension);
                    if (file == null)
                    {
                        Log.Warn($"[fx] asset ausente: {mod.Name}/{relative}{extension} (pedido '{assetName}')");
                        return new Asset<T>($"{mod.Name}/{relative}", null);
                    }
                    have = new Asset<T>($"{mod.Name}/{relative}", file);
                    requested[key] = have;
                    created = true;
                }
                byName[(assetName, extension)] = have;
                result = (Asset<T>)have;
            }
            return result.OnRequest(mode, created);
        }
        catch (Exception e)
        {
            Log.Error($"[fx] Request<{typeof(T).Name}>({assetName}) mod={mod.Name}: {e.GetType().Name}: {e.Message}");
            return new Asset<T>($"{mod.Name}/{assetName}", null);
        }
    }

    /// <summary>
    /// v46: uma linha por par (T, asset) com o tipo COMPLETO. Responde a pergunta
    /// que o v45 deixou sem resposta: este override chega a ser chamado para o
    /// <c>Effect</c> do mod, e o <c>typeof(T)</c> que chega e' o nosso shim ou o
    /// tipo do Assembly-CSharp?
    /// </summary>
    void DispatchProbe<T>(string assetName) where T : class
    {
        if (dispatchProbeLeft <= 0) return;
        // v47: textura nao interessa e consome o orcamento (no v46 as 40 linhas
        // foram todas do CalamityModMusic, e o Luminance ficou sem sonda de novo).
        // So' tipos "de verdade": Effect, fonte, o que aparecer de novo.
        if (typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D)) return;
        try
        {
            string key = typeof(T).FullName + "|" + assetName;
            lock (dispatchSeen)
            {
                if (!dispatchSeen.Add(key)) return;
                if (dispatchSeen.Count > 40) return;
            }
            if (System.Threading.Interlocked.Decrement(ref dispatchProbeLeft) < 0) return;
            Type t = typeof(T);
            Log.Info($"[fx] req T={t.FullName} asm={t.Assembly.GetName().Name} " +
                     $"effect={AssetKinds.IsEffectType(t)} mod={mod.Name} nome={assetName}");
        }
        catch { }
    }

    /// <summary>
    /// v48: forca, no NOSSO assembly, a instanciacao concreta de
    /// <c>Asset&lt;Effect&gt;</c> e de <c>Request&lt;Effect&gt;</c>.
    ///
    /// O v46/v47 provaram que o NRE do Luminance acontece ENTRE o
    /// <c>get_Assets()</c> (que loga) e o <c>Request&lt;Effect&gt;</c> (nenhuma
    /// linha "[fx] req" sai): e' o <c>callvirt</c> de um metodo virtual
    /// generico sem slot — textura funciona porque <c>Asset&lt;Texture2D&gt;</c>
    /// e' usado em centenas de pontos do shim, <c>Asset&lt;Effect&gt;</c> em
    /// nenhum (só aparece dentro de corpos genericos). Uma chamada CONCRETA
    /// como esta faz o compilador emitir a instanciacao certa.
    /// </summary>
    internal static void PrewarmEffect()
    {
        try
        {
            Mod? first = Host.Mods.Length > 0 ? Host.Mods[0] : null;
            if (first == null)
            {
                Log.Info("[fx] prewarm effect: nenhum mod carregado");
                return;
            }
            Asset<Effect> asset = first.Assets.Request<Effect>("__prewarm", AssetRequestMode.DoNotLoad);
            Effect? value = asset.Value;
            Log.Info($"[fx] prewarm effect ok (valor nulo={value == null})");
        }
        catch (Exception e)
        {
            Log.Error($"[fx] prewarm effect falhou: {e.GetType().Name}: {e.Message}");
        }
    }

    // v41: quantas linhas o caminho de shader ainda escreve.
    static int effectLogLeft = 24;

    /// <summary>
    /// Caminho do <c>Asset&lt;Effect&gt;</code>: nao ha .xnb para carregar
    /// neste port (sem runtime de HLSL), o valor e' um stub gerenciado que aceita
    /// parametros. Cada passo e' logado (so' as primeiras chamadas) porque o
    /// Luminance morria no meio daqui com NullReferenceException e sem rastro.
    /// </summary>
    Asset<T> EffectAsset<T>(string assetName, AssetRequestMode mode) where T : class
    {
        int step = 0;
        bool log = System.Threading.Interlocked.Decrement(ref effectLogLeft) >= 0;
        try
        {
            step = 1;
            if (log) Log.Info($"[fx] effect 1/6 nome={assetName}");
            Asset<T>.CheckSupported();
            step = 2;
            string extension = Extension<T>();
            step = 3;
            lock (requested)
            {
                if (byName.TryGetValue((assetName, extension), out object? seen))
                {
                    if (log) Log.Info($"[fx] effect 3/6 cache {mod.Name}/{assetName}");
                    return ((Asset<T>)seen).OnRequest(mode, false);
                }
            }
            step = 4;
            string relative = Relative(assetName);
            string key = relative + extension;
            FxcProbe(relative, log);
            step = 5;
            Asset<T> result;
            bool created = false;
            lock (requested)
            {
                if (!requested.TryGetValue(key, out object? have))
                {
                    // v35: uma linha por efeito pedido. E' o marco que prova que
                    // o LoadShaders do Luminance chegou ate aqui.
                    Log.Info($"[fx] asset de efeito: {mod.Name}/{relative}");
                    have = new Asset<T>($"{mod.Name}/{relative}", null);
                    requested[key] = have;
                    created = true;
                }
                byName[(assetName, extension)] = have;
                result = (Asset<T>)have;
            }
            step = 6;
            if (log) Log.Info($"[fx] effect 6/6 ok {mod.Name}/{relative}");
            return result.OnRequest(mode, created);
        }
        catch (Exception e)
        {
            Log.Error($"[fx] Request<{typeof(T).Name}>({assetName}) mod={mod.Name} estourou no passo {step}: {e}");
            return new Asset<T>($"{mod.Name}/{assetName}", null);
        }
    }

    /// <summary>
    /// Upstream 0.5.13 (MojoShader): se o mod trouxe um <c>.fxc</c> compilado
    /// para este efeito, traduz agora e loga passagens/GLSL. E' a prova, em
    /// aparelho, de que o tradutor nativo (graphics/FxcEffect.cpp) esta vivo.
    /// O <c>Asset&lt;Effect&gt;</c> continua saindo como stub desta engine --
    /// desenhar com o GLSL traduzido e' a etapa seguinte.
    /// </summary>
    void FxcProbe(string relative, bool log)
    {
        try
        {
            string? path = Find(relative, ".fxc");
            if (path == null) return;
            FxcShader.Shader? shader = FxcShader.LoadFile(path, $"{mod.Name}/{relative}", log);
            if (shader != null) FxcShader.Free(shader);
        }
        catch (Exception e)
        {
            Log.Warn($"[fxc] {mod.Name}/{relative}: sonda estourou: {e.GetType().Name} {e.Message}");
        }
    }

    public override bool HasAsset<T>(string assetName) where T : class =>        AssetKinds.IsEffectType(typeof(T)) ||
        ((typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.Texture2D) || AssetKinds.IsFontType(typeof(T))) &&
         Find(Relative(assetName), Extension<T>()) != null);

    static string Extension<T>() where T : class =>
        typeof(T) == typeof(ReLogic.Graphics.DynamicSpriteFont) ? ".ttf" :
        typeof(T) == typeof(Microsoft.Xna.Framework.Graphics.SpriteFont) ? ".json" :
        AssetKinds.IsEffectType(typeof(T)) ? ".xnb" : ".png";

    static string Relative(string assetName)
    {
        string name = assetName.Replace('\\', '/').TrimStart('/');
        foreach (string extension in Extensions)
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return name[..^extension.Length];
        return name;
    }

    string? Find(string relative, string extension)
    {
        string key = relative + extension;
        if (located.TryGetValue(key, out string? known)) return known;
        string? path = mod.ResolveFile(key);
        string? found = path != null && File.Exists(path) ? path : null;

        // No tModLoader 1.4.5 "NomeDoMod/Item" e "Item" apontam para o mesmo
        // recurso (o nome do mod e' so um prefixo logico do asset). Um .tmod
        // pede quase sempre a forma completa; o arquivo extraido fica na raiz
        // da pasta do mod. Tentar as duas formas evita o "asset not found".
        if (found == null)
        {
            string prefix = mod.Name + "/";
            if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = mod.ResolveFile(relative.Substring(prefix.Length) + extension);
                found = path != null && File.Exists(path) ? path : null;
            }
        }

        located[key] = found;
        return found;
    }
}
