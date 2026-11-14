using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using Terraria.ModLoader.IO;

namespace Terraria.IO;

public sealed partial class WorldFileData
{
    // v65: tModLoader da um Guid estavel por arquivo de mundo; o codigo de
    // subworld usa como nome de pasta para o .map. Derivamos do caminho para
    // ser estavel entre execucoes (o jogo nao guarda esse id no .wld).
    System.Guid? uniqueId;

    public System.Guid UniqueId
    {
        get
        {
            if (uniqueId is System.Guid known) return known;
            string key = Path ?? Name ?? "";
            ulong hash = 0xCBF29CE484222325UL;
            foreach (char c in key) hash = (hash ^ c) * 0x100000001B3UL;
            var bytes = new byte[16];
            System.BitConverter.GetBytes(hash).CopyTo(bytes, 0);
            System.BitConverter.GetBytes((ulong)key.Length * 0x9E3779B97F4A7C15UL).CopyTo(bytes, 8);
            uniqueId = new System.Guid(bytes);
            return uniqueId.Value;
        }
        // o upstream tambem escreve (SubworldTravel.cs:369/408)
        set => uniqueId = value;
    }

    public bool TryGetHeaderData(ModSystem system, out TagCompound data) => SystemHooks.TryGetHeaderData(this, system, out data);

    public bool TryGetHeaderData<T>(out TagCompound data) where T : ModSystem => TryGetHeaderData(ModContent.GetInstance<T>(), out data);
}
