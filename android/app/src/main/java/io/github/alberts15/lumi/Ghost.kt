package io.github.alberts15.lumi

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color

val Teal = Color(93, 202, 165)
private val Eye = Color(0xFF1A1A1A)

/** Lumi, the glowing ghost, on the same 14-pixel grid as the desktop versions: B body, E eyes and mouth. */
private val ROWS = listOf(
    "....BBBBBB....",
    "..BBBBBBBBBB..",
    ".BBBBBBBBBBBB.",
    ".BBBBBBBBBBBB.",
    "BBBEEBBBBEEBBB",
    "BBBEEBBBBEEBBB",
    "BBBBBBBBBBBBBB",
    "BBBBBBEEBBBBBB",
    "BBBBBBBBBBBBBB",
    "BBBBBBBBBBBBBB",
)
private val HEMS = listOf("BB..BBBBBB..BB", "..BBBB..BBBB..")

/** Floats in place; while Lumi is answering it bobs twice as fast. */
@Composable
fun Ghost(modifier: Modifier, busy: Boolean = false) {
    val transition = rememberInfiniteTransition(label = "ghost")
    val phase by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(tween(if (busy) 600 else 1200, easing = LinearEasing)),
        label = "phase",
    )
    val step = if (phase < 0.5f) 0 else 1
    Canvas(modifier) {
        drawCircle(
            Brush.radialGradient(listOf(Teal.copy(alpha = 0.35f), Color.Transparent)),
            radius = size.minDimension * 0.75f,
        )
        val cell = minOf(size.width / 16f, size.height / 14f)
        val left = (size.width - cell * 14) / 2
        val top = (size.height - cell * 12) / 2
        val lift = if (step == 1) 0 else 1
        (ROWS + HEMS[step]).forEachIndexed { y, row ->
            row.forEachIndexed { x, ch ->
                if (ch != '.') {
                    drawRect(
                        if (ch == 'E') Eye else Teal,
                        Offset(left + x * cell, top + (y + lift) * cell),
                        Size(cell + 0.5f, cell + 0.5f),
                    )
                }
            }
        }
    }
}
