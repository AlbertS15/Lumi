package io.github.alberts15.lumi

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Send
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.MutableState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    /** Text handed over by "Ask Lumi" on a selection or by Share; it lands in the input. */
    private val incoming = mutableStateOf<String?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        incoming.value = sharedText(intent)
        setContent { LumiTheme { LumiScreen(Store(this), incoming, onClose = { finish() }) } }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        incoming.value = sharedText(intent)
    }

    private fun sharedText(intent: Intent?): String? = when (intent?.action) {
        Intent.ACTION_PROCESS_TEXT -> intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)?.toString()
        Intent.ACTION_SEND -> intent.getStringExtra(Intent.EXTRA_TEXT)
        else -> null
    }
}

@Composable
fun LumiTheme(content: @Composable () -> Unit) {
    val scheme = if (isSystemInDarkTheme()) {
        darkColorScheme(primary = Teal, surface = Color(0xFF202020), onPrimary = Color(0xFF0E2A20))
    } else {
        lightColorScheme(primary = Color(0xFF1F8F6B), surface = Color(0xFFF9F9F9))
    }
    MaterialTheme(colorScheme = scheme, content = content)
}

@Composable
fun LumiScreen(store: Store, incoming: MutableState<String?>, onClose: () -> Unit) {
    var connections by remember { mutableStateOf(store.connections()) }
    var activeId by remember { mutableStateOf(store.activeId) }
    var settings by remember { mutableStateOf(connections.isEmpty()) }
    val messages = remember { mutableStateListOf<Msg>() }
    var input by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    var job by remember { mutableStateOf<Job?>(null) }
    val scope = rememberCoroutineScope()
    val active = connections.firstOrNull { it.id == activeId } ?: connections.firstOrNull()

    LaunchedEffect(incoming.value) {
        incoming.value?.let { text ->
            input = if (input.isBlank()) "«${text.trim()}»\n" else input + "\n" + text
            incoming.value = null
        }
    }

    fun send() {
        val question = input.trim()
        val c = active ?: run { settings = true; return }
        if (question.isEmpty() || busy) return
        val history = messages.toList() + Msg("user", question)
        messages.add(Msg("user", question))
        messages.add(Msg("assistant", ""))
        val index = messages.lastIndex
        input = ""
        error = null
        busy = true
        job = scope.launch {
            try {
                Chat.stream(c, store.key(c.id), history).collect { piece ->
                    messages[index] = messages[index].copy(text = messages[index].text + piece)
                }
                if (messages[index].text.isBlank()) messages.removeAt(index)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                error = e.message ?: e.toString()
                if (messages[index].text.isBlank()) messages.removeAt(index)
            } finally {
                busy = false
            }
        }
    }

    val quiet = remember { MutableInteractionSource() }
    Box(
        Modifier
            .fillMaxSize()
            .background(Color.Black.copy(alpha = 0.45f))
            .clickable(interactionSource = quiet, indication = null) { onClose() }
            .statusBarsPadding()
            .navigationBarsPadding()
            .imePadding(),
    ) {
        val tall = settings || messages.isNotEmpty() || error != null
        Surface(
            shape = RoundedCornerShape(24.dp),
            tonalElevation = 2.dp,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .fillMaxWidth()
                .padding(10.dp)
                .then(if (tall) Modifier.fillMaxHeight(0.88f) else Modifier)
                .clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {},
        ) {
            if (settings) {
                SettingsScreen(
                    store = store,
                    connections = connections,
                    activeId = active?.id,
                    onChange = { list, id ->
                        connections = list
                        activeId = id
                        store.save(list)
                        store.activeId = id
                    },
                    onDone = { if (connections.isNotEmpty()) settings = false },
                )
                return@Surface
            }
            Column(Modifier.padding(14.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Ghost(Modifier.size(40.dp), busy)
                    Spacer(Modifier.size(8.dp))
                    Text("Lumi", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
                    Spacer(Modifier.size(8.dp))
                    active?.let { AssistChip(onClick = { settings = true }, label = { Text(it.title) }) }
                    Spacer(Modifier.weight(1f))
                    if (messages.isNotEmpty()) {
                        IconButton(onClick = { job?.cancel(); messages.clear(); error = null }) {
                            Icon(Icons.Default.Add, stringResource(R.string.new_chat))
                        }
                    }
                    IconButton(onClick = { settings = true }) { Icon(Icons.Default.Settings, stringResource(R.string.settings)) }
                }
                if (tall) {
                    Answers(messages, error, Modifier.weight(1f))
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedTextField(
                        value = input,
                        onValueChange = { input = it },
                        placeholder = { Text(stringResource(R.string.ask_placeholder)) },
                        modifier = Modifier.weight(1f).heightIn(max = 160.dp),
                        shape = RoundedCornerShape(18.dp),
                        keyboardOptions = KeyboardOptions(imeAction = ImeAction.Send),
                        keyboardActions = KeyboardActions(onSend = { send() }),
                    )
                    if (busy) {
                        IconButton(onClick = { job?.cancel(); busy = false }) { Icon(Icons.Default.Close, stringResource(R.string.stop)) }
                    } else {
                        @Suppress("DEPRECATION")
                        IconButton(onClick = { send() }, enabled = input.isNotBlank()) { Icon(Icons.Default.Send, stringResource(R.string.send)) }
                    }
                }
            }
        }
    }
}

@Composable
private fun Answers(messages: List<Msg>, error: String?, modifier: Modifier) {
    val list = rememberLazyListState()
    val clipboard = LocalClipboardManager.current
    LaunchedEffect(messages.size, messages.lastOrNull()?.text?.length, error) {
        val count = messages.size + if (error != null) 1 else 0
        if (count > 0) list.scrollToItem(count - 1)
    }
    LazyColumn(modifier.fillMaxWidth(), state = list, verticalArrangement = Arrangement.spacedBy(10.dp)) {
        itemsIndexed(messages) { _, m ->
            if (m.role == "user") {
                Text(
                    m.text,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.fillMaxWidth().padding(top = 8.dp),
                    fontWeight = FontWeight.Medium,
                )
            } else {
                Column {
                    SelectionContainer { Text(m.text.ifEmpty { "…" }, style = MaterialTheme.typography.bodyLarge) }
                    if (m.text.isNotEmpty()) {
                        TextButton(onClick = { clipboard.setText(AnnotatedString(m.text)) }) { Text(stringResource(R.string.copy)) }
                    }
                }
            }
        }
        if (error != null) {
            item { Text(error, color = MaterialTheme.colorScheme.error) }
        }
    }
}
