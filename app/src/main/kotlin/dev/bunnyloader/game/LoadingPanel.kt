package dev.bunnyloader.game

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.view.View
import kotlin.math.abs
import kotlin.math.min

/**
 * Painel de carregamento no estilo do tModLoader de PC: uma caixa
 * semi-transparente azul no meio da tela, uma linha de texto com a etapa e
 * uma barra amarela.
 *
 * Antes disso o progresso era uma faixa colada no topo, que cobria o menu e
 * nao dizia em que ponto do carregamento o jogo estava. O painel fica no
 * centro, escurece o jogo so um pouco (o mundo continua visivel atras) e e
 * lido a distancia de um braco, como a tela do PC.
 *
 * O valor vem de `dotnet/tml-progress.txt` (`porcentagem|texto`). O jogo
 * escreve por etapa; aqui o numero anda com suavizacao para a barra nao
 * saltar de 40 para 75 de um quadro para o outro.
 */
class LoadingPanel(context: Context) : View(context) {

    private val d = resources.displayMetrics.density
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val panel = RectF()
    private val bar = RectF()
    private val fill = RectF()

    private var shown = 0f
    private var pulse = 0f
    private var attached = false

    var title: String = ""
        set(value) {
            if (field == value) return
            field = value
            invalidate()
        }

    /** Alvo 0..1. Zero significa "ainda sem numero": a barra pulsa. */
    var target: Float = 0f
        set(value) {
            field = value.coerceIn(0f, 1f)
            invalidate()
        }

    private val ticker = object : Runnable {
        override fun run() {
            if (!attached) return
            val delta = target - shown
            shown += if (abs(delta) < 0.002f) delta else delta * 0.16f
            pulse = (pulse + 0.012f) % 1f
            invalidate()
            postOnAnimation(this)
        }
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        attached = true
        postOnAnimation(ticker)
    }

    override fun onDetachedFromWindow() {
        attached = false
        removeCallbacks(ticker)
        super.onDetachedFromWindow()
    }

    override fun onDraw(canvas: Canvas) {
        val w = width.toFloat()
        val h = height.toFloat()
        if (w <= 0f || h <= 0f) return

        // Escurece de leve: o painel precisa de contraste, mas o jogador tem
        // que continuar vendo que o jogo esta vivo atras.
        canvas.drawColor(Color.argb(64, 0, 0, 0))

        val panelW = min(w * 0.84f, 460f * d)
        val panelH = 92f * d
        val left = (w - panelW) / 2f
        val top = h * 0.40f
        panel.set(left, top, left + panelW, top + panelH)

        paint.style = Paint.Style.FILL
        paint.color = Color.argb(200, 63, 82, 151)
        canvas.drawRoundRect(panel, 10f * d, 10f * d, paint)

        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2f * d
        paint.color = Color.argb(210, 22, 32, 72)
        canvas.drawRoundRect(panel, 10f * d, 10f * d, paint)

        paint.style = Paint.Style.FILL
        paint.color = Color.WHITE
        paint.textSize = 15f * d
        paint.textAlign = Paint.Align.CENTER
        paint.typeface = Typeface.DEFAULT
        paint.setShadowLayer(2f * d, 0f, 1f * d, Color.argb(190, 0, 0, 0))
        canvas.drawText(title, panel.centerX(), panel.top + 33f * d, paint)
        paint.clearShadowLayer()

        val pad = 18f * d
        bar.set(panel.left + pad, panel.top + 56f * d, panel.right - pad, panel.top + 66f * d)

        paint.color = Color.rgb(20, 34, 92)
        canvas.drawRoundRect(bar, 3f * d, 3f * d, paint)

        // Sem numero ainda: uma faixa clara correndo, para nao parecer travado.
        val value = if (target > 0f) shown else pulse
        if (value > 0.001f) {
            fill.set(bar)
            fill.right = bar.left + bar.width() * value
            paint.color = Color.rgb(230, 214, 75)
            canvas.drawRoundRect(fill, 3f * d, 3f * d, paint)
        }
    }
}
