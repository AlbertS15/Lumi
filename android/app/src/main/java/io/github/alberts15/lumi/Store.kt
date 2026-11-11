package io.github.alberts15.lumi

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** Connections in app preferences; API keys encrypted with a key that never leaves the phone's keystore. */
class Store(context: Context) {
    private val prefs = context.getSharedPreferences("lumi", Context.MODE_PRIVATE)

    fun connections(): List<Connection> {
        val array = JSONArray(prefs.getString("connections", "[]"))
        return (0 until array.length()).mapNotNull { i ->
            val o = array.getJSONObject(i)
            val preset = runCatching { Preset.valueOf(o.getString("preset")) }.getOrNull() ?: return@mapNotNull null
            Connection(o.getString("id"), preset, o.getString("baseUrl"), o.getString("model"))
        }
    }

    fun save(list: List<Connection>) {
        val array = JSONArray()
        list.forEach {
            array.put(JSONObject().put("id", it.id).put("preset", it.preset.name).put("baseUrl", it.baseUrl).put("model", it.model))
        }
        prefs.edit().putString("connections", array.toString()).apply()
    }

    var activeId: String?
        get() = prefs.getString("active", null)
        set(value) = prefs.edit().putString("active", value).apply()

    fun key(id: String): String = prefs.getString("key_$id", null)?.let(Secrets::decrypt) ?: ""

    fun setKey(id: String, key: String) {
        if (key.isBlank()) prefs.edit().remove("key_$id").apply()
        else prefs.edit().putString("key_$id", Secrets.encrypt(key.trim())).apply()
    }
}

private object Secrets {
    private const val ALIAS = "lumi-api-keys"

    private fun key(): SecretKey {
        val store = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (store.getKey(ALIAS, null) as? SecretKey)?.let { return it }
        val gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
        gen.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build()
        )
        return gen.generateKey()
    }

    fun encrypt(plain: String): String {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        return Base64.encodeToString(cipher.iv + cipher.doFinal(plain.toByteArray()), Base64.NO_WRAP)
    }

    fun decrypt(text: String): String? = runCatching {
        val all = Base64.decode(text, Base64.NO_WRAP)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, all, 0, 12))
        String(cipher.doFinal(all, 12, all.size - 12))
    }.getOrNull()
}
