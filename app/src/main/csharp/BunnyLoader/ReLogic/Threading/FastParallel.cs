namespace ReLogic.Threading;

/// <summary>
/// O delegate que o <c>FastParallel</c> usa para dividir um intervalo.
///
/// Faltava no shim, e o Luminance guarda uma instância dele num campo do
/// closure do <c>ParticleManager</c> — tipo de campo ausente derruba a classe
/// no carregamento.
/// </summary>
public delegate void ParallelForAction(int fromInclusive, int toExclusive);

/// <summary>
/// Versão mínima: o jogo roda numa thread só para o mod, e o caminho que o
/// Luminance usa é só o <c>For</c> com contagem fixa de threads. Fica
/// sequencial de propósito — paralelismo aqui não traz ganho e mexeria com o
/// coletor de lixo do il2cpp no meio do desenho.
/// </summary>
public static class FastParallel
{
    public static void For(int fromInclusive, int toExclusive, ParallelForAction action) =>
        action(fromInclusive, toExclusive);

    public static void For(int fromInclusive, int toExclusive, ParallelForAction action, int minimumActionsPerThread) =>
        action(fromInclusive, toExclusive);
}
