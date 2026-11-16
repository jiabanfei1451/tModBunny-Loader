using Microsoft.Xna.Framework;

namespace Terraria.DataStructures;

// v100: tipo do vanilla ausente no dump; os mods so' chamam FromUnknown.
// Campos seguem o contrato 1.4.4.9 (Position + CanVisualizeTransfers), com
// valores neutros porque nao ha estado nativo para ler.
public struct ContainerTransferContext
{
    public Vector2? Position;

    public bool CanVisualizeTransfers;

    public static ContainerTransferContext FromUnknown(Player player) => default;

    public static ContainerTransferContext FromPoint(Vector2 position) => new() { Position = position };

    public static ContainerTransferContext FromContainer(Item container) => default;
}
