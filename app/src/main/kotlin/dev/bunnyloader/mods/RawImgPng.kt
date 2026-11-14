package dev.bunnyloader.mods

import java.io.ByteArrayOutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.CRC32
import java.util.zip.Deflater

/**
 * Converte o `.rawimg` do tModLoader 1.4.5 em um PNG comum.
 *
 * O tML não guarda PNG de textura no `.tmod`: no build,
 * `Core/ContentConverters.Convert` chama `IO/ImageIO.ToRaw` e troca a extensão.
 * Para ler de volta ele tem `ImageIO.RawToPng` e o `RawImgReader`. Aqui não
 * existe FNA3D, então fazemos a mesma coisa com um escritor de PNG em Java
 * puro — sem `android.graphics.Bitmap`, o que também deixa testar na JVM.
 *
 * Formato (little-endian, `ImageIO.ReadRaw`):
 * ```
 * int32 version = 1
 * int32 width
 * int32 height
 * byte[width*height*4]  RGBA, linha por linha, sem padding
 * ```
 * O `ToRaw` do tML zera os pixels com alpha 0; copiar os bytes já preserva
 * isso. O PNG sai com color type 6 (RGBA8), filtro 0 em todas as linhas.
 */
object RawImgPng {
    private const val VERSION = 1
    private const val MAX_SIDE = 16384

    private val SIGNATURE = byteArrayOf(
        0x89.toByte(), 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    )

    /** `null` quando o arquivo não é um rawimg válido. */
    fun convert(raw: ByteArray): ByteArray? {
        if (raw.size < 12) return null
        val header = ByteBuffer.wrap(raw).order(ByteOrder.LITTLE_ENDIAN)
        if (header.int != VERSION) return null
        val width = header.int
        val height = header.int
        if (width <= 0 || height <= 0 || width > MAX_SIDE || height > MAX_SIDE) return null
        val rowBytes = width * 4
        val pixels = 12L + rowBytes.toLong() * height.toLong()
        if (pixels > raw.size.toLong()) return null
        if (pixels > Int.MAX_VALUE.toLong()) return null

        // Cada scanline do PNG começa com 1 byte de filtro (0 = None).
        val scanlines = ByteArray(height * (rowBytes + 1))
        var src = 12
        for (y in 0 until height) {
            val dst = y * (rowBytes + 1)
            scanlines[dst] = 0
            System.arraycopy(raw, src, scanlines, dst + 1, rowBytes)
            src += rowBytes
        }

        val out = ByteArrayOutputStream(1 shl 16)
        out.write(SIGNATURE)
        val ihdr = ByteBuffer.allocate(13).order(ByteOrder.BIG_ENDIAN)
        ihdr.putInt(width)
        ihdr.putInt(height)
        ihdr.put(8.toByte())   // bit depth
        ihdr.put(6.toByte())   // color type: RGBA
        ihdr.put(0.toByte())   // compression
        ihdr.put(0.toByte())   // filter
        ihdr.put(0.toByte())   // interlace
        chunk(out, "IHDR", ihdr.array())
        chunk(out, "IDAT", deflate(scanlines))
        chunk(out, "IEND", ByteArray(0))
        return out.toByteArray()
    }

    private fun deflate(data: ByteArray): ByteArray {
        val deflater = Deflater(Deflater.DEFAULT_COMPRESSION)
        try {
            deflater.setInput(data)
            deflater.finish()
            val buffer = ByteArray(64 * 1024)
            val out = ByteArrayOutputStream(data.size / 2)
            while (!deflater.finished()) {
                val n = deflater.deflate(buffer)
                if (n == 0) break
                out.write(buffer, 0, n)
            }
            return out.toByteArray()
        } finally {
            deflater.end()
        }
    }

    private fun chunk(out: ByteArrayOutputStream, type: String, data: ByteArray) {
        val name = type.toByteArray(Charsets.US_ASCII)
        writeInt(out, data.size)
        out.write(name)
        out.write(data)
        val crc = CRC32()
        crc.update(name)
        crc.update(data)
        writeInt(out, crc.value.toInt())
    }

    private fun writeInt(out: ByteArrayOutputStream, value: Int) {
        out.write((value ushr 24) and 0xFF)
        out.write((value ushr 16) and 0xFF)
        out.write((value ushr 8) and 0xFF)
        out.write(value and 0xFF)
    }
}
