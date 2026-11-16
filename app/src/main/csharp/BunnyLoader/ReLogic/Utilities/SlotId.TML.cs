namespace ReLogic.Utilities;

/// <summary>
/// O <c>Invalid</c> do ReLogic: slot 0 quer dizer "nenhum som". O proxy gera
/// só o campo <c>Value</c>, então o <c>ldsfld SlotId::Invalid</c> dos mods
/// (Luminance usa em vários pontos do pipeline de shader) ficava sem alvo e
/// esvaziava o método inteiro por "campo sem traducao".
/// </summary>
public partial struct SlotId
{
    public static SlotId Invalid => new() { Value = 0 };
}
