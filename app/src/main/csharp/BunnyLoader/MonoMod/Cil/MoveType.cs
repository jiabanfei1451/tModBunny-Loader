namespace MonoMod.Cil;

/// <summary>
/// Onde o cursor para depois de uma busca por padrão (<c>GotoNext</c>).
///
/// A ORDEM DOS MEMBROS É CONTRATO BINÁRIO: o mod compilado no PC gravou o
/// valor numérico dentro do IL (<c>ldc.i4.1</c> para <c>After</c>), então esta
/// enum tem de repetir a de <c>MonoMod.Cil.MoveType</c> do MonoMod real.
/// </summary>
public enum MoveType
{
    /// <summary>Antes da primeira instrução casada. É o padrão de <c>GotoNext</c>.</summary>
    Before = 0,

    /// <summary>Depois da última instrução casada.</summary>
    After = 1,

    /// <summary>Antes da última instrução casada (dentro da sequência).</summary>
    BeforeInside = 2,

    /// <summary>Depois da primeira instrução casada (dentro da sequência).</summary>
    AfterInside = 3,
}
