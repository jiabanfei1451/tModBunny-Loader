using System;
using System.Reflection;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Terraria;

/// <summary>
/// <c>Terraria.IL_Main</c> — o que o <b>ILHookGen</b> do tML gera no
/// <c>TerrariaHooks.dll</c>: para cada método do jogo existe um evento estático
/// de mesmo nome que recebe um <see cref="ILContext.Manipulator"/>.
///
/// Por que existe aqui: o Luminance chama
/// <c>Terraria.IL_Main::add_DoDraw(Manipulator)</c> em
/// <c>ScreenShaderFixer.Subscribe</c>. Sem o tipo, o construtor estático do
/// <c>ScreenShaderFixer</c> morre e o mod inteiro não carrega — era o
/// <c>VTable setup of type ... failed</c> que sobrou depois de resolver o
/// MonoMod. (Diagnóstico do <c>tools/modsandbox</c>: "Could not load type
/// 'Terraria.IL_Main' from assembly 'TerrariaHooks'".)
///
/// Só entram os eventos que algum mod assina. Um evento a mais aqui seria uma
/// promessa vazia; a falta dele é <c>MissingMethodException</c> com nome e
/// assinatura no log.
///
/// O corpo do evento vai para o mesmo caminho do <c>ILHook</c>: registra no
/// ledger (<c>&lt;TML&gt;/ilpatch</c>) e não aplica nada hoje. Quando houver
/// aplicador, basta trocar <see cref="Subscribe"/>.
/// </summary>
public static class IL_Main
{
    static ILHook? doDraw;

    /// <summary>Patch de IL em <c>Terraria.Main.DoDraw(GameTime)</c>.</summary>
    public static event ILContext.Manipulator DoDraw
    {
        add => Subscribe(ref doDraw, nameof(DoDraw), value);
        remove => Unsubscribe(ref doDraw);
    }

    static void Subscribe(ref ILHook? slot, string method, ILContext.Manipulator? manipulator)
    {
        // Nada aqui pode lançar: quem chama é o construtor estático do mod.
        if (manipulator == null) return;
        try
        {
            slot?.Undo();
            MethodInfo? target = FindOnMain(method);
            if (target == null)
            {
                // Sem alvo não há nem o que registrar; o log basta.
                BunnyLoader.Log.Warn($"IL_Main.{method}: método não existe no shim; patch ignorado");
                return;
            }
            slot = new ILHook(target, manipulator);
            slot.Apply();
        }
        catch (Exception)
        {
            // ILHook já engole o que importa e grava no ledger; isto é só a
            // rede para o caso de o reflexo em si falhar.
        }
    }

    static void Unsubscribe(ref ILHook? slot)
    {
        try { slot?.Undo(); }
        catch { /* idem */ }
        slot = null;
    }

    static MethodInfo? FindOnMain(string name)
    {
        foreach (MethodInfo candidate in typeof(Main).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                return candidate;
        return null;
    }
}
