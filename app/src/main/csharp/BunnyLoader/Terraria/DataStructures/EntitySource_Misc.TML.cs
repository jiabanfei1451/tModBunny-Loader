namespace Terraria.DataStructures;

/// <summary>
/// Não existe no dump do Android: o mod só faz <c>isinst</c> + <c>get_Context</c>
/// sobre uma fonte que ele recebe pronto, então basta a FORMA (nunca é
/// instanciado por nós).
/// </summary>
public sealed class EntitySource_Misc
{
    public EntitySource_Misc(string? context = null)
    {
        Context = context;
    }

    public string? Context { get; }
}
