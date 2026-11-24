using System;
using BunnyLoader;
using Terraria.ID;

namespace Terraria.ModLoader.Core;

internal static class MusicHooks
{
    const float FadeStep = 0.005f, Audible = 0.25f;
    const int ChooseEvery = 10;

    static int wanted = -1, chooseIn;
    static bool chosenOtherworld, titleCrossFade;
    const int TitleLoop = MusicID.TitleJourneysBeginning;
    static float? savedSilenceFade;

    internal static int Wanted => wanted;

    internal static int Playing => MusicLoader.TrackOf(wanted) is { } track && track.Fade > Audible ? wanted : -1;

    internal static void Install() => ItemHooks.Once("Music", () =>
    {
        On_Main.UpdateAudio_DecideOnNewMusic += DecideOnNewMusic;
        On_Main.UpdateAudio_DecideOnTOWMusic += DecideOnTOWMusic;
        On_Main.UpdateAudio += UpdateAudio;
    });

    static void DecideOnNewMusic(On_Main.orig_UpdateAudio_DecideOnNewMusic orig, Main self)
    {
        orig(self);
        Decide(false);
    }

    static void DecideOnTOWMusic(On_Main.orig_UpdateAudio_DecideOnTOWMusic orig, Main self)
    {
        orig(self);
        Decide(true);
    }

    static void UpdateAudio(On_Main.orig_UpdateAudio orig, Main self)
    {
        orig(self);
        try { Step(); }
        catch (Exception e) { ItemHooks.Report("Music (fade)", e); }
        try { WatchBox2(); }
        catch (Exception e) { ItemHooks.Report("Music (box2)", e); }
    }

    // O MusicEvents do UnCalamityModMusic (interludio do Hardmode, por exemplo)
    // nao toca som por conta propria: ele escreve Main.musicBox2 e deixa o
    // jogo tocar. Se este valor muda e nada sai do alto-falante, o problema
    // esta' depois desse ponto, nao no mod. Loga so' quando muda.
    static int lastBox2 = -1;

    static void WatchBox2()
    {
        int box = Main.musicBox2;
        if (box == lastBox2) return;
        lastBox2 = box;
        Log.Info($"musica: musicBox2 -> {box} (newMusic={Main.newMusic} cur={Main.curMusic} wanted={wanted})");
    }

    static int Choose(bool otherworld)
    {
        if (Main.gameMenu) return MenuLoader.MenuMusic();
        int box = MusicBoxHooks.Active();
        if (box > 0) return box;
        if (Main.SceneMetrics.ActiveMusicBox >= 0) return -1;
        int music = -1;
        SceneEffectPriority priority = SceneEffectPriority.None;
        FromNpcs(ref music, ref priority);
        if (SceneEffectLoader.Count > 0) SceneEffectLoader.UpdateMusic(ref music, ref priority);
        if (music < 0 || priority < SceneEffectPriority.BiomeLow) return -1;
        SceneEffectPriority tier = VanillaMusic.Tier(otherworld);
        bool wins = priority >= tier;
        string why = $"{priority} >= {tier} (otherworld={otherworld})";
        if (lastDiag != why)
        {
            lastDiag = why;
            Log.Info($"musica: cenario {music} prioridade {priority} vs vanilla {tier} -> {(wins ? "mod vence" : "vanilla vence")}");
        }
        return wins ? music : -1;
    }

    static string lastDiag = "";

    // v27 diagnostico: dois caminhos independentes decidiram "nao e cidade":
    // o tier nativo (SceneMetrics.TownNPCCount < 3) e o mod (Player.townNPCs > 2f).
    // Se os dois derem zero dentro de uma cidade real, o campo nativo nao esta'
    // sendo mantido neste port. Loga so' quando a faixa muda (raro).
    static void DiagTown()
    {
        try
        {
            var p = Main.LocalPlayer;
            int townNpcs = p.IsNull ? -1 : p.townNPCs;
            int metricsTown = Main.SceneMetrics.IsNull ? -1 : Main.SceneMetrics.TownNPCCount;
            bool inTown = p.IsNull ? false : p.townNPCs > 2;
            Log.Info($"[town] townNPCs={townNpcs} metricsTownNPCCount={metricsTown} playerInTown={inTown} day={Main.dayTime} time={Main.time:F0}");
        }
        catch (Exception e) { ItemHooks.Report("Music (town diag)", e); }
    }

    static void FromNpcs(ref int music, ref SceneEffectPriority priority)
    {
        var screen = Main.screenPosition;
        const float margin = 5000;
        float x0 = screen.X - margin, x1 = screen.X + Main.screenWidth + margin;
        float y0 = screen.Y - margin, y1 = screen.Y + Main.screenHeight + margin;
        var npcs = Main.npc;
        for (int i = 0; i < npcs.Length; i++)
        {
            NPC npc = npcs[i];
            if (!npc.active || npc.type < NPCID.Count || npc.ModNPC is not { } modNpc || modNpc.Music < 0) continue;
            var center = npc.Center;
            if (center.X < x0 || center.X > x1 || center.Y < y0 || center.Y > y1) continue;
            if (music < 0 || modNpc.SceneEffectPriority > priority)
            {
                music = modNpc.Music;
                priority = modNpc.SceneEffectPriority;
            }
        }
    }

    static void TitleCrossFade(bool theme)
    {
        var flags = Main.musicNoCrossFade;
        if (flags.IsNull || flags.Length <= TitleLoop) return;
        if (theme)
        {
            if (!flags[TitleLoop]) return;
            flags[TitleLoop] = false;
            titleCrossFade = true;
        }
        else if (titleCrossFade)
        {
            flags[TitleLoop] = true;
            titleCrossFade = false;
        }
    }

    static void SilenceGame(bool on)
    {
        var fade = Main.musicFade;
        if (on)
        {
            savedSilenceFade ??= fade[0];
            fade[0] = 1;
            Main.newMusic = 0;
        }
        else if (savedSilenceFade is float saved)
        {
            fade[0] = saved;
            savedSilenceFade = null;
        }
    }

    static void Decide(bool otherworld)
    {
        try
        {
            if (--chooseIn <= 0 || chosenOtherworld != otherworld || Main.gameMenu)
            {
                int previous = wanted;
                wanted = Choose(otherworld);
                chooseIn = ChooseEvery;
                chosenOtherworld = otherworld;
                if (wanted != previous)
                {
                    MusicLoader.Track? picked = MusicLoader.TrackOf(wanted);
                    Log.Info(picked != null
                        ? $"musica: faixa {wanted} <- {picked.File}"
                        : $"musica: vanilla ({wanted})");
                    DiagTown();
                }
            }
            TitleCrossFade(Main.gameMenu && wanted >= 0);
            MusicLoader.Track? track = MusicLoader.TrackOf(wanted);
            if (track != null && track.Fade > Audible)
            {
                SilenceGame(true);
                return;
            }
            SilenceGame(false);
            if (track != null) Main.newMusic = Main.curMusic;
            else if (wanted >= 0) Main.newMusic = wanted;
        }
        catch (Exception e) { ItemHooks.Report("Music (choice)", e); }
    }

    static void Step()
    {
        if (MusicLoader.Tracks.Count == 0) return;
        MusicLoader.Track? main = MusicLoader.TrackOf(Main.gameMenu ? MenuLoader.MenuMusic() : wanted);
        int current = Main.curMusic;
        bool mainAudible = main != null ? main.Fade > Audible : current <= 0 || Main.musicFade[current] > Audible;
        float volume = Main.musicVolume;
        foreach (MusicLoader.Track track in MusicLoader.Tracks)
        {
            if (track == main) track.Fade = Math.Min(1, track.Fade + FadeStep);
            else if (mainAudible) track.Fade = Math.Max(0, track.Fade - FadeStep);
            float v = track.Fade * volume;
            if (v == track.Sent) continue;
            MusicLoader.SetVolume(track, v);
            track.Sent = v;
        }
    }
}
