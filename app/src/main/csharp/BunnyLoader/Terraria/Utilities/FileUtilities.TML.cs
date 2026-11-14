using System.IO;

namespace Terraria.Utilities;

// v65: a API FileUtilities do tModLoader (Terraria.Utilities). O jogo no Android
// nao expoe esse tipo no dump, mas o codigo de subworld so usa o basico de
// arquivo — implementar em cima do System.IO com a mesma assinatura (o segundo
// parametro e' "cloud", que aqui e' sempre local).
public static class FileUtilities
{
    public static bool Exists(string path, bool cloud = false) => File.Exists(path);

    public static void CreateDirectory(string path, bool cloud = false)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }

    public static void WriteAllBytes(string path, byte[] data, bool cloud = false) => File.WriteAllBytes(path, data);

    public static byte[] ReadAllBytes(string path, bool cloud = false) => File.ReadAllBytes(path);

    public static void Delete(string path, bool cloud = false)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public static void Copy(string source, string destination, bool cloud = false) => File.Copy(source, destination, true);

    public static void Move(string source, string destination, bool cloud = false) => File.Move(source, destination, true);
}
