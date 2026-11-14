using System;
using System.Collections.Generic;
using System.Threading;

namespace BunnyLoader.Hooks;

public sealed class HookChain<THook, TOrig> where THook : System.Delegate where TOrig : System.Delegate
{
    // Todas as cadeias desta assinatura, para o `Always` alcancar a que o
    // HookGen criou (ele guarda a instancia num campo privado de uma classe
    // aninhada; nao ha como pega-la de fora, mas o construtor passa por aqui).
    static readonly List<HookChain<THook, TOrig>> registry = new();
    static bool alwaysOn;

    readonly object gate = new();
    readonly List<THook> hooks = new();
    readonly Func<THook, TOrig, TOrig> wrap;
    readonly TOrig native;
    readonly Action install;
    bool installed;
    volatile bool forced;
    TOrig? chain;

    public HookChain(Func<THook, TOrig, TOrig> wrap, TOrig native, Action install)
    {
        this.wrap = wrap;
        this.native = native;
        this.install = install;
        lock (registry)
        {
            forced = alwaysOn;
            registry.Add(this);
        }
    }

    /// <summary>
    /// Abre SO esta cadeia mesmo com a porta fechada (<see cref="HookGate"/>).
    ///
    /// A porta existe porque durante a carga dos mods metade do estado do jogo
    /// ainda nao esta de pe: um gancho de mod disparando ali derruba a thread
    /// do jogo. Mas o painel de carregamento e justamente desenhado nessa
    /// janela — ele nao toca em nada de mod, so no pixel branco e na fonte do
    /// jogo. Sem esta excecao o `DoDraw` volta direto para o original e o
    /// painel nunca aparece, por mais que a inscricao esteja feita.
    /// </summary>
    internal static void Always(bool on)
    {
        lock (registry)
        {
            alwaysOn = on;
            foreach (HookChain<THook, TOrig> hook in registry) hook.forced = on;
        }
    }

    public TOrig? Chain => (forced || HookGate.Open) ? Volatile.Read(ref chain) : null;

    public int Count => hooks.Count;

    public void Add(THook hook)
    {
        lock (gate)
        {
            if (!installed)
            {
                // Com BL_NO_HOOKS (harness de PC) o `install` nao roda: ele e o
                // unico lugar que referencia o corpo [UnmanagedCallersOnly]
                // gerado pelo HookGen, e o CLR do PC derruba o processo ao
                // compilar esse corpo. No aparelho (Mono) nao ha esse problema.
                if (!BunnyLoader.Hook.Skipped) install();
                installed = true;
            }
            hooks.Add(hook);
            Rebuild();
        }
    }

    public void Remove(THook hook)
    {
        lock (gate)
        {
            if (hooks.Remove(hook)) Rebuild();
        }
    }

    void Rebuild()
    {
        if (hooks.Count == 0)
        {
            Volatile.Write(ref chain, null);
            return;
        }
        TOrig next = native;
        foreach (THook hook in hooks) next = wrap(hook, next);
        Volatile.Write(ref chain, next);
    }
}

internal static unsafe class HookGate
{
    static volatile bool open;

    [ThreadStatic] internal static bool Loading;

    internal static bool Open => open || Loading || (open = Host.Native()->IsCoreReady() != 0);
}

public static class HookErrors
{
    public static void Report(string method, Exception error) => Log.Error($"hook {method}: {error}");
}
