namespace Terraria.GameContent.Bestiary;

/// <summary>
/// O PC expoe o construtor <c>CommonEnemyUICollectionInfoProvider(string
/// persistentId, bool quickUnlock)</c>. O proxy so gera o construtor de
/// ponteiro mais a fabrica <see cref="New(string, bool)"/>, entao
/// <c>new CommonEnemyUICollectionInfoProvider(...)</c> no mod morria em
/// <c>MissingMethodException</c> (LesserSquirrel/LesserFairy ::SetBestiary).
/// </summary>
public unsafe sealed partial class CommonEnemyUICollectionInfoProvider
{
    public CommonEnemyUICollectionInfoProvider(string? persistentId, bool quickUnlock)
        : this(New(persistentId, quickUnlock).Pointer)
    {
    }
}
