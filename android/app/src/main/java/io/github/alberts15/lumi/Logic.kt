package io.github.alberts15.lumi

import org.json.JSONArray
import org.json.JSONObject

class ChatError(message: String) : Exception(message)

data class Msg(val role: String, val text: String)

/** Rules without Android UI, covered by unit tests. */
object Logic {
    /** The text piece of one server-sent-events line of a streamed chat answer, or null. */
    fun delta(line: String): String? {
        if (!line.startsWith("data:")) return null
        val data = line.removePrefix("data:").trim()
        if (data.isEmpty() || data == "[DONE]") return null
        val json = try { JSONObject(data) } catch (e: Exception) { return null }
        json.optJSONObject("error")?.let { throw ChatError(it.optString("message", data)) }
        val choices = json.optJSONArray("choices") ?: return null
        if (choices.length() == 0) return null
        val delta = choices.getJSONObject(0).optJSONObject("delta") ?: return null
        return delta.opt("content") as? String
    }

    /** A readable message for a failed request: the server's own words when it gives them. */
    fun errorText(status: Int, body: String): String {
        val fromServer = try {
            val json = JSONObject(body)
            val err = json.opt("error")
            when (err) {
                is JSONObject -> err.optString("message")
                is String -> err
                else -> json.optString("message")
            }
        } catch (e: Exception) {
            try { (JSONArray(body).getJSONObject(0).optJSONObject("error"))?.optString("message") } catch (e: Exception) { null }
        }
        val hint = when (status) {
            401, 403 -> "key"
            404 -> "model"
            429 -> "limit"
            else -> ""
        }
        val text = fromServer?.takeIf { it.isNotBlank() } ?: body.take(300).ifBlank { "HTTP $status" }
        return if (hint.isEmpty()) "HTTP $status: $text" else "HTTP $status ($hint): $text"
    }

    /** The chat endpoint for a base address typed with or without a trailing slash. */
    fun chatUrl(baseUrl: String): String = baseUrl.trim().trimEnd('/') + "/chat/completions"

    /** Yandex reads the folder from "gpt://<folder>/<model>"; it goes in the OpenAI-Project header. */
    fun yandexFolder(model: String): String? =
        if (model.startsWith("gpt://")) model.removePrefix("gpt://").split("/").firstOrNull()?.takeIf { it.isNotEmpty() } else null

    fun host(url: String): String =
        url.substringAfter("://").substringBefore("/").substringBefore(":").ifEmpty { url }

    fun requestBody(model: String, system: String, messages: List<Msg>): String {
        val list = JSONArray()
        list.put(JSONObject().put("role", "system").put("content", system))
        for (m in messages) list.put(JSONObject().put("role", m.role).put("content", m.text))
        return JSONObject().put("model", model).put("messages", list).put("stream", true).toString()
    }
}
