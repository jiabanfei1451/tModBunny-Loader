using BunnyLoader;

namespace Terraria;

/// <summary>
/// O dump do 1.4.5.8 do Android não traz <c>Terraria.Program</c>, mas o mod
/// (Luminance) referencia <c>Program.get_SavePathShared()</c>.
///
/// v35: a linha de log aqui é de propósito. Ela é o primeiro marco do
/// <c>PostSetupContent</c> do Luminance (<c>ShaderRecompilationMonitor::LoadForMod</c>
/// faz <c>Path.Combine(Program.SavePathShared, "ModSources")</c> e sai cedo se a
/// pasta não existe). Antes de <c>Main.SavePath</c> existir, esse getter
/// estourava <c>GameException</c> e derrubava o passo antes de qualquer shader
/// ser registrado.
/// </summary>
public static class Program
{
    public static string SavePathShared
    {
        get
        {
            Log.Info("[fx] Program.SavePathShared pedido (Luminance: LoadForMod) -> " + Main.SavePath);
            return Main.SavePath;
        }
    }
}
