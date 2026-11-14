package dev.bunnyloader.mods

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.CRC32
import java.util.zip.Inflater

/**
 * Conferência do leitor do container `TMOD` e do `.rawimg` contra um `.tmod`
 * de verdade. Fica de fora do build entregue: aponta para um arquivo do
 * aparelho de quem desenvolve.
 */
class TmodArchiveTest {

    private val sample = File("D:/User/Documents/My Games/Terraria/tModLoader/Mods/超标怪缓冲.tmod")

    @Test
    fun leOContainerTmod() {
        if (!sample.isFile) return
        ModPackages.open(sample).use { pkg ->
            assertEquals(true, ModPackages.isTmodContainer(sample))
            assertEquals("mods", pkg.containerName)

            val info = pkg.read("Info")!!
            val fields = ModInfo.read(info)
            assertEquals("jiaogege", fields["displayname"])
            assertEquals("jiabanfei", fields["author"])
            assertEquals("0.1", fields["version"])

            val dll = pkg.read("mods.dll")!!
            assertEquals(0x4D, dll[0].toInt() and 0xFF) // "MZ"
            assertEquals(0x5A, dll[1].toInt() and 0xFF)
            assertEquals(3584, dll.size)

            val icon = pkg.read("icon.png")!!
            assertEquals(0x89.toByte(), icon[0])
            assertEquals(504, icon.size)

            // Deflate cru: o `.rawimg` é o único grande o bastante para valer
            // a compressão.
            val raw = pkg.read("HuanChong152.rawimg")!!
            assertEquals(1638412, raw.size)
            val png = RawImgPng.convert(raw)!!
            assertEquals(0x89.toByte(), png[0])
            assertEquals("PNG", String(png, 1, 3, Charsets.US_ASCII))
        }
    }

    @Test
    fun extraiEConverteOInfoParaTexto() {
        if (!sample.isFile) return
        val target = File(System.getProperty("java.io.tmpdir"), "tmod-extract-test")
        val count = ModPackages.open(sample).use { it.extractTo(target) }
        assertEquals(9, count)

        val text = File(target, "Info").readText()
        assertTrue(text, text.contains("displayname = jiaogege"))
        assertTrue(text, text.contains("name = mods"))
        assertTrue(text, text.contains("version = 0.1"))
        assertTrue(File(target, "Info.original").isFile)
        assertTrue(File(target, "mods.dll").length() == 3584L)

        // O `.rawimg` do tML 1.4.5 tem de virar PNG legível: é o que o loader
        // de textura procura, e foi exatamente o que faltou no relatório.
        val pngFile = File(target, "HuanChong152.png")
        assertTrue("HuanChong152.png não foi gerado", pngFile.isFile)
        assertTrue(!File(target, "HuanChong152.rawimg").exists())
        val (w, h, scan) = decodePng(pngFile.readBytes())
        assertEquals(640, w)
        assertEquals(640, h)
        // Linha 0: byte de filtro 0, depois RGBA do primeiro pixel.
        assertEquals(0, scan[0].toInt())
        assertEquals(0xE5, scan[1].toInt() and 0xFF)
        assertEquals(0xE1, scan[2].toInt() and 0xFF)
        assertEquals(0xE2, scan[3].toInt() and 0xFF)
        assertEquals(0xFF, scan[4].toInt() and 0xFF)
        target.deleteRecursively()
    }

    /**
     * A linha ESTÁVEL 1.4.4.9 tem a chave `libraryMod` (10 letras = 0x0A),
     * que a 1.4.5 de teste não tem. Um `Info` que começa por ela tem 0x0A no
     * byte zero — exatamente o que a heurística antiga confundia com "\n" de
     * arquivo texto. Se isso quebrar, um mod de biblioteca perde nome, autor,
     * versão e E TODAS as referências.
     */
    @Test
    fun infoDaLinhaEstavel1449ComecaPorLibraryMod() {
        val out = java.io.ByteArrayOutputStream()
        fun tag(s: String) {
            val b = s.toByteArray(Charsets.UTF_8)
            out.write(b.size) // chaves curtas: 7 bits cabem num byte
            out.write(b)
        }
        tag("libraryMod")          // flag, sem valor
        tag("author"); tag("Fabsol")
        tag("version"); tag("2.0.4")
        tag("displayName"); tag("Calamity")
        tag("side"); out.write(2) // byte cru: ModSide.Server
        tag("buildVersion"); tag("1.4.4.9")
        tag("")

        val fields = ModInfo.read(out.toByteArray())
        assertEquals("Fabsol", fields["author"])
        assertEquals("2.0.4", fields["version"])
        assertEquals("Calamity", fields["displayname"])
        assertEquals("Server", fields["side"])
        assertEquals("1.4.4.9", fields["buildversion"])

        // Texto puro (1.4.3 e build.txt exportado) continua pela outra via.
        val text = ModInfo.read("author = Re-Logic\nversion = 1.4.4.9\n".toByteArray())
        assertEquals("Re-Logic", text["author"])
        assertEquals("1.4.4.9", text["version"])
    }

    @Test
    fun converteRawImgSintetico() {
        // 2x1: vermelho opaco + azul translúcido.
        val raw = ByteArray(12 + 8).also {
            ByteBuffer.wrap(it).order(ByteOrder.LITTLE_ENDIAN).putInt(1).putInt(2).putInt(1)
            it[12] = 0xFF.toByte(); it[13] = 0; it[14] = 0; it[15] = 0xFF.toByte()
            it[16] = 0; it[17] = 0; it[18] = 0xFF.toByte(); it[19] = 0x80.toByte()
        }
        val (w, h, scan) = decodePng(RawImgPng.convert(raw)!!)
        assertEquals(2, w)
        assertEquals(1, h)
        assertEquals(0, scan[0].toInt())
        assertEquals(0xFF, scan[1].toInt() and 0xFF) // R
        assertEquals(0, scan[2].toInt())             // G
        assertEquals(0, scan[3].toInt())             // B
        assertEquals(0xFF, scan[4].toInt() and 0xFF) // A
        assertEquals(0, scan[5].toInt())             // R
        assertEquals(0, scan[6].toInt())             // G
        assertEquals(0xFF, scan[7].toInt() and 0xFF) // B
        assertEquals(0x80, scan[8].toInt() and 0xFF) // A
        assertEquals(null, RawImgPng.convert(ByteArray(4)))
    }

    /**
     * Lê um PNG sem `javax.imageio` (o classpath Android não tem AWT):
     * percorre os chunks, confere o CRC e infla o IDAT. Devolve largura,
     * altura e as scanlines já descomprimidas.
     */
    private fun decodePng(png: ByteArray): Triple<Int, Int, ByteArray> {
        assertEquals(0x89.toByte(), png[0])
        assertEquals("PNG", String(png, 1, 3, Charsets.US_ASCII))
        var pos = 8
        var w = 0
        var h = 0
        val idat = ByteArrayOutputStream()
        while (pos + 12 <= png.size) {
            val len = ByteBuffer.wrap(png, pos, 4).order(ByteOrder.BIG_ENDIAN).int
            val type = String(png, pos + 4, 4, Charsets.US_ASCII)
            val data = png.copyOfRange(pos + 8, pos + 8 + len)
            val crc = CRC32().apply {
                update(png, pos + 4, 4)
                update(data)
            }.value.toInt()
            val stored = ByteBuffer.wrap(png, pos + 8 + len, 4).order(ByteOrder.BIG_ENDIAN).int
            assertEquals("CRC do chunk $type", crc, stored)
            if (type == "IHDR") {
                val bb = ByteBuffer.wrap(data).order(ByteOrder.BIG_ENDIAN)
                w = bb.int
                h = bb.int
                assertEquals(8, bb.get().toInt()) // bit depth
                assertEquals(6, bb.get().toInt()) // color type: RGBA
            } else if (type == "IDAT") {
                idat.write(data)
            }
            pos += 12 + len
            if (type == "IEND") break
        }
        val inflater = Inflater()
        val scan = ByteArrayOutputStream()
        try {
            inflater.setInput(idat.toByteArray())
            val buf = ByteArray(64 * 1024)
            while (!inflater.finished()) {
                val n = inflater.inflate(buf)
                if (n == 0) break
                scan.write(buf, 0, n)
            }
        } finally {
            inflater.end()
        }
        return Triple(w, h, scan.toByteArray())
    }
}
