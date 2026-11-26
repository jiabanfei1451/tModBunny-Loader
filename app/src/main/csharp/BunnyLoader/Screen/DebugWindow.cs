using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader.Core;

namespace BunnyLoader;

/// <summary>
/// v41: janela de depuracao de segundo nivel, com controle na tela e pele nativa.
///
/// Tres partes:
///
/// 1. um **icone** ("调") no canto superior esquerdo, logo abaixo do resumo
///    <c>[town]</c>, na camada de interface (estagio
///    <c>DrawInterface_33_MouseText</c>). Tocar nele abre/fecha a janela. Tem
///    interruptor proprio no menu de configuracoes (<c>悬浮窗图标：</code>).
/// 2. a **janela** com a caixa rolavel, desenhada por
///    <see cref="DebugWindowSkin"/> (painel, titulo, texto e botoes do proprio
///    jogo) — e nao mais por retangulo de MagicPixel. Se a pele nativa nao
///    estiver disponivel, cai no desenho simples.
/// 3. o mesmo angulo <c>[town]</code> de antes, ligado no menu.
/// </summary>
internal static class DebugWindow
{
    const float LineHeight = 30f;
    const float PadX = 30f;
    const float TitleBand = 66f;
    const float FooterBand = 126f;
    const float DragThreshold = 10f;
    const float BackWidth = 240f;
    const float BackHeight = 56f;

    // mesmo canto do resumo em SystemDrawHooks (20 / 40 / 22)
    const float TownLeft = 20f;
    const float TownTop = 40f;
    const float TownLineHeight = 22f;
    const float IconSize = 64f;

    static bool open;
    static float scroll;
    static float maxScroll;
    static bool wasDown, pressed, moved;
    static float pressY, pressScroll;
    static bool iconWasDown;
    static float titleScale = 1f, backScale = 1f, iconScale = 1f, cornerScale = 1f, targetScale = 1f;

    internal static bool IsOpen => open;

    internal static void Install() => ItemHooks.Once("DebugWindow", () =>
    {
        // v40: o estagio de MouseText e' o ultimo da interface, entao o icone e a
        // janela ficam por cima de tudo (inclusive do menu de configuracoes).
        On_Main.DrawInterface_33_MouseText += DrawHud;
        WorldViewProbe.Install();
        Log.Info("[debug] icone da janela de depuracao instalado (canto superior esquerdo)");
    });

    internal static void Open()
    {
        open = true;
        scroll = 0;
        maxScroll = 0;
        wasDown = pressed = moved = false;
        Log.Info("[debug] janela aberta (caixa rolavel)");
    }

    internal static void Close()
    {
        if (!open) return;
        open = false;
        pressed = wasDown = false;
        Log.Info("[debug] janela fechada");
    }

    static void Toggle()
    {
        if (open) Close();
        else Open();
    }

    static void DrawHud(On_Main.orig_DrawInterface_33_MouseText orig, Main self)
    {
        orig(self);
        if (Main.gameMenu) return;
        try
        {
            DrawIcon();
            if (open) Draw();
        }
        catch (Exception e)
        {
            ItemHooks.Report("DebugWindow (hud)", e);
            Close();
        }
    }

    static Rectangle Box(float x, float y, float w, float h) =>
        new((int)Math.Round(x), (int)Math.Round(y), Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));

    static Rectangle IconRect()
    {
        float y = TownTop + (TownDebugSetting.Enabled ? 7 * TownLineHeight + 10f : 0f);
        return Box(TownLeft, y, IconSize, IconSize);
    }

    /// <summary>Icone de entrada. Toca = abre/fecha.</summary>
    static void DrawIcon()
    {
        // v40: o icone tem interruptor proprio (IconEnabled). Desligado, a janela
        // so' abre pelo menu de configuracoes.
        if (!TownDebugSetting.IconEnabled)
        {
            iconWasDown = Main.mouseLeft;
            return;
        }
        SpriteBatch batch = Main.spriteBatch;
        if (batch is null || batch.Pointer == 0) return;
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        if (pixel is null || pixel.IsNull) return;

        Rectangle r = IconRect();
        Color edge = new(255, 235, 120, open ? 255 : 170);
        bool hover = r.Contains((int)Main.mouseX, (int)Main.mouseY);
        batch.Draw(pixel, r, new Color(8, 10, 18, hover ? 235 : 200));
        batch.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, 2), edge);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - 2, r.Width, 2), edge);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, 2, r.Height), edge);
        batch.Draw(pixel, new Rectangle(r.Right - 2, r.Y, 2, r.Height), edge);
        Utils.DrawBorderString(batch, "调",
            new Vector2(r.X + 18f, r.Y + 16f), new Color(255, 235, 120, 255), 1.15f, 0f, 0f, -1);

        bool down = Main.mouseLeft;
        if (down && !iconWasDown && r.Contains((int)Main.mouseX, (int)Main.mouseY))
        {
            Toggle();
            // o mesmo toque ja foi entregue ao jogo no Update; zerar aqui evita
            // que ele vire tambem um clique dentro da janela neste frame.
            Main.mouseLeft = false;
            Main.mouseLeftRelease = false;
            wasDown = false;
            pressed = false;
        }
        iconWasDown = down;
    }

    static void Draw()
    {
        SpriteBatch batch = Main.spriteBatch;
        if (batch is null || batch.Pointer == 0) return;
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        if (pixel is null || pixel.IsNull) return;

        float sw = Main.screenWidth, sh = Main.screenHeight;
        float boxW = Math.Min(sw * 0.9f, 1400f);
        float boxH = Math.Min(sh * 0.8f, 1000f);
        float x = (sw - boxW) * 0.5f, y = (sh - boxH) * 0.5f;
        Rectangle box = Box(x, y, boxW, boxH);

        bool native = DebugWindowSkin.Ensure();
        Color edge = new(255, 235, 120, 255);
        Color textColor = new(226, 231, 255, 255);

        if (native)
        {
            DebugWindowSkin.Panel(box);
        }
        else
        {
            batch.Draw(pixel, box, new Color(8, 10, 18, 238));
            batch.Draw(pixel, Box(x, y, boxW, 3), edge);
            batch.Draw(pixel, Box(x, y + boxH - 3, boxW, 3), edge);
            batch.Draw(pixel, Box(x, y, 3, boxH), edge);
            batch.Draw(pixel, Box(x + boxW - 3, y, 3, boxH), edge);
        }

        string heading = Language.GetTextValue("BunnyLoader.TownDebugRow");
        if (native)
        {
            titleScale = Math.Clamp(titleScale, 0.7f, 1.3f);
            DebugWindowSkin.TitleBar(heading, Box(x + PadX, y + 8, boxW - PadX * 2, 46), ref titleScale);
        }
        else
        {
            Utils.DrawBorderString(batch, heading, new Vector2(x + PadX, y + 16), edge, 1.3f, 0f, 0f, -1);
        }

        float top = y + TitleBand;
        float areaH = boxH - TitleBand - FooterBand;
        List<string> lines = SystemDrawHooks.DebugLines();

        float lineHeight = LineHeight;
        if (native)
        {
            float row = DebugWindowSkin.RowHeight();
            lineHeight = Math.Max(24f, row + 8f);
        }
        else
        {
            batch.Draw(pixel, Box(x + PadX, y + TitleBand - 10, boxW - PadX * 2, 2), new Color(255, 255, 255, 60));
        }

        float total = lines.Count * lineHeight;
        maxScroll = Math.Max(0f, total - areaH + 10f);
        if (scroll > maxScroll) scroll = maxScroll;
        if (scroll < 0f) scroll = 0f;

        Rectangle listArea = Box(x + PadX - 8, top, boxW - PadX * 2 + 16, areaH);
        if (native)
        {
            Rectangle saved = ControlAnchor._gridItemRegion;
            SpriteBatchItem.EnableClipping(listArea, listArea, batch, true);
            try { PaintLines(lines, x, top, lineHeight, textColor, native); }
            finally
            {
                SpriteBatchItem.DisabledClipping();
                ControlAnchor._gridItemRegion = saved;
            }
        }
        else
        {
            PaintLines(lines, x, top, lineHeight, textColor, native);
        }

        if (maxScroll > 0f)
        {
            float track = areaH - 16f;
            float thumb = Math.Max(40f, track * (areaH / total));
            float position = scroll / maxScroll * (track - thumb);
            float barX = x + boxW - 16f;
            batch.Draw(pixel, Box(barX, top + 8, 8, track), new Color(0, 0, 0, 90));
            batch.Draw(pixel, Box(barX, top + 8 + position, 8, thumb), edge);
        }

        float fx = x + PadX, fw = boxW - PadX * 2;
        float hintY = y + boxH - FooterBand + 12f;
        Utils.DrawBorderString(batch, "关闭：点左上角「调」图标，或按下面的按钮。图标与角标的开关在设置里",
            new Vector2(fx, hintY + 4), new Color(180, 186, 215, 255), 0.95f, 0f, 0f, -1);

        // v41: dois botoes nativos (o jogo tem ToggleTemplate.ToggleButton).
        // v42: terceiro botao — o vigia do alvo de renderizacao.
        Rectangle iconToggle = Box(fx, hintY + 40, 260, 56);
        Rectangle cornerToggle = Box(fx + 276, hintY + 40, 260, 56);
        Rectangle targetToggle = Box(fx + 552, hintY + 40, 300, 56);
        if (native)
        {
            iconScale = Math.Clamp(iconScale, 0.6f, 1.2f);
            cornerScale = Math.Clamp(cornerScale, 0.6f, 1.2f);
            targetScale = Math.Clamp(targetScale, 0.6f, 1.2f);
            DebugWindowSkin.Button(iconToggle, "图标：" + (TownDebugSetting.IconEnabled ? "开" : "关"), false, ref iconScale);
            DebugWindowSkin.Button(cornerToggle, "角标：" + (TownDebugSetting.Enabled ? "开" : "关"), false, ref cornerScale);
            DebugWindowSkin.Button(targetToggle, "复位目标：" + (TownDebugSetting.ResetTargets ? "开" : "关"), false, ref targetScale);
        }

        Rectangle back = Box(x + (boxW - BackWidth) * 0.5f, y + boxH - 66f, BackWidth, BackHeight);
        if (native)
        {
            backScale = Math.Clamp(backScale, 0.6f, 1.2f);
            DebugWindowSkin.Button(back, "返回", false, ref backScale);
        }
        else
        {
            batch.Draw(pixel, back, new Color(38, 42, 62, 238));
            batch.Draw(pixel, new Rectangle(back.X, back.Y, back.Width, 2), edge);
            Utils.DrawBorderString(batch, "返回", new Vector2(back.X + BackWidth * 0.5f, back.Y + 12f), edge, 1.05f, 0.5f, 0f, -1);
        }

        HandleInput(box, top, areaH, back, iconToggle, cornerToggle, targetToggle);
    }

    static void PaintLines(List<string> lines, float x, float top, float lineHeight, Color color, bool native)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            float ly = top + 6f - scroll + i * lineHeight;
            if (ly < top - lineHeight || ly > top + (Main.screenHeight * 0.8f)) continue;
            if (native) DebugWindowSkin.Line(lines[i], new Vector2(x + PadX, ly), color);
            else Utils.DrawBorderString(Main.spriteBatch, lines[i], new Vector2(x + PadX, ly), color, 1f, 0f, 0f, -1);
        }
    }

    static void HandleInput(Rectangle box, float listTop, float listH, Rectangle back, Rectangle iconToggle, Rectangle cornerToggle, Rectangle targetToggle)
    {
        bool down = Main.mouseLeft;
        float mx = Main.mouseX, my = Main.mouseY;
        bool inList = mx >= box.X + PadX && mx <= box.Right - 30f && my >= listTop && my <= listTop + listH;

        if (down && !wasDown)
        {
            pressed = inList;
            moved = false;
            pressY = my;
            pressScroll = scroll;
        }
        else if (down && pressed)
        {
            if (Math.Abs(my - pressY) > DragThreshold) moved = true;
            if (moved)
            {
                scroll = pressScroll + (pressY - my);
                if (scroll < 0f) scroll = 0f;
                if (scroll > maxScroll) scroll = maxScroll;
            }
        }
        else if (!down && wasDown)
        {
            bool wasPressed = pressed;
            pressed = false;
            if (!wasPressed && !moved)
            {
                if (back.Contains((int)mx, (int)my))
                {
                    Close();
                    return;
                }
                if (iconToggle.Contains((int)mx, (int)my))
                {
                    TownDebugSetting.SetIcon(!TownDebugSetting.IconEnabled);
                    Log.Info($"[debug] icone da janela: {(TownDebugSetting.IconEnabled ? "ligado" : "desligado")}");
                }
                else if (cornerToggle.Contains((int)mx, (int)my))
                {
                    TownDebugSetting.Set(!TownDebugSetting.Enabled);
                    Log.Info($"[debug] angulo do canto: {(TownDebugSetting.Enabled ? "ligado" : "desligado")}");
                }
                else if (targetToggle.Contains((int)mx, (int)my))
                {
                    TownDebugSetting.SetResetTargets(!TownDebugSetting.ResetTargets);
                    Log.Info($"[debug] vigia do alvo: {(TownDebugSetting.ResetTargets ? "ligado" : "desligado")}");
                }
            }
        }

        wasDown = down;
    }
}

