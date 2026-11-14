using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BunnyLoader.Sandbox;

/// <summary>
/// Converte o `.rawimg` do tModLoader 1.4.5/1.4.4.9 num PNG comum.
///
/// Mesmo formato de `IO/ImageIO.cs` (duas linhas do tML): int32 versao=1,
/// int32 largura, int32 altura, e RGBA cru. Aqui o PNG e escrito a mao com
/// ZLibStream + CRC32 para nao depender de System.Drawing (inexistente no
/// Android e desnecessario no PC). E a MESHA conversao que o Kotlin faz no
/// import (RawImgPng.kt), refeita em C# para o sandbox poder descompactar um
/// .tmod inteiro antes de rodar o loader.
/// </summary>
internal static class PngWriter
{
    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    static uint Crc(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static byte[]? FromRawImg(byte[] raw)
    {
        if (raw.Length < 12) return null;
        int version = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(0, 4));
        if (version != 1) return null;
        int w = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4, 4));
        int h = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8, 4));
        if (w <= 0 || h <= 0 || w > 16384 || h > 16384) return null;
        long need = 12L + (long)w * h * 4;
        if (need > raw.Length || need > int.MaxValue) return null;

        int row = w * 4;
        var scan = new byte[h * (row + 1)];
        int src = 12;
        for (int y = 0; y < h; y++)
        {
            int dst = y * (row + 1);
            scan[dst] = 0; // filtro None
            Array.Copy(raw, src, scan, dst + 1, row);
            src += row;
        }

        using var idat = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(scan, 0, scan.Length);
        byte[] compressed = idat.ToArray();

        using var output = new MemoryStream();
        output.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        Chunk(output, "IHDR", ihdr);
        Chunk(output, "IDAT", compressed);
        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        output.Write(len);
        byte[] name = Encoding.ASCII.GetBytes(type);
        output.Write(name);
        output.Write(data);
        var crcInput = new byte[name.Length + data.Length];
        name.CopyTo(crcInput, 0);
        data.CopyTo(crcInput, name.Length);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(crcInput));
        output.Write(crc);
    }
}
