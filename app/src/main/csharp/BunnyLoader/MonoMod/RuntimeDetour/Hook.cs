using System;
using System.Reflection;

namespace MonoMod.RuntimeDetour;

/// <summary>
/// <c>Hook</c> do MonoMod: troca a implementação de um método por um delegate.
///
/// Diferente do <see cref="ILHook"/>, aqui o substituto é código gerenciado com
/// assinatura livre — o MonoMod do PC instala um trampolim nativo que preserva
/// os argumentos. O il2cpp daqui só aceita gancho com ABI de ponteiro puro
/// (<c>[UnmanagedCallersOnly]</c>, que é o que o HookGen gera), então um
/// delegate de mod não pode ser plugado direto: os tipos gerenciados
/// (<c>ref</c>, structs por valor, <c>orig</c>) não sobreviveriam à conversão.
///
/// Por isso: registra no ledger e segue. O mod continua carregando — que é o
/// ponto — e o ledger diz exatamente qual método ele queria substituir, que é
/// o dado necessário para escrever a ponte de ABI depois.
/// </summary>
public class Hook : IDisposable
{
    public MethodBase Method { get; }

    public Delegate Target { get; }

    public bool IsApplied { get; private set; }

    readonly string source;

    public Hook(MethodBase from, Delegate to)
    {
        Method = from;
        Target = to;
        source = Caller();
    }

    public void Apply()
    {
        if (IsApplied) return;
        IsApplied = true;
        BunnyLoader.IlPatchLedger.Record(source, "Hook", Method, null,
            $"detour para {Target.Method.DeclaringType?.Name}.{Target.Method.Name}; sem ponte de ABI", null, false);
    }

    public void Undo() => IsApplied = false;

    public void Dispose() => Undo();

    static string Caller()
    {
        try { return Assembly.GetCallingAssembly().GetName().Name ?? "?"; }
        catch { return "?"; }
    }
}
