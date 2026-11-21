using System;
using Terraria.ModLoader;

namespace Terraria.ID;

public sealed partial class TileID
{
    public static partial class Sets
    {
        static bool[] countsAsHoneyForCrafting = Array.Empty<bool>();

        static bool[] countsAsLavaForCrafting = Array.Empty<bool>();

        static bool[] multiTileSway = Array.Empty<bool>();

        static bool[] clock = Array.Empty<bool>();

        static int[] openDoorID = Array.Empty<int>();

        static int[] closeDoorID = Array.Empty<int>();

        public static bool[] CountsAsHoneyForCrafting => Grow(ref countsAsHoneyForCrafting);

        public static bool[] CountsAsLavaForCrafting => Grow(ref countsAsLavaForCrafting);

        public static bool[] MultiTileSway => Grow(ref multiTileSway);

        public static bool[] Clock => Grow(ref clock, GrandfatherClocks);

        public static int[] OpenDoorID => GrowInt(ref openDoorID);

        public static int[] CloseDoorID => GrowInt(ref closeDoorID);

        // Marcadores que existem no PC 1.4.5 mas nao no dump do Android: guardamos
        // so no lado gerenciado para que o SetStaticDefaults dos mods rode inteiro.
        // Sem isso o IlPatcher troca o corpo do metodo por "return" e o tile fica
        // sem TileObjectData (foi assim que o SiblingPylon ficou invisivel 1x1).
        static bool[] interactibleByNpcs = Array.Empty<bool>();

        static bool[] preventsSandfall = Array.Empty<bool>();

        static bool[] countsAsHoneySource = Array.Empty<bool>();

        static bool[] countsAsLavaSource = Array.Empty<bool>();

        static bool[] countsAsWaterSource = Array.Empty<bool>();

        public static bool[] InteractibleByNPCs => Grow(ref interactibleByNpcs);

        public static bool[] PreventsSandfall => Grow(ref preventsSandfall);

        public static bool[] CountsAsHoneySource => Grow(ref countsAsHoneySource);

        public static bool[] CountsAsLavaSource => Grow(ref countsAsLavaSource);

        public static bool[] CountsAsWaterSource => Grow(ref countsAsWaterSource);

        static int[] GrowInt(ref int[] set)
        {
            int count = Math.Max(TileLoader.TileCount, Main.tileFrameImportant.Length);
            lock (Gate)
            {
                if (set.Length >= count) return set;
                int old = set.Length;
                Array.Resize(ref set, count);
                Array.Fill(set, -1, old, count - old);
                return set;
            }
        }
    }
}
