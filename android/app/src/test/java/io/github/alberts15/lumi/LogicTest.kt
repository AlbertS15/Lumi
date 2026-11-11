package io.github.alberts15.lumi

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class LogicTest {
    @Test fun deltaReadsTextPiece() {
        assertEquals("Привет", Logic.delta("""data: {"choices":[{"delta":{"content":"Привет"}}]}"""))
    }

    @Test fun deltaSkipsNonData() {
        assertNull(Logic.delta(": keep-alive"))
        assertNull(Logic.delta("data: [DONE]"))
        assertNull(Logic.delta("data: {\"choices\":[]}"))
        assertNull(Logic.delta("""data: {"choices":[{"delta":{"role":"assistant"}}]}"""))
        assertNull(Logic.delta("""data: {"choices":[{"delta":{"content":null}}]}"""))
    }

    @Test(expected = ChatError::class) fun deltaThrowsOnStreamedError() {
        Logic.delta("""data: {"error":{"message":"quota"}}""")
    }

    @Test fun errorTextUsesServerMessage() {
        val text = Logic.errorText(401, """{"error":{"message":"Invalid API key"}}""")
        assertTrue(text, text.contains("Invalid API key") && text.contains("key"))
        assertTrue(Logic.errorText(500, "").contains("HTTP 500"))
        assertTrue(Logic.errorText(400, """[{"error":{"message":"Gemini says no"}}]""").contains("Gemini says no"))
    }

    @Test fun chatUrlHandlesSlash() {
        assertEquals("https://api.deepseek.com/chat/completions", Logic.chatUrl("https://api.deepseek.com/"))
        assertEquals("https://api.groq.com/openai/v1/chat/completions", Logic.chatUrl(" https://api.groq.com/openai/v1 "))
    }

    @Test fun yandexFolder() {
        assertEquals("b1gabc", Logic.yandexFolder("gpt://b1gabc/yandexgpt"))
        assertNull(Logic.yandexFolder("gpt-4o"))
    }

    @Test fun hostOfCustomAddress() {
        assertEquals("192.168.1.10", Logic.host("http://192.168.1.10:11434/v1"))
    }

    @Test fun requestBodyHasSystemFirst() {
        val body = org.json.JSONObject(Logic.requestBody("m", "sys", listOf(Msg("user", "hi"))))
        assertEquals("system", body.getJSONArray("messages").getJSONObject(0).getString("role"))
        assertEquals(true, body.getBoolean("stream"))
    }
}
