package dev.bunnyloader.ui

import android.os.Build
import androidx.compose.material3.ColorScheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.dynamicDarkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * O tema do launcher: Material 3 com as cores do sistema quando existem.
 *
 * Android 12 (API 31) trouxe a paleta dinâmica: o sistema entrega um esquema
 * tirado do papel de parede e o app só usa. Antes disso não há de onde tirar
 * cor do usuário, então vai a paleta fixa do Bunny.
 *
 * A interface inteira (as ~300 chamadas de `Bl.Panel`, `PixelCard`, …) não
 * conhece nada disso: o esquema é escrito no `Bl` uma vez, aqui, e todo mundo
 * continua lendo os mesmos nomes de antes.
 */
private val BunnyShapes = Shapes(
    extraSmall = androidx.compose.foundation.shape.RoundedCornerShape(6.dp),
    small = androidx.compose.foundation.shape.RoundedCornerShape(10.dp),
    medium = androidx.compose.foundation.shape.RoundedCornerShape(16.dp),
    large = androidx.compose.foundation.shape.RoundedCornerShape(22.dp),
    extraLarge = androidx.compose.foundation.shape.RoundedCornerShape(28.dp),
)

private fun bunnyTypography(): Typography {
    // A fonte do sistema já tem os pesos certos; aqui só o título ganha um
    // pouco mais de corpo, porque o launcher é lido de longe (celular deitado).
    val base = Typography()
    fun TextStyle.bold(weight: FontWeight = FontWeight.SemiBold) = copy(fontWeight = weight)
    return Typography(
        titleLarge = base.titleLarge.bold().copy(fontSize = 22.sp),
        titleMedium = base.titleMedium.bold(),
        titleSmall = base.titleSmall.bold(),
        bodyLarge = base.bodyLarge,
        bodyMedium = base.bodyMedium,
        bodySmall = base.bodySmall,
        labelLarge = base.labelLarge.bold(FontWeight.Medium),
        labelMedium = base.labelMedium,
        labelSmall = base.labelSmall,
    )
}

/** Paleta fixa do Bunny: roxo/verde dos painéis do jogo, para Android 8..11. */
private val BunnyDark = darkColorScheme(
    primary = Color(0xFFBAC3FF),
    onPrimary = Color(0xFF16277B),
    primaryContainer = Color(0xFF3143AC),
    onPrimaryContainer = Color(0xFFDEE0FF),
    secondary = Color(0xFF9BD6B2),
    onSecondary = Color(0xFF00391F),
    secondaryContainer = Color(0xFF14512F),
    onSecondaryContainer = Color(0xFFB6F2CD),
    tertiary = Color(0xFFFFB870),
    onTertiary = Color(0xFF4A2800),
    tertiaryContainer = Color(0xFF653D00),
    onTertiaryContainer = Color(0xFFFFDCBE),
    background = Color(0xFF121318),
    onBackground = Color(0xFFE4E1E9),
    surface = Color(0xFF121318),
    onSurface = Color(0xFFE4E1E9),
    surfaceVariant = Color(0xFF45464F),
    onSurfaceVariant = Color(0xFFC6C5D0),
    surfaceContainerLowest = Color(0xFF0D0E13),
    surfaceContainerLow = Color(0xFF1A1B21),
    surfaceContainer = Color(0xFF1E1F26),
    surfaceContainerHigh = Color(0xFF282A31),
    surfaceContainerHighest = Color(0xFF33343C),
    outline = Color(0xFF90909A),
    outlineVariant = Color(0xFF45464F),
    error = Color(0xFFFFB4AB),
    onError = Color(0xFF690005),
)

@Composable
fun BunnyTheme(content: @Composable () -> Unit) {
    // Sempre no escuro: os ícones e as molduras herdados do jogo foram
    // desenhados para fundo escuro. O que muda com a versão do Android é a
    // origem das cores, não o brilho.
    val context = LocalContext.current
    val scheme: ColorScheme = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
        dynamicDarkColorScheme(context)
    } else {
        BunnyDark
    }
    RememberBlScheme(scheme)
    MaterialTheme(colorScheme = scheme, shapes = BunnyShapes, typography = bunnyTypography(), content = content)
}

/**
 * Escreve o esquema do Material no `Bl`, que é o vocabulário da interface
 * inteira. É o único ponto de contato entre os dois mundos.
 */
@Composable
private fun RememberBlScheme(scheme: ColorScheme) {
    // SideEffect: o esquema é escrito ao fim da composição, no mesmo quadro.
    // Com LaunchedEffect a primeira tela aparecia com as cores antigas.
    SideEffect {
        Bl.applyMaterial(
            MaterialColors(
                text = scheme.onSurface,
                textDim = scheme.onSurfaceVariant,
                textFaint = scheme.onSurfaceVariant.copy(alpha = 0.78f),
                textMuted = scheme.onSurfaceVariant.copy(alpha = 0.6f),
                bad = scheme.error,
                good = scheme.tertiary,
                outline = scheme.outlineVariant,
                night = scheme.surfaceContainerLowest,
                pressedBorder = scheme.primary,
                pressedText = scheme.primary,
                dragBorder = scheme.secondary,
                slotGold = scheme.secondaryContainer,
                slotGoldOutline = scheme.outline,
                enabledPanel = scheme.secondaryContainer.copy(alpha = 0.55f),
                newPanel = scheme.tertiaryContainer.copy(alpha = 0.55f),
                newTitleFill = scheme.tertiaryContainer,
                discord = Color(0xFF5865F2),
            ),
            UiPalette(
                panel = scheme.surfaceContainer,
                button = scheme.primaryContainer,
                buttonPressed = scheme.primary,
                select = scheme.surfaceContainerHigh,
                titleFill = scheme.secondaryContainer,
                gamePanel = scheme.surfaceContainerHigh,
                frameBorder = scheme.outlineVariant,
                frameFill = scheme.surfaceContainerLowest,
                disabledPanel = scheme.surfaceContainerLow,
            ),
        )
    }
}
