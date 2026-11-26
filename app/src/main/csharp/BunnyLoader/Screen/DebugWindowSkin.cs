using System;
using System.Collections.Generic;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader.Core;

namespace BunnyLoader;

/// <summary>
/// v41: pele NATIVA da janela de depuracao.
///
/// A v39/v40 desenhava tudo com <c>MagicPixel</c> + <c>DrawBorderString</c>: fundo
/// chapado, borda de 3px e texto solto. O pedido e' usar os elementos do proprio
/// jogo, entao aqui a janela e' montada com os MESMOS widgets das telas nativas:
///
/// - <c>Panel_Layout</c> + <c>GUIPanel.Draw</c> para o fundo (o painel de 9 fatias
///   do menu de configuracoes);
/// - <c>StringButton_Layout</c> + <c>GUIStringButton.DrawButton</c> para o titulo;
/// - <c>String_Layout</code> + <c>GUIString.Draw</c> para o texto das linhas;
/// - <c>TransactionButton_Layout</c> + <c>GUITransactionButton.Draw</c> para os
///   botoes.
///
/// Os layouts sao CLONES (<c>object MemberwiseClone()</c>) de
/// <c>SettingsOverlay_Layout.Instance</c>, como o ConfigMenu e o ServerSyncMenu
/// fazem: clonar libera a janela de depender do menu aberto (o objeto do jogo
/// continua vivo e o nosso e' pinado por <see cref="GameHandle"/>).
///
/// Se algum desses layouts nao existir (menu ainda nao montado), <see cref="Ready"/>
/// fica false e <see cref="DebugWindow"/> cai no desenho antigo — a janela nunca
/// deixa de aparecer por causa da pele.
/// </summary>
internal static unsafe class DebugWindowSkin
{
    const int ScreenAnchor = 0;
    const int TopLeft = 9;
    const int Escape = 27;

    static readonly List<GameHandle> kept = new();
    static readonly GameClass ObjectClass = new("System", "Object");
    static MethodRef cloneMethod;
    static readonly int[] cloneLayout = { -1 };

    static Panel_Layout? backing;
    static StringButton_Layout? title;
    static String_Layout? text;
    static TransactionButton_Layout? button;

    internal static bool Ready => button is { IsNull: false };

    static nint Clone(nint obj)
    {
        ref readonly var m = ref GameInterop.Method(ref cloneMethod, ObjectClass, "object MemberwiseClone()", cloneLayout);
        CallFrame f;
        System.Runtime.CompilerServices.Unsafe.SkipInit(out f);
        f.X[0] = obj;
        f.X[1] = (long)m.Info;
        f.Ret = RetKind.Int;
        GameInterop.Call(m.Fn, &f);
        nint copy = (nint)(long)f.Out[0];
        if (copy != 0) kept.Add(new GameHandle(copy));
        return copy;
    }

    /// <summary>Clona os layouts uma vez. Devolve false enquanto o menu (ou o
    /// template dele) nao existir ainda.</summary>
    internal static bool Ensure()
    {
        if (Ready) return true;
        try
        {
            // v41: os getters do proxy devolvem null! quando o campo nativo e' 0
            // (nao um GameObject zerado), entao o teste tem de ser "is not {IsNull:false}"
            // para pegar os dois casos sem derrubar em NullReferenceException.
            SettingsOverlay_Layout? s = SettingsOverlay_Layout.Instance;
            if (s is not { IsNull: false }) return false;
            Panel_Layout? src = s.Backing;
            if (src is not { IsNull: false }) return false;
            StringButton_Layout? srcTitle = s.Title;
            if (srcTitle is not { IsNull: false }) return false;
            SettingsOverlayToggle_Layout? toggle = s.ToggleTemplate;
            if (toggle is not { IsNull: false }) return false;
            String_Layout? srcText = toggle.Option1Label;
            if (srcText is not { IsNull: false }) return false;
            TransactionButton_Layout? srcButton = toggle.ToggleButton;
            if (srcButton is not { IsNull: false }) return false;

            backing = new Panel_Layout(Clone(src.Pointer));
            title = new StringButton_Layout(Clone(srcTitle.Pointer));
            text = new String_Layout(Clone(srcText.Pointer));
            button = new TransactionButton_Layout(Clone(srcButton.Pointer));
            Anchor(backing!);
            Anchor(title!);
            Anchor(text!);
            Anchor(button!);
            text!.Alignment = (LayoutCalculator.AnchorType)0;
            Log.Info("[debug] pele nativa pronta (painel/titulo/linhas/botoes do proprio jogo)");
            return Ready;
        }
        catch (Exception e)
        {
            ItemHooks.Report("DebugWindowSkin (clone)", e);
            backing = null;
            title = null;
            text = null;
            button = null;
            return false;
        }
    }

    static void Anchor(Panel_Layout l)
    {
        l.AnchorControl = (ControlAnchor.ControlId)ScreenAnchor;
        l.Anchor = (LayoutCalculator.AnchorType)TopLeft;
    }

    static void Anchor(StringButton_Layout l)
    {
        l.AnchorControl = (ControlAnchor.ControlId)ScreenAnchor;
        l.Anchor = (LayoutCalculator.AnchorType)TopLeft;
    }

    static void Anchor(String_Layout l)
    {
        l.AnchorControl = (ControlAnchor.ControlId)ScreenAnchor;
        l.Anchor = (LayoutCalculator.AnchorType)TopLeft;
    }

    static void Anchor(TransactionButton_Layout l)
    {
        l.AnchorControl = (ControlAnchor.ControlId)ScreenAnchor;
        l.Anchor = (LayoutCalculator.AnchorType)TopLeft;
    }

    static bool Focus()
    {
        // v41: os getters devolvem null quando o campo nativo e' 0; testar os
        // dois casos evita NullReferenceException dentro do desenho.
        GUIInstance? gi = GUIInstance.Active;
        if (gi is null || gi.IsNull) return false;
        var pad = gi.GUIVirtualInputController;
        return pad is not null && !pad.IsNull && pad.ControllerActive;
    }

    internal static void Panel(Rectangle box)
    {
        if (backing == null) return;
        backing.Location = new Vector2(box.X, box.Y);
        backing.Size = new Vector2(box.Width, box.Height);
        GUIPanel.Draw(backing, false, default, default, default);
    }

    internal static void TitleBar(string value, Rectangle box, ref float scale)
    {
        if (title == null) return;
        title.Location = new Vector2(box.X, box.Y);
        title.Size = new Vector2(box.Width, box.Height);
        GUIStringButton.DrawButton(title, value, ref scale, false, true);
    }

    internal static void Line(string value, Vector2 at, Color color)
    {
        if (text == null) return;
        text.Location = at;
        GUIString.Draw(text, value, color, false);
    }

    /// <summary>Desenha um botao nativo. O clique continua sendo tratado pelo
    /// DebugWindow (retangulo proprio), para nao depender do estado de input da
    /// camada de UI do jogo.</summary>
    internal static void Button(Rectangle box, string value, bool disabled, ref float scale)
    {
        if (button == null) return;
        ControlAnchor._gridItemRegion = box;
        button.Location = new Vector2(box.X, box.Y);
        button.overloadedSize = new Vector2(box.Width, box.Height);
        GUITransactionButton.Draw(button, default(Item), value, disabled, ref scale, false, Focus(), false, false);
    }

    internal static float RowHeight()
    {
        if (text == null) return 26f;
        SpriteFont? font = text.GetFont();
        if (font is null || font.IsNull) return 24f * text.Scale;
        return font.LineSpacing * text.Scale;
    }

    /// <summary>Tamanho de texto na fonte nativa — a mesma que desenha as
    /// linhas, para a quebra de linha bater.</summary>
    internal static float Measure(string value)
    {
        if (text == null) return 0f;
        SpriteFont? font = text.GetFont();
        if (font is null || font.IsNull) return 0f;
        return font.MeasureString(value).X * text.Scale;
    }
}
