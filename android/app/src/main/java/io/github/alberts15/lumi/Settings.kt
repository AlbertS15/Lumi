@file:OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)

package io.github.alberts15.lumi

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.Button
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import java.util.UUID

@Composable
fun SettingsScreen(
    store: Store,
    connections: List<Connection>,
    activeId: String?,
    onChange: (List<Connection>, String?) -> Unit,
    onDone: () -> Unit,
) {
    var preset by remember { mutableStateOf(Preset.OPENROUTER) }
    var baseUrl by remember { mutableStateOf(preset.baseUrl) }
    var model by remember { mutableStateOf(preset.model) }
    var key by remember { mutableStateOf("") }
    val links = LocalUriHandler.current

    Column(Modifier.padding(16.dp).verticalScroll(rememberScrollState())) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Ghost(Modifier.size(36.dp))
            Spacer(Modifier.size(8.dp))
            Text(stringResource(R.string.settings), style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f))
            if (connections.isNotEmpty()) TextButton(onClick = onDone) { Text(stringResource(R.string.done)) }
        }

        if (connections.isEmpty()) {
            Text(stringResource(R.string.welcome), modifier = Modifier.padding(vertical = 8.dp))
        } else {
            Text(stringResource(R.string.connections), fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 12.dp))
            connections.forEach { c ->
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    modifier = Modifier.fillMaxWidth().clickable { onChange(connections, c.id) },
                ) {
                    RadioButton(selected = c.id == activeId, onClick = { onChange(connections, c.id) })
                    Column(Modifier.weight(1f)) {
                        Text(c.title)
                        Text(c.model, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    IconButton(onClick = {
                        store.setKey(c.id, "")
                        val rest = connections.filter { it.id != c.id }
                        onChange(rest, if (activeId == c.id) rest.firstOrNull()?.id else activeId)
                    }) { Icon(Icons.Default.Delete, stringResource(R.string.remove)) }
                }
            }
        }

        Text(stringResource(R.string.add_connection), fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 16.dp, bottom = 4.dp))
        FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Preset.entries.forEach { p ->
                FilterChip(
                    selected = p == preset,
                    onClick = { preset = p; baseUrl = p.baseUrl; model = p.model },
                    label = { Text(label(p)) },
                )
            }
        }
        if (preset == Preset.OLLAMA) {
            Text(stringResource(R.string.ollama_hint), style = MaterialTheme.typography.bodySmall, modifier = Modifier.padding(vertical = 4.dp))
        }
        OutlinedTextField(baseUrl, { baseUrl = it }, label = { Text(stringResource(R.string.address)) },
            singleLine = true, modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri))
        OutlinedTextField(model, { model = it }, label = { Text(stringResource(R.string.model)) },
            singleLine = true, modifier = Modifier.fillMaxWidth())
        if (preset.needsKey) {
            OutlinedTextField(key, { key = it }, label = { Text(stringResource(R.string.api_key)) },
                singleLine = true, modifier = Modifier.fillMaxWidth(),
                visualTransformation = PasswordVisualTransformation(),
                keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password))
            if (preset.keyUrl.isNotEmpty()) {
                TextButton(onClick = { links.openUri(preset.keyUrl) }) { Text(stringResource(R.string.get_key, preset.title)) }
            }
        }
        Button(
            enabled = baseUrl.isNotBlank() && model.isNotBlank() && (key.isNotBlank() || !preset.needsKey),
            onClick = {
                val c = Connection(UUID.randomUUID().toString(), preset, baseUrl.trim(), model.trim())
                store.setKey(c.id, key)
                key = ""
                onChange(connections + c, c.id)
                onDone()
            },
            modifier = Modifier.padding(top = 8.dp),
        ) { Text(stringResource(R.string.save)) }
        Text(stringResource(R.string.privacy), style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 12.dp))
    }
}

@Composable
private fun label(p: Preset): String = when (p) {
    Preset.OLLAMA -> stringResource(R.string.preset_ollama)
    Preset.OTHER -> stringResource(R.string.preset_other)
    else -> p.title
}
