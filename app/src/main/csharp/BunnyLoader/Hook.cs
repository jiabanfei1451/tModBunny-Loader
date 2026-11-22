using System;
using BunnyLoader.Interop;

namespace BunnyLoader;

public sealed unsafe class Hook
{
    readonly IntPtr* original;

    Hook(IntPtr* original) => this.original = original;

    public IntPtr Original => *original;

    public IntPtr* OriginalSlot => original;

    /// <summary>
    /// Instalação desligada (harness de PC).
    ///
    /// O `InstallHook` do aparelho patcheia o código nativo do il2cpp; no
    /// Windows não existe jogo nenhum para patchear. Mais grave: o
    /// HookGen gera os corpos de gancho como `[UnmanagedCallersOnly]`, e o CLR
    /// do PC derruba o processo (FailFast "attempted to call a
    /// UnmanagedCallersOnly method from managed code") assim que um desses
    /// corpos é compilado. O Mono do aparelho aceita.
    ///
    /// Com <c>BL_NO_HOOKS=1</c> nenhum gancho é instalado e o resto do
    /// fluxo (IL, Load, SetupContent, AddRecipes, SetDefaults) roda inteiro —
    /// é o que o harness precisa para testar a carga sem aparelho.
    /// </summary>
    static readonly bool skip = Environment.GetEnvironmentVariable("BL_NO_HOOKS") == "1";

    internal static bool Skipped => skip;

    public static Hook Install(IntPtr method, IntPtr replacement)
    {
        if (skip) return new Hook(DeadSlot());

        // Quando o pedido vem de dentro de um HookChain.Add o HookGates sabe de
        // que tipo e' o gancho e instala pelo portao nativo (o gate so chama o
        // shim quando existe mod inscrito). Fora disso instala direto.
        Type? gated = Hooks.HookGates.Installing;
        Hooks.HookGates.Installing = null;
        IntPtr* slot = gated != null
            ? Hooks.HookGates.Install(gated, method, replacement)
            : Host.Native()->InstallHook(method, replacement);

        if (slot == null) throw new InvalidOperationException("hook install failed");
        return new Hook(slot);
    }

    internal static Hook InstallGated(GameClass gameClass, string signature, IntPtr replacement, GateSpec spec, out int id)
    {
        MethodRef method = default;
        GameInterop.Method(ref method, gameClass, signature, LayoutUnchecked(signature));
        int gate;

        // O ramo de gates veio do upstream e depende de InstallGateHook, que o
        // nosso libbunny.so nao exporta. Sem o simbolo o hook sai pelo caminho
        // classico e o spec de gate e' ignorado.
        if (true)
        {
            IntPtr* classic = Host.Native()->InstallHook(method.Info, replacement);
            if (classic == null) throw new InvalidOperationException("hook install failed");
            id = -1;
            return new Hook(classic);
        }

        IntPtr* slot = Host.Native()->InstallHook(method.Info, replacement);
        id = -1;
        if (slot == null) throw new InvalidOperationException("hook install failed");
        id = gate;
        return new Hook(slot);
    }

    static IntPtr* deadSlot;

    static IntPtr* DeadSlot()
    {
        if (deadSlot == null)
        {
            deadSlot = (IntPtr*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)sizeof(IntPtr));
            *deadSlot = IntPtr.Zero;
        }
        return deadSlot;
    }

    public static Hook Install(string ns, string cls, string signature, IntPtr replacement) =>
        Install(new GameClass(ns, cls), signature, replacement);

    public static Hook Install(GameClass gameClass, string signature, IntPtr replacement)
    {
        MethodRef method = default;
        // Marca o passo antes de resolver: se o processo morrer nesta resolucao,
        // esta e a ultima linha util do log (o "hook instalado" ainda nao existe).
        Log.Info($"hook: procurando {gameClass.FullName} :: {signature}");
        GameInterop.Method(ref method, gameClass, signature, LayoutUnchecked(signature));
        Hook hook = Install(method.Info, replacement);
        Log.Info($"hook: instalado {gameClass.FullName} :: {signature} (original=0x{hook.Original:X})");
        return hook;
    }

    static int[] LayoutUnchecked(string signature)
    {
        int open = signature.IndexOf('('), close = signature.LastIndexOf(')');
        string inner = signature.Substring(open + 1, close - open - 1).Trim();
        int count = inner.Length == 0 ? 0 : 1;
        int depth = 0;
        foreach (char c in inner)
        {
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        int[] layout = new int[count + 1];
        Array.Fill(layout, -1);
        return layout;
    }
}
