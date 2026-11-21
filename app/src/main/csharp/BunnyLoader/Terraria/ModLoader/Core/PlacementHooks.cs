using System;
using System.Collections.Generic;
using BunnyLoader;
using Terraria.DataStructures;
using Terraria.ObjectData;

namespace Terraria.ModLoader.Core;

internal static class PlacementHooks
{
    const int Marker = 0x5C000000;

    static readonly List<Func<int, int, int, int, int, int, int>> Hooks = new();

    internal static int Register(Func<int, int, int, int, int, int, int> hook)
    {
        int index;
        lock (Hooks)
        {
            index = Hooks.IndexOf(hook);
            if (index < 0)
            {
                index = Hooks.Count;
                Hooks.Add(hook);
            }
        }
        ItemHooks.Once("PlacementHooks", () => On_TileObjectData.CallPostPlacementPlayerHook += CallPostPlacementPlayerHook);
        return Marker + index;
    }

    static bool CallPostPlacementPlayerHook(On_TileObjectData.orig_CallPostPlacementPlayerHook orig, int tileX, int tileY, int type, int style, int dir, int alternate, TileObject data)
    {
        bool result = orig(tileX, tileY, type, style, dir, alternate, data);
        try
        {
            TileObjectData tileData = TileObjectData.GetTileData(type, style, data.alternate);

            // GetTileData devolve null gerenciado quando o tipo nao registrou dados
            // (aprendido com o SiblingPylon): sem o "is null" o IsNull estoura NRE e
            // o Hook_AfterPlacement do mod nunca roda (fica sem tile entity, sem
            // icone no mapa e sem teleporte).
            if (tileData is null || tileData.IsNull) return result;
            PlacementHook hook = tileData.HookPostPlaceMyPlayer;

            // HookFormat e' uma CLASSE no shim: quando o campo nativo esta vazio o
            // getter devolve null gerenciado, e "hook.hook.IsNull" estourava NRE
            // (foi o que impedia o tile entity do pylon de ser criado).
            PlacementHook.HookFormat? native = hook.hook;
            if (native is not null && !native.IsNull) return result;

            int index = hook.badReturn - Marker;
            Func<int, int, int, int, int, int, int>? call;
            lock (Hooks) call = (uint)index < (uint)Hooks.Count ? Hooks[index] : null;
            if (call == null) return result;
            if (hook.processedCoordinates)
            {
                Point16 origin = tileData.Origin;
                tileX -= origin.X;
                tileY -= origin.Y;
            }
            int entity = call(tileX, tileY, type, style, dir, data.alternate);
            if (placed.Add(type))
                Log.Info($"PlacementHook: tipo {type} -> tile entity {entity} em ({tileX},{tileY})");
        }
        catch (Exception e) { ItemHooks.Report("PlacementHook (mod placement hook)", e); }
        return result;
    }

    static readonly HashSet<int> placed = new();
}
