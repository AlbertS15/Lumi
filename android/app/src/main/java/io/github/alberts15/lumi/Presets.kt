package io.github.alberts15.lumi

/** Ready-made services for the add-connection form; all of them speak the OpenAI chat format. */
enum class Preset(
    val title: String,
    val baseUrl: String,
    val model: String,
    val keyUrl: String,
) {
    OPENROUTER("OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4o-mini", "https://openrouter.ai/keys"),
    GEMINI("Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.1-flash-lite", "https://aistudio.google.com/apikey"),
    OPENAI("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", "https://platform.openai.com/api-keys"),
    DEEPSEEK("DeepSeek", "https://api.deepseek.com", "deepseek-chat", "https://platform.deepseek.com/api_keys"),
    GROQ("Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "https://console.groq.com/keys"),
    MISTRAL("Mistral", "https://api.mistral.ai/v1", "mistral-small-latest", "https://console.mistral.ai/api-keys"),
    YANDEX("YandexGPT", "https://ai.api.cloud.yandex.net/v1", "gpt://<folder>/yandexgpt", "https://yandex.cloud/ru/docs/foundation-models/quickstart/yandexgpt"),
    /** Ollama on the user's computer in the same Wi-Fi: http://<computer IP>:11434/v1. */
    OLLAMA("Ollama", "http://192.168.1.10:11434/v1", "lumi-mini", ""),
    OTHER("", "", "", "");

    val needsKey get() = this != OLLAMA
}

data class Connection(
    val id: String,
    val preset: Preset,
    val baseUrl: String,
    val model: String,
) {
    /** Name shown in the bar: the service, or the server's host for a custom address. */
    val title: String
        get() = preset.title.ifEmpty { Logic.host(baseUrl) }
}
