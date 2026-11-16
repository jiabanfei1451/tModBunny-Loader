using Mono.Cecil.Cil;

namespace MonoMod.Cil;

/// <summary>
/// Rótulo de desvio do MonoMod.
///
/// O MonoMod não usa <c>Instruction</c> direto como alvo de <c>br</c>: ele
/// envolve num <see cref="ILLabel"/> para poder marcar o destino DEPOIS de
/// emitir o desvio (é o idioma <c>c.Emit(OpCodes.Br, label); ...; c.MarkLabel(label)</c>).
/// O Cecil já trabalha com a instrução-alvo, então aqui o rótulo é só o
/// invólucro do <see cref="Target"/>.
/// </summary>
public class ILLabel
{
    /// <summary>Instrução de destino; <c>null</c> enquanto não marcado.</summary>
    public Instruction? Target { get; set; }

    public ILLabel() { }

    public ILLabel(Instruction target) => Target = target;
}
