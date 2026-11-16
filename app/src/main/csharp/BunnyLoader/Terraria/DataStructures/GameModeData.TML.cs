namespace Terraria.DataStructures;

/// <summary>
/// Visão mínima de <c>GameModeData</c> (o dump do Android não traz o tipo).
/// <c>Main.GameMode</c> neste shim é um <c>int</c>: 0 normal, 1 expert,
/// 2 master, 3 creative(journey).
/// </summary>
public sealed class GameModeData
{
    public const int NormalModeValue = 0;

    public const int ExpertModeValue = 1;

    public const int MasterModeValue = 2;

    public const int JourneyModeValue = 3;

    public GameModeData(int gameMode)
    {
        GameMode = gameMode;
    }

    public int GameMode { get; }

    public bool IsJourneyMode => GameMode == JourneyModeValue;

    public bool IsExpertMode => GameMode == ExpertModeValue || GameMode == MasterModeValue;

    public bool IsMasterMode => GameMode == MasterModeValue;

    public bool IsNormalMode => GameMode == NormalModeValue;

    public float DebuffTimeMultiplier => 1f;

    public float NPCDamageMultiplier => IsMasterMode ? 3f : (IsExpertMode ? 2f : 1f);
}
