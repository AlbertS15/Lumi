package io.github.alberts15.lumi

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import java.net.HttpURLConnection
import java.net.URL

const val SYSTEM_PROMPT = """You are Lumi, a friendly glowing pixel ghost and the assistant inside the Lumi app on the user's Android phone. The answer is read on a small screen: be brief and to the point, start with the answer itself. Always answer in the language of the user's message. Use plain text; short lists are fine, no tables. Never invent facts; if you are not sure, say so."""

object Chat {
    /** Streams the answer piece by piece. */
    fun stream(c: Connection, key: String, messages: List<Msg>): Flow<String> = flow {
        val conn = URL(Logic.chatUrl(c.baseUrl)).openConnection() as HttpURLConnection
        try {
            conn.requestMethod = "POST"
            conn.connectTimeout = 15_000
            conn.readTimeout = 120_000
            conn.doOutput = true
            conn.setRequestProperty("Content-Type", "application/json")
            conn.setRequestProperty("Accept", "text/event-stream")
            if (key.isNotBlank()) conn.setRequestProperty("Authorization", "Bearer ${key.trim()}")
            Logic.yandexFolder(c.model)?.let { conn.setRequestProperty("OpenAI-Project", it) }
            conn.outputStream.use { it.write(Logic.requestBody(c.model.trim(), SYSTEM_PROMPT, messages).toByteArray()) }

            val status = conn.responseCode
            if (status != 200) {
                val body = (conn.errorStream ?: conn.inputStream)?.bufferedReader()?.use { it.readText() } ?: ""
                throw ChatError(Logic.errorText(status, body))
            }
            conn.inputStream.bufferedReader().use { reader ->
                while (true) {
                    currentCoroutineContext().ensureActive()
                    val line = reader.readLine() ?: break
                    Logic.delta(line)?.let { if (it.isNotEmpty()) emit(it) }
                }
            }
        } finally {
            conn.disconnect()
        }
    }.flowOn(Dispatchers.IO)
}
