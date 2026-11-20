namespace BunnyLoader;

using System.Runtime.CompilerServices;

/// <summary>
/// Ponte PÚBLICA usada pelo IL que o <see cref="IlPatcher"/> reescreve.
///
/// O <c>IlPatcher</c> é <c>internal</c> (ninguém fora do loader deve chamá-lo),
/// mas o código do mod precisa EXECUTAR <c>Read&lt;T&gt;</c> depois da
/// reescrita. Um método <c>public</c> dentro de uma classe <c>internal</c>
/// continua inacessível de fora do assembly — era exatamente o
/// <c>MethodAccessException: IlPatcher.Read[int](int&amp;)</c> que derrubava
/// todos os <c>SetDefaults</c> do Fargowiltas. Por isso a ponte mora numa
/// classe pública e minúscula.
/// </summary>
public static class IlInterop
{
    /// <summary>Descarrega uma referência (<c>T&amp;</c>) no valor.</summary>
    public static T Read<T>(ref T value) => value;

    /// <summary>
    /// <c>array[index]</c> de um array EMBUTIDO no objeto do jogo
    /// (<c>Projectile.ai</c>, <c>Item.glowColor</c>…), que o dump declara como
    /// struct de tamanho fixo e não como <c>T[]</c>.
    ///
    /// Recebe o array por <c>ref</c> de propósito: o elemento está DENTRO da
    /// memória do dono, então uma cópia por valor leria certo mas escreveria
    /// no nada. Com o endereço, <c>Unsafe.Add</c> aponta direto para o campo
    /// nativo — é o equivalente exato do <c>ldelem</c> original.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ArrayGet<TArray, T>(ref TArray array, int index) where TArray : struct
        => Unsafe.Add(ref Unsafe.As<TArray, T>(ref array), index);

    /// <summary><c>array[index] = value</c> do array embutido.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ArraySet<TArray, T>(ref TArray array, int index, T value) where TArray : struct
        => Unsafe.Add(ref Unsafe.As<TArray, T>(ref array), index) = value;

    /// <summary>
    /// Instancia GERENCIADA vazia para um slot do jogo que o dump do il2cpp
    /// perdeu (o ProxyGen so conseguiu emitir <c>Interop.GameObject</c>).
    ///
    /// O caso concreto: <c>NPCID.Sets.BossBestiaryPriority</c> e
    /// <c>List&lt;int&gt;</c> no contrato do tModLoader e uma lista NATIVA do
    /// il2cpp no jogo — nenhum objeto do jogo implementa a vtable do
    /// <c>List&lt;T&gt;</c> do Mono, entao o <c>callvirt Add</c> do mod nunca
    /// verificaria (era InvalidProgram nos 20 bosses do FargowiltasSouls).
    /// Devolver uma lista vazia deixa o metodo do mod COMPLETO; o que ele
    /// escreve aqui nao chega ao jogo (so a ordenacao do bestiario).
    ///
    /// A chave e o nome do slot: o mesmo mod que guarda a referencia e
    /// compara identidade continua vendo o mesmo objeto.
    /// </summary>
    public static T? ManagedSlot<T>(string key) where T : class
    {
        lock (ManagedSlots)
        {
            if (ManagedSlots.TryGetValue(key, out object? cached)) return (T?)cached;
            object? made;
            try { made = System.Activator.CreateInstance(typeof(T)); }
            catch (System.Exception e)
            {
                Log.Warn("il patch: slot " + key + " sem construtor vazio (" + e.GetType().Name + "); mod recebe null");
                made = null;
            }
            ManagedSlots[key] = made;
            return (T?)made;
        }
    }

    /// <summary>Cache dos slots fabricados, por <c>Dono::Campo</c>.</summary>
    static readonly System.Collections.Generic.Dictionary<string, object?> ManagedSlots = new();
}
