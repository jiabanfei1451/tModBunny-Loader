using System;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using CecilBody = Mono.Cecil.Cil.MethodBody;

namespace MonoMod.RuntimeDetour;

/// <summary>
/// <c>ILHook</c> do MonoMod: "rode este manipulador sobre o IL do alvo".
///
/// No Android o alvo é código nativo do il2cpp, então não existe IL para
/// editar. O que esta classe garante é o CONTRATO: construtor, <c>Apply</c>,
/// <c>Undo</c> e <c>Dispose</c> nunca lançam. Era exatamente aí que o
/// <c>ScreenShaderFixer</c> do Luminance morria (construtor estático chamando
/// <c>Apply</c> com tipo ausente), levando o mod inteiro e, com ele, o pacote
/// de almas do FargoSouls.
///
/// O manipulador roda de verdade contra um esqueleto Cecil; o resultado vai
/// para <see cref="BunnyLoader.IlPatchLedger"/>. Quando <see cref="ILContext.Applier"/>
/// existir, o corpo editado passa a ser aplicado sem mudar nada aqui.
/// </summary>
public class ILHook : IDisposable
{
    public MethodBase Method { get; }

    public ILContext.Manipulator Manipulator { get; }

    public bool IsApplied { get; private set; }

    /// <summary>Assembly que pediu o hook (o mod); vira o nome no ledger.</summary>
    readonly string source;

    public ILHook(MethodBase from, ILContext.Manipulator manipulator)
    {
        Method = from;
        Manipulator = manipulator;
        source = Caller();
    }

    public void Apply()
    {
        if (IsApplied) return;
        IsApplied = true;

        ILContext? context = null;
        string note = "sem corpo";
        Exception? error = null;
        bool applied = false;

        try
        {
            context = ILContext.Create(Method, out note);
            try
            {
                context.Invoke(Manipulator);
            }
            catch (Exception e)
            {
                // O manipulador do mod estourou (padrão não encontrado, índice
                // fora, etc.). É o caso COMUM hoje: sem o IL do jogo os
                // GotoNext não casam. Não propaga.
                error = e;
            }

            if (error == null)
            {
                try
                {
                    Func<MethodBase, CecilBody, bool>? applier = ILContext.Applier;
                    applied = applier != null && applier(Method, context.Body);
                }
                catch (Exception e)
                {
                    error = e;
                }
            }
        }
        catch (Exception e)
        {
            error = e;
        }

        BunnyLoader.IlPatchLedger.Record(source, "ILHook", Method, context, note, error, applied);
    }

    public void Undo() => IsApplied = false;

    public void Dispose() => Undo();

    static string Caller()
    {
        try
        {
            Assembly caller = Assembly.GetCallingAssembly();
            return caller.GetName().Name ?? "?";
        }
        catch { return "?"; }
    }
}
