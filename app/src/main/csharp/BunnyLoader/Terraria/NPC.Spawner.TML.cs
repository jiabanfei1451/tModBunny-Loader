using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace Terraria;

public sealed partial class NPC
{
    public sealed partial class Spawner
    {
        public int SpawnTileX => NPCSpawnHooks.InfoOf(this).SpawnTileX;
        public int SpawnTileY => NPCSpawnHooks.InfoOf(this).SpawnTileY;
        public int GroundTileY => NPCSpawnHooks.InfoOf(this).GroundTileY;
        public int SpawnTileType => NPCSpawnHooks.InfoOf(this).SpawnTileType;
        public int SpawnWallType => NPCSpawnHooks.InfoOf(this).SpawnWallType;
        public Player Player => NPCSpawnHooks.InfoOf(this).Player;
        public bool SafeRangeX => NPCSpawnHooks.InfoOf(this).SafeRangeX;
    }
}
