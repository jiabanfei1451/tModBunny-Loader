using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MonoMod.Cil;

/// <summary>
/// Cursor de edição sobre <see cref="ILContext.Instrs"/>, com a semântica do
/// <c>MonoMod.Cil.ILCursor</c> de verdade: <c>Index</c> aponta para a instrução
/// que vem DEPOIS do cursor, e <c>Emit</c> insere nessa posição avançando.
///
/// Isso não é decoração: <c>GotoNext(MoveType.After, ...)</c> + <c>Emit</c> é o
/// idioma que Luminance/FargoSouls usam para inserir uma chamada depois de um
/// <c>ldsfld</c>. Se a semântica fosse "insere antes do casado", a instrução
/// emitida cairia no lugar errado assim que houver como aplicar os patches.
/// </summary>
public class ILCursor
{
    public readonly ILContext Context;

    int index;

    public ILCursor(ILContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        index = 0;
    }

    IList<Instruction> Instrs => Context.Body.Instructions;

    /// <summary>Posição do cursor: índice da PRÓXIMA instrução.</summary>
    public int Index
    {
        get => index;
        set => index = value < 0 ? 0 : value > Instrs.Count ? Instrs.Count : value;
    }

    public Instruction? Next => index >= 0 && index < Instrs.Count ? Instrs[index] : null;

    public Instruction? Prev => index > 0 && index <= Instrs.Count ? Instrs[index - 1] : null;

    public ILCursor Clone() => new(Context) { index = index };

    public ILCursor Goto(Instruction instruction)
    {
        int at = Instrs.IndexOf(instruction);
        if (at < 0) throw new InvalidOperationException("ILCursor.Goto: instrução não pertence a este corpo");
        index = at;
        return this;
    }

    public ILCursor Goto(int index)
    {
        Index = index;
        return this;
    }

    public ILCursor MoveBefore(Instruction instruction) => Goto(instruction);

    public ILCursor MoveAfter(Instruction instruction) => Goto(instruction).Goto(Index + 1);

    // ---------------------------------------------------------------- busca

    public bool TryGotoNext(MoveType moveType, params Func<Instruction, bool>[] predicates) =>
        Search(1, moveType, predicates);

    /// <summary>MonoMod real: sobrecarga sem <see cref="MoveType"/> (equivale a <c>Before</c>).</summary>
    public bool TryGotoNext(params Func<Instruction, bool>[] predicates) =>
        Search(1, MoveType.Before, predicates);

    public bool TryGotoPrev(MoveType moveType, params Func<Instruction, bool>[] predicates) =>
        Search(-1, moveType, predicates);

    /// <summary>MonoMod real: sobrecarga sem <see cref="MoveType"/> (equivale a <c>Before</c>).</summary>
    public bool TryGotoPrev(params Func<Instruction, bool>[] predicates) =>
        Search(-1, MoveType.Before, predicates);

    public ILCursor GotoNext(MoveType moveType, params Func<Instruction, bool>[] predicates)
    {
        if (!TryGotoNext(moveType, predicates))
            throw new InvalidOperationException("ILCursor.GotoNext: padrão não encontrado");
        return this;
    }

    public ILCursor GotoNext(params Func<Instruction, bool>[] predicates) =>
        GotoNext(MoveType.Before, predicates);

    public ILCursor GotoPrev(MoveType moveType, params Func<Instruction, bool>[] predicates)
    {
        if (!TryGotoPrev(moveType, predicates))
            throw new InvalidOperationException("ILCursor.GotoPrev: padrão não encontrado");
        return this;
    }

    public ILCursor GotoPrev(params Func<Instruction, bool>[] predicates) =>
        GotoPrev(MoveType.Before, predicates);

    /// <summary>Predicado nulo casa qualquer instrução (é o que o MonoMod faz).</summary>
    bool Search(int direction, MoveType moveType, Func<Instruction, bool>[] predicates)
    {
        if (predicates == null || predicates.Length == 0) return false;
        int count = Instrs.Count;
        int last = count - predicates.Length;
        if (last < 0) return false;

        if (direction > 0)
        {
            for (int start = index; start <= last; start++)
                if (Matches(start, predicates)) { Place(moveType, start, predicates.Length); return true; }
        }
        else
        {
            for (int start = Math.Min(index - 1, last); start >= 0; start--)
                if (Matches(start, predicates)) { Place(moveType, start, predicates.Length); return true; }
        }
        return false;
    }

    bool Matches(int start, Func<Instruction, bool>[] predicates)
    {
        for (int i = 0; i < predicates.Length; i++)
        {
            Func<Instruction, bool> p = predicates[i];
            if (p == null) continue;
            if (!p(Instrs[start + i])) return false;
        }
        return true;
    }

    void Place(MoveType moveType, int start, int length)
    {
        Index = moveType switch
        {
            MoveType.Before => start,
            MoveType.After => start + length,
            MoveType.BeforeInside => start + length - 1,
            MoveType.AfterInside => start + 1,
            _ => start,
        };
    }

    // ---------------------------------------------------------------- escrita

    public ILCursor Emit(Instruction instruction)
    {
        Instrs.Insert(Index, instruction);
        Index++;
        return this;
    }

    public ILCursor Emit(OpCode opcode) => Emit(Instruction.Create(opcode));

    public ILCursor Emit(OpCode opcode, TypeReference type) => Emit(Instruction.Create(opcode, type));

    public ILCursor Emit(OpCode opcode, MethodReference method) => Emit(Instruction.Create(opcode, method));

    public ILCursor Emit(OpCode opcode, FieldReference field) => Emit(Instruction.Create(opcode, field));

    public ILCursor Emit(OpCode opcode, string value) => Emit(Instruction.Create(opcode, value));

    public ILCursor Emit(OpCode opcode, sbyte value) => Emit(Instruction.Create(opcode, value));

    public ILCursor Emit(OpCode opcode, byte value) => Emit(Instruction.Create(opcode, (int)value));

    public ILCursor Emit(OpCode opcode, int value) => Emit(Instruction.Create(opcode, value));

    public ILCursor Emit(OpCode opcode, long value) => Emit(Instruction.Create(opcode, value));

    public ILCursor Emit(OpCode opcode, float value) => Emit(Instruction.Create(opcode, value));

    public ILCursor Emit(OpCode opcode, double value) => Emit(Instruction.Create(opcode, value));

    public ILCursor Emit(OpCode opcode, VariableDefinition variable) => Emit(Instruction.Create(opcode, variable));

    public ILCursor Emit(OpCode opcode, ParameterDefinition parameter) => Emit(Instruction.Create(opcode, parameter));

    /// <summary>Atalho do MonoMod real: <c>or</c> lógico.</summary>
    public ILCursor EmitOr() => Emit(OpCodes.Or);

    /// <summary>
    /// MonoMod real: injeta uma chamada a um delegate gerenciado. Devolve o
    /// índice da referência guardada no contexto (é isso que o mod recebe de
    /// volta). O corpo emitido é fiel; quem não executa é o jogo.
    /// </summary>
    public int EmitDelegate<T>(T del) where T : Delegate
    {
        int id = Context.AddReference(del);
        Emit(OpCodes.Ldc_I4, id);
        MethodReference? reference = DelegateReference(typeof(T));
        if (reference != null) Emit(OpCodes.Call, reference);
        else Emit(OpCodes.Pop);
        return id;
    }

    static MethodReference? DelegateReference(Type delegateType)
    {
        try
        {
            var open = typeof(ILContext).GetMethod(nameof(ILContext.GetReference),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (open == null) return null;
            var generic = new GenericInstanceMethod(ILContext.Scratch.ImportReference(open));
            generic.GenericArguments.Add(ILContext.Scratch.ImportReference(delegateType));
            return generic;
        }
        catch { return null; }
    }

    public ILCursor Remove()
    {
        if (index > 0 && index <= Instrs.Count)
        {
            Instrs.RemoveAt(index - 1);
            index--;
        }
        return this;
    }

    public ILCursor RemoveRange(int num)
    {
        for (int i = 0; i < num; i++) Remove();
        return this;
    }

    public ILLabel MarkLabel(ILLabel label)
    {
        label.Target = Next;
        return label;
    }

    public ILLabel MarkLabel(out ILLabel label)
    {
        label = new ILLabel();
        return MarkLabel(label);
    }
}
