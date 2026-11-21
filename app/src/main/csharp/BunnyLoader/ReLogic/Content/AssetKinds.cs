using System;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;

namespace ReLogic.Content;

/// <summary>
/// Leitura/escrita atômica de um <see cref="IntPtr"/> SEM passar por método
/// genérico.
///
/// <c>System.Threading.Volatile.Read&lt;T&gt;(ref T)</c> é um método genérico;
/// com <c>T = IntPtr</c> (value type) o Mono precisa da instanciação
/// "gsharedvt", e quando a chamada sai de dentro de um corpo genérico
/// compartilhado (<c>Asset`1&lt;T_REF&gt;</c>) o JIT pede um "other slot" do
/// rgctx da classe que o template do Mono do jogo não tem: aborta com
/// <c>mini-generic-sharing.c:911, condition `oti' not met</c>.
/// As sobrecargas de <c>Interlocked</c> para <see cref="IntPtr"/> são
/// não genéricas, então não pedem slot nenhum.
/// </summary>
internal static class PointerSlot
{
    internal static IntPtr Read(ref IntPtr slot) => System.Threading.Interlocked.CompareExchange(ref slot, IntPtr.Zero, IntPtr.Zero);

    internal static void Write(ref IntPtr slot, IntPtr value) => System.Threading.Interlocked.Exchange(ref slot, value);
}
/// <summary>
/// Os testes de tipo do <see cref="Asset{T}"/> moram aqui, numa classe SEM
/// genéricos, de propósito.
///
/// O Mono do jogo (Unity, libmonosgen-2.0) aborta com
/// <c>Assertion at mini-generic-sharing.c:911, condition `oti' not met</c>
/// quando código dentro de uma instanciação compartilhada (<c>Asset`1&lt;T_REF&gt;</c>)
/// chama um método estático da PRÓPRIA classe genérica: o JIT precisa do
/// "other slot" do rgctx da classe para o handle do método e o template que o
/// Mono monta para instanciações criadas depois (as do shim, carregado por
/// Assembly.Load) não tem esse slot. Foi assim que o jogo morria ao desenhar
/// qualquer bloco de mod que faz <c>TextureAssets.Tile[type].Value</c>
/// (OmnistationSheet, MusicBoxTile...).
///
/// Chamar métodos estáticos de uma classe NÃO genérica, de dentro do corpo
/// genérico, não pede slot nenhum. Por isso os testes ficam aqui e o
/// <see cref="Asset{T}"/> só chama estes.
/// </summary>
internal static class AssetKinds
{
    /// <summary><c>Asset&lt;DynamicSpriteFont&gt;</c> (tML 1.4.4.9) e <c>Asset&lt;SpriteFont&gt;</c> carregam fonte.</summary>
    internal static bool IsFontType(Type type) =>
        type == typeof(SpriteFont) || type == typeof(DynamicSpriteFont);

    /// <summary><c>Asset&lt;Effect&gt;</c> (Luminance <c>ShaderManager.LoadShaders</c>): shader não existe neste port, vira stub.</summary>
    internal static bool IsEffectType(Type type) => type == typeof(Effect);

    internal static void CheckSupported(Type type)
    {
        if (type != typeof(Texture2D) && !IsFontType(type) && !IsEffectType(type))
            throw new NotSupportedException($"Asset<{type.Name}>: only Texture2D, a font (DynamicSpriteFont/SpriteFont) and Effect can be requested");
    }
}
