package dev.bunnyloader.ui

import android.graphics.BitmapFactory
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clipToBounds
import androidx.compose.ui.graphics.ImageBitmap
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import dev.bunnyloader.mods.ImportLog
import dev.bunnyloader.mods.TmlMod
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * O ícone de um `.tmod`: já saiu do zip na importação, então aqui é só um
 * arquivo. Mod sem `icon.png` fica com a inicial do nome no lugar.
 */
@Composable
fun TmlIcon(mod: TmlMod, size: Dp) {
    val bitmap = produceState<ImageBitmap?>(null, mod.iconFile) {
        value = withContext(Dispatchers.IO) {
            runCatching {
                mod.iconFile?.let { BitmapFactory.decodeFile(it)?.asImageBitmap() }
            }.getOrNull()
        }
    }.value
    Box(Modifier.size(size).framePanel().padding(2.dp).clipToBounds(), contentAlignment = Alignment.Center) {
        if (bitmap != null) {
            Image(bitmap, contentDescription = null, modifier = Modifier.fillMaxSize(), contentScale = ContentScale.Fit)
        } else {
            PixelText(mod.label.take(1).uppercase(), size = Ts.Big, color = Bl.TextFaint)
        }
    }
}

/** Uma linha da lista de TML: ícone, nome, autor/versão/tamanho e o interruptor. */
@Composable
fun TmlModRow(
    mod: TmlMod,
    enabled: Boolean,
    onClick: () -> Unit,
    onToggle: (Boolean) -> Unit,
    modifier: Modifier = Modifier,
    /** Todos os mods instalados: para dizer qual dependência falta. */
    installed: Set<String> = emptySet(),
    /** Os ligados: dependência instalada mas desligada continua bloqueando. */
    enabledSet: Set<String> = emptySet(),
) {
    val fill = if (enabled) Bl.EnabledPanel else Bl.DisabledPanel
    PixelCard(modifier.fillMaxWidth(), fill = fill, onClick = onClick) {
        Row(
            Modifier.padding(10.dp).fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TmlIcon(mod, 64.dp)
            Column(Modifier.weight(1f).padding(horizontal = 10.dp)) {
                PixelText(
                    mod.label, size = Ts.Item, color = Bl.Text,
                    maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                PixelText(
                    tr("ByAuthor", mod.author.ifBlank { tr("UnknownAuthor") }),
                    size = Ts.Small, color = Bl.TextFaint,
                    maxLines = 1, overflow = TextOverflow.Ellipsis,
                )
                Row(Modifier.padding(top = 5.dp), verticalAlignment = Alignment.CenterVertically) {
                    PixelTag(if (mod.version.isBlank()) "TML" else "v${mod.version}", Bl.TextDim)
                    PixelText(
                        mod.sizeLabel, size = Ts.Small, color = Bl.TextFaint,
                        modifier = Modifier.padding(start = 12.dp),
                    )
                    if (!mod.clientSide) {
                        PixelTag(tr("TmlServerOnly"), Bl.Bad, Modifier.padding(start = 8.dp))
                    }
                }
                // Dependências na própria linha: quem olha a lista precisa
                // saber que o mod sozinho não vai subir. Vermelho = não
                // instalado; apagado = instalado mas desligado.
                if (mod.modReferences.isNotEmpty()) {
                    Row(
                        Modifier.padding(top = 5.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        PixelText(tr("TmlDependsOn"), size = Ts.Small, color = Bl.TextFaint)
                        mod.modReferences.forEachIndexed { index, ref ->
                            val missing = installed.isNotEmpty() && ref !in installed
                            val off = !missing && enabledSet.isNotEmpty() && ref !in enabledSet
                            PixelText(
                                (if (index == 0) " " else ", ") + ref,
                                size = Ts.Small,
                                color = when {
                                    missing -> Bl.Bad
                                    off -> Bl.TextFaint
                                    else -> Bl.TextDim
                                },
                                maxLines = 1,
                                overflow = TextOverflow.Ellipsis,
                            )
                        }
                    }
                }
            }
            SwitchSprite(enabled) { onToggle(!enabled) }
        }
    }
}

/**
 * A ficha de um `.tmod`: o que o Info diz, e nada além.
 *
 * Não há changelog nem páginas: o `.tmod` não carrega nada disso, e inventar
 * uma aba vazia só daria trabalho a quem abriu.
 */
@Composable
fun TmlModDetail(mod: TmlMod, enabled: Boolean, onBack: () -> Unit, onToggle: (Boolean) -> Unit) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Row(
            Modifier.fillMaxWidth().padding(start = EdgePad, end = EdgePad, top = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            PixelButton(tr("Back"), onBack, fontSize = Ts.Small)
        }
        Row(
            Modifier.fillMaxWidth().padding(horizontal = EdgePad, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TmlIcon(mod, 96.dp)
            Column(Modifier.weight(1f).padding(start = 14.dp)) {
                PixelText(mod.label, size = Ts.Big, color = Bl.Text, maxLines = 2, overflow = TextOverflow.Ellipsis)
                PixelText(
                    tr("ByAuthor", mod.author.ifBlank { tr("UnknownAuthor") }),
                    size = Ts.Small, color = Bl.TextDim,
                    modifier = Modifier.padding(top = 4.dp),
                )
                if (mod.version.isNotBlank()) {
                    PixelText("v${mod.version}", size = Ts.Small, color = Bl.TextFaint,
                        modifier = Modifier.padding(top = 2.dp))
                }
            }
        }
        Row(
            Modifier.fillMaxWidth().padding(horizontal = EdgePad),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            PixelText(
                if (enabled) tr("TmlEnabledHint") else tr("TmlDisabledHint"),
                size = Ts.Small, color = if (enabled) Bl.TextDim else Bl.TextFaint,
                modifier = Modifier.weight(1f),
            )
            SwitchSprite(enabled) { onToggle(!enabled) }
        }

        PixelText(tr("Description"), size = Ts.Body, color = Bl.Text,
            modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 18.dp))
        PixelText(
            mod.description.ifBlank { tr("TmlNoDescription") },
            size = Ts.Small, color = if (mod.description.isBlank()) Bl.TextFaint else Bl.TextDim,
            modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 6.dp),
        )

        if (mod.modReferences.isNotEmpty()) {
            PixelText(tr("TmlReferences"), size = Ts.Body, color = Bl.Text,
                modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 18.dp))
            for (ref in mod.modReferences) {
                PixelText("• $ref", size = Ts.Small, color = Bl.TextDim,
                    modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 4.dp))
            }
        }
        if (mod.weakReferences.isNotEmpty()) {
            PixelText(tr("TmlWeakReferences"), size = Ts.Body, color = Bl.Text,
                modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 18.dp))
            PixelText(mod.weakReferences.joinToString(", "), size = Ts.Small, color = Bl.TextFaint,
                modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 4.dp))
        }

        PixelText(tr("TmlFile"), size = Ts.Body, color = Bl.Text,
            modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 18.dp))
        for (line in listOfNotNull(
            "${mod.fileName} · ${mod.sizeLabel}",
            mod.entryDll?.let { "dll: $it" },
            tr("TmlResourceCount", mod.resourceCount),
            if (mod.side.isBlank()) null else "side: ${mod.side}",
        )) {
            PixelText(line, size = Ts.Small, color = Bl.TextFaint,
                modifier = Modifier.padding(start = EdgePad, end = EdgePad, top = 4.dp))
        }
        Box(Modifier.padding(bottom = 24.dp))
    }
}

/** A lista inteira, com o estado vazio explicando o que fazer. */
@Composable
fun TmlModList(    mods: List<TmlMod>,
    enabled: Set<String>,
    onOpen: (TmlMod) -> Unit,
    onToggle: (TmlMod, Boolean) -> Unit,
    modifier: Modifier = Modifier,
    empty: @Composable () -> Unit,
) {
    if (mods.isEmpty()) {
        Box(modifier.fillMaxSize(), contentAlignment = Alignment.Center) { empty() }
        return
    }
    androidx.compose.foundation.lazy.LazyColumn(
        modifier.fillMaxSize(),
        contentPadding = androidx.compose.foundation.layout.PaddingValues(
            start = EdgePad, end = EdgePad, top = 6.dp, bottom = 24.dp),
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        items(mods.size) { index ->
            val mod = mods[index]
            TmlModRow(
                mod = mod,
                enabled = mod.name in enabled,
                onClick = { onOpen(mod) },
                onToggle = { onToggle(mod, it) },
                installed = mods.mapTo(HashSet()) { it.name },
                enabledSet = enabled,
            )
        }
    }
}

/**
 * O diário dos imports, para ler e copiar.
 *
 * Fica aqui, e não no relatório de diagnóstico, porque a pergunta que ele
 * responde é outra: "por que ESTE arquivo não entrou?" — o relatório fala do
 * jogo que fechou, este fala do arquivo que o seletor devolveu.
 */
@Composable
fun ImportLogDialog(onDismiss: () -> Unit) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val clipboard = androidx.compose.ui.platform.LocalClipboardManager.current
    var text by remember { mutableStateOf("") }
    LaunchedEffect(Unit) { text = withContext(Dispatchers.IO) { ImportLog.read(context) } }
    PixelDialog(tr("ImportLog"), onDismiss = onDismiss) {
        Column(
            Modifier.fillMaxWidth().heightIn(max = 380.dp).verticalScroll(rememberScrollState()),
        ) {
            PixelText(
                if (text.isBlank()) tr("ImportLogEmpty") else text,
                size = Ts.Small,
                color = if (text.isBlank()) Bl.TextFaint else Bl.TextDim,
            )
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            PixelButton(tr("Clear"), {
                ImportLog.clear(context)
                text = ""
            }, Modifier.weight(1f), fontSize = Ts.Body)
            PixelButton(tr("Copy"), {
                if (text.isNotBlank()) clipboard.setText(AnnotatedString(text))
            }, Modifier.weight(1f), fill = Bl.TitleFill, fontSize = Ts.Body)
        }
        PixelButton(tr("Close"), onDismiss, Modifier.fillMaxWidth(), fontSize = Ts.Body)
    }
}
