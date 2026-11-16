namespace Terraria;

/// <summary>
/// O dump do 1.4.5.8 do Android não traz <c>Terraria.Program</c>, mas o mod
/// (Luminance) referencia <c>Program.get_SavePathShared()</c>.
/// </summary>
public static class Program
{
    public static string SavePathShared => Main.SavePath;
}
