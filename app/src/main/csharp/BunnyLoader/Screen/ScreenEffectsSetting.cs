using System;
using System.IO;
using System.Text.Json;
using BunnyLoader.Interop;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader.Core;

namespace BunnyLoader;

internal static class ScreenEffectsSetting
{
    const int SettingNone = 192;
    const int GeneralCategory = 0;
    const float CaptionPadding = 16;

    static GameHandle? entry;
    static bool restored;

    internal static void Install() =>
        ItemHooks.Once("ScreenEffectsSetting", () =>
        {
            On_GUISettingsOverlay.SetCategory += SetCategory;
            On_GUIOpenUIButtonSetting.GetOption += GetOption;
            On_GUIOpenUIButtonSetting.GetButtonCaption += GetButtonCaption;
            On_GUIOpenUIButtonSetting.OpenUI += OpenUI;
        });

    static string File => Path.Combine(Main.SavePath, "BunnyLoader.screen.json");

    internal static void Restore()
    {
        if (restored) return;
        restored = true;
        string file;
        try { file = File; }
        catch (Exception)
        {
            restored = false;
            return;
        }
        if (!System.IO.File.Exists(file)) return;
        try
        {
            using JsonDocument json = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("Enabled", out JsonElement enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                ScreenEffects.Enabled = enabled.GetBoolean();
        }
        catch (Exception e)
        {
            Log.Warn($"ScreenEffects: {file} is broken ({e.Message}); screen effects stay on");
        }
    }

    internal static void Set(bool enabled)
    {
        restored = true;
        ScreenEffects.Enabled = enabled;
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new { Enabled = enabled })); }
        catch (Exception e) { ItemHooks.Report("ScreenEffectsSetting (save)", e); }
    }

    static bool Ours(GUIOpenUIButtonSetting self) => entry != null && self.Pointer == entry.Target;

    static void SetCategory(On_GUISettingsOverlay.orig_SetCategory orig, GUISettingsOverlay self, SettingsOverlayOptionCategories_Layout.Category category)
    {
        orig(self, category);
        try { AddEntry(self); }
        catch (Exception e) { ItemHooks.Report("ScreenEffectsSetting (row)", e); }
    }

    static void AddEntry(GUISettingsOverlay overlay)
    {
        if ((int)overlay.Selected != GeneralCategory) return;
        var list = new GameList(overlay._categorySettings);
        if (list.IsNull) return;
        Restore();
        if (entry == null)
        {
            GUIOpenUIButtonSetting created = GUIOpenUIButtonSetting.New((GUISettingID)SettingNone);
            created._category = (SettingsOverlayOptionCategories_Layout.Category)GeneralCategory;
            entry = new GameHandle(created.Pointer);
        }
        if (list.IndexOf(entry.Target) >= 0) return;
        int at = 0;
        for (int k = 0; k < list.Count; k++)
        {
            var item = new GameObject(list[k]);
            if (!GUIOpenUIButtonSetting.Is(item) || (int)new GUIOpenUIButtonSetting(list[k])._id != SettingNone) break;
            at = k + 1;
        }
        list.Insert(at, entry.Target);
    }

    static string Text(string key) => Language.GetTextValue("BunnyLoader." + key);

    static string? GetOption(On_GUIOpenUIButtonSetting.orig_GetOption orig, GUIOpenUIButtonSetting self) =>
        Ours(self) ? Text("ScreenEffectsRow") : orig(self);

    static string? GetButtonCaption(On_GUIOpenUIButtonSetting.orig_GetButtonCaption orig, GUIOpenUIButtonSetting self)
    {
        if (!Ours(self)) return orig(self);
        string value = Text(ScreenEffects.Enabled ? "ScreenEffectsOn" : "ScreenEffectsOff");
        try
        {
            TransactionButton_Layout caption = self.GetLayout().Caption;
            String_Layout label = caption.Label;
            SpriteFont font = label.GetFont();
            float width = caption.overloadedSize.X - CaptionPadding;
            if (font.IsNull || width <= 0) return value;
            float Measure(string text) => font.MeasureString(text).X * label.Scale;
            if (Measure(value) <= width) return value;
            string cut = value;
            while (cut.Length > 1 && Measure(cut + "...") > width) cut = cut[..^1];
            return cut + "...";
        }
        catch (Exception e) { ItemHooks.Report("ScreenEffectsSetting (caption width)", e); }
        return value;
    }

    static void OpenUI(On_GUIOpenUIButtonSetting.orig_OpenUI orig, GUIOpenUIButtonSetting self)
    {
        if (!Ours(self))
        {
            orig(self);
            return;
        }
        Set(!ScreenEffects.Enabled);
    }
}

/// <summary>
/// v29: "mostrar townNPCs" option in the general settings menu. Reuses the same
/// GUISettingID 192 row trick as ScreenEffectsSetting, but stores its own file
/// and defaults to OFF.
/// </summary>
internal static class TownDebugSetting
{
    const int SettingNone = 192;
    const int IconSettingNone = 193;
    const int GeneralCategory = 0;

    static GameHandle? entry, iconEntry;
    static bool restored;

    internal static bool Enabled;

    /// <summary>
    /// v40: se o icone "调" aparece na tela. Separado de <see cref="Enabled"/> (角标)
    /// porque sao duas coisas diferentes: o resumo no canto e a porta da janela.
    /// Padrao ligado — senao ninguem acha a janela sem passar pelo menu.
    /// </summary>
    internal static bool IconEnabled = true;

    /// <summary>
    /// v42: no fim de cada quadro, se o device estiver preso num alvo de
    /// renderizacao (mod que trocou de alvo e nao voltou), volta para a tela.
    /// E' diagnostico e correcao: o mundo some exatamente quando o quadro vai
    /// parar num alvo que nunca e' apresentado. Padrao ligado; desliga na
    /// janela de depuracao para comparar o quadro "como o mod deixou".
    /// </summary>
    internal static bool ResetTargets = true;

    internal static void Install() =>
        ItemHooks.Once("TownDebugSetting", () =>
        {
            // v39: a linha "Debug" deixou de ser interruptor — agora abre a janela
            // de segundo nivel (DebugWindow), que tem a caixa rolavel.
            DebugWindow.Install();
            On_GUISettingsOverlay.SetCategory += SetCategory;
            On_GUIOpenUIButtonSetting.GetOption += GetOption;
            On_GUIOpenUIButtonSetting.GetButtonCaption += GetButtonCaption;
            On_GUIOpenUIButtonSetting.OpenUI += OpenUI;
        });

    static string File => Path.Combine(Main.SavePath, "BunnyLoader.towndebug.json");

    internal static void Restore()
    {
        if (restored) return;
        restored = true;
        string file;
        try { file = File; }
        catch (Exception) { restored = false; return; }
        if (!System.IO.File.Exists(file)) return;
        try
        {
            using JsonDocument json = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("Enabled", out JsonElement enabled) &&
                enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                Enabled = enabled.GetBoolean();
            if (json.RootElement.TryGetProperty("IconEnabled", out JsonElement icon) &&
                icon.ValueKind is JsonValueKind.True or JsonValueKind.False)
                IconEnabled = icon.GetBoolean();
            if (json.RootElement.TryGetProperty("ResetTargets", out JsonElement reset) &&
                reset.ValueKind is JsonValueKind.True or JsonValueKind.False)
                ResetTargets = reset.GetBoolean();
        }
        catch (Exception e)
        {
            Log.Error($"TownDebug: {file} is broken ({e.Message}); overlay stays off");
        }
    }

    internal static void Set(bool enabled)
    {
        restored = true;
        Enabled = enabled;
        Save();
    }

    /// <summary>v40: liga/desliga o icone "调" da tela.</summary>
    internal static void SetIcon(bool enabled)
    {
        restored = true;
        IconEnabled = enabled;
        Save();
    }

    /// <summary>v42: liga/desliga o vigia do alvo de renderizacao.</summary>
    internal static void SetResetTargets(bool enabled)
    {
        restored = true;
        ResetTargets = enabled;
        Save();
    }

    static void Save()
    {
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new { Enabled, IconEnabled, ResetTargets })); }
        catch (Exception e) { ItemHooks.Report("TownDebugSetting (save)", e); }
    }

    static bool Ours(GUIOpenUIButtonSetting self) => entry != null && self.Pointer == entry.Target;

    static bool OursIcon(GUIOpenUIButtonSetting self) => iconEntry != null && self.Pointer == iconEntry.Target;

    static bool OursAny(GUIOpenUIButtonSetting self) => Ours(self) || OursIcon(self);

    static void SetCategory(On_GUISettingsOverlay.orig_SetCategory orig, GUISettingsOverlay self, SettingsOverlayOptionCategories_Layout.Category category)
    {
        orig(self, category);
        try { AddEntry(self); }
        catch (Exception e) { ItemHooks.Report("TownDebugSetting (row)", e); }
    }

    static void AddEntry(GUISettingsOverlay overlay)
    {
        if ((int)overlay.Selected != GeneralCategory) return;
        var list = new GameList(overlay._categorySettings);
        if (list.IsNull) return;
        Restore();
        if (entry == null)
        {
            GUIOpenUIButtonSetting created = GUIOpenUIButtonSetting.New((GUISettingID)SettingNone);
            created._category = (SettingsOverlayOptionCategories_Layout.Category)GeneralCategory;
            entry = new GameHandle(created.Pointer);
        }
        if (iconEntry == null)
        {
            // v40: linha irma, com id proprio, so' para ligar/desligar o icone.
            GUIOpenUIButtonSetting created = GUIOpenUIButtonSetting.New((GUISettingID)IconSettingNone);
            created._category = (SettingsOverlayOptionCategories_Layout.Category)GeneralCategory;
            iconEntry = new GameHandle(created.Pointer);
        }
        // onde termina o bloco das nossas linhas (elas ficam no topo da lista)
        int at = 0;
        for (int k = 0; k < list.Count; k++)
        {
            var item = new GameObject(list[k]);
            if (!GUIOpenUIButtonSetting.Is(item)) break;
            int id = (int)new GUIOpenUIButtonSetting(list[k])._id;
            if (id != SettingNone && id != IconSettingNone) break;
            at = k + 1;
        }
        if (list.IndexOf(entry.Target) < 0)
        {
            list.Insert(Math.Min(at, list.Count), entry.Target);
            at++;
        }
        if (list.IndexOf(iconEntry.Target) < 0)
            list.Insert(Math.Min(at, list.Count), iconEntry.Target);
    }

    static string Text(string key) => Language.GetTextValue("BunnyLoader." + key);

    static string? GetOption(On_GUIOpenUIButtonSetting.orig_GetOption orig, GUIOpenUIButtonSetting self)
    {
        if (Ours(self)) return Text("TownDebugRow");
        if (OursIcon(self)) return Text("IconRow");
        return orig(self);
    }

    static string? GetButtonCaption(On_GUIOpenUIButtonSetting.orig_GetButtonCaption orig, GUIOpenUIButtonSetting self)
    {
        if (Ours(self)) return Text("TownDebugOpen");
        if (OursIcon(self)) return Text(IconEnabled ? "TownDebugOn" : "TownDebugOff");
        return orig(self);
    }

    static void OpenUI(On_GUIOpenUIButtonSetting.orig_OpenUI orig, GUIOpenUIButtonSetting self)
    {
        if (Ours(self))
        {
            // v39: abre a janela de segundo nivel em vez de ligar/desligar o angulo.
            DebugWindow.Open();
            return;
        }
        if (OursIcon(self))
        {
            SetIcon(!IconEnabled);
            Log.Info($"[debug] icone da janela: {(IconEnabled ? "ligado" : "desligado")}");
            return;
        }
        orig(self);
    }
}
