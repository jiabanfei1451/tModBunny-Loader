using System;

namespace Terraria;

/// <summary>
/// Parte escrita à mão de <see cref="On_SceneMetrics"/> (o resto é gerado pelo
/// ProxyGen a partir do dump do Android).
///
/// O 1.4.4.9 tem <c>SceneMetrics.ExportTileCountsToMain()</c> e o HookGen
/// publica <c>orig_/hook_ExportTileCountsToMain</c>; o dump do 1.4.5.8 desta
/// build NÃO tem esse método, então o gerador não criou a dupla de delegates.
/// O mod (Fargowiltas) referencia o tipo -> <c>TypeLoadException</c> ao carregar
/// a classe do mod -> o mod inteiro falha.
///
/// Aqui só reconstruímos a FORMA: o delegate e um evento vazio. Assinar
/// funciona, mas nunca dispara — perde-se o ajuste de contagem do
/// PurityTotem, não o mod.
/// </summary>
public static partial class On_SceneMetrics
{
#pragma warning disable CS0067 // evento nunca disparado (proposital)

    public delegate void orig_ExportTileCountsToMain(SceneMetrics self);

    public delegate void hook_ExportTileCountsToMain(orig_ExportTileCountsToMain orig, SceneMetrics self);

    /// <summary>
    /// Sem gancho nativo nesta versão do jogo: <c>+=</c>/<c>-=</c> são aceitos
    /// e ignorados de propósito (ver o comentário do tipo).
    /// </summary>
    public static event hook_ExportTileCountsToMain ExportTileCountsToMain
    {
        add { }
        remove { }
    }

#pragma warning restore CS0067
}
