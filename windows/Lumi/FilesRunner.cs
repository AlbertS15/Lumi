using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Lumi;

/// Keeps the library in step with the settings: on start, on every change of folders and every 20 minutes.
public static class LibraryHost
{
    private static Timer? _timer;
    private static double? _pull;

    public static void Start()
    {
        Apply();
        Settings.Shared.Changed += Apply;
        _timer = new Timer(_ => Library.Shared.Refresh(), null, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20));
    }

    private static void Apply() => Library.Shared.Configure(Settings.Shared.LibraryFolders, Settings.Shared.LibraryEmbedModel);

    public static bool IsEnabled => Settings.Shared.LibraryFolders.Count > 0;

    public static void AddFolders(IEnumerable<string> folders)
    {
        foreach (var f in folders)
            if (!string.IsNullOrWhiteSpace(f) && !Settings.Shared.LibraryFolders.Contains(f, LibraryIndex.PathComparer))
                Settings.Shared.LibraryFolders.Add(f);
        Settings.Shared.Save();
    }

    public static void RemoveFolder(string folder)
    {
        Settings.Shared.LibraryFolders.RemoveAll(f => LibraryIndex.PathComparer.Equals(f, folder));
        Settings.Shared.Save();
    }

    /// The folder with the home folder shortened to ~.
    public static string Short(string folder) =>
        folder.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");

    /// Whether the settings should offer to download the embedding model.
    public static bool NeedsModel => Library.Shared.State == LibraryState.NoModel && _pull == null;

    public static string StatusText
    {
        get
        {
            if (_pull is double p) return S.FilesDownloading(((int)(p * 100)).ToString());
            var lib = Library.Shared;
            if (!IsEnabled) return S.FilesStatusOff;
            switch (lib.State)
            {
                case LibraryState.Indexing:
                    return S.FilesStatusIndexing(lib.Done.ToString(), lib.Total.ToString());
                case LibraryState.NoOllama:
                    return S.FilesStatusNoOllama;
                case LibraryState.NoModel:
                    return S.FilesStatusNoModel(lib.Model, lib.Model == Library.DefaultEmbedModel ? Library.DefaultEmbedSize : "?");
                case LibraryState.Error:
                    return S.FilesStatusError(lib.Error ?? "");
                default:
                    var text = S.FilesStatusReady(lib.FileCount.ToString(), lib.ChunkCount.ToString());
                    return lib.SkippedCount > 0 ? text + " · " + S.FilesStatusSkipped(lib.SkippedCount.ToString()) : text;
            }
        }
    }

    /// Downloads the embedding model through the local Ollama, then indexes. `changed` runs on a background thread.
    public static async Task PullModelAsync(Action changed)
    {
        if (_pull != null) return;
        _pull = 0;
        changed();
        try
        {
            var last = 0.0;
            await Ollama.PullAsync(Library.Shared.Model, p =>
            {
                _pull = p;
                if (p - last < 0.01) return;
                last = p;
                changed();
            }, CancellationToken.None);
        }
        catch
        {
            // Library.Refresh below reports what's still missing.
        }
        _pull = null;
        Library.Shared.Refresh();
        changed();
    }

    /// Lumi's own models, the one recommended for this computer first, then the rest that fit, largest first.
    public static IEnumerable<string> PreferredChatModels =>
        new[] { Ollama.Recommended.Name }
            .Concat(LumiModels.All.Where(r => r.MemoryGB <= Ollama.MemoryGB).Reverse().Select(r => r.Name))
            .Concat(LumiModels.All.Select(r => r.Name))
            .Distinct();
}

/// Answers a question from the person's files with a model in the local Ollama, whatever model is chosen for chat.
public sealed class FilesRunner
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private CancellationTokenSource? _cts;

    public void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
    }

    /// The callbacks run on the UI thread through `post`; onSources gets the files used and the model's name.
    public void Ask(string question, Action<Action> post, Action<IReadOnlyList<string>, string> onSources,
        Action<string> onText, Action<string?> onDone)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;
        Task.Run(async () =>
        {
            try
            {
                var lib = Library.Shared;
                if (lib.ChunkCount == 0)
                {
                    var why = !LibraryHost.IsEnabled ? S.FilesNoFolders
                        : lib.State == LibraryState.Indexing ? S.FilesStillIndexing(lib.Done.ToString(), lib.Total.ToString())
                        : lib.State is LibraryState.NoOllama or LibraryState.NoModel or LibraryState.Error ? LibraryHost.StatusText
                        : S.FilesEmpty;
                    post(() => onDone(why));
                    return;
                }

                var models = await LocalOllama.ModelsAsync(token);
                if (models == null)
                {
                    post(() => onDone(S.FilesStatusNoOllama));
                    return;
                }
                // The chosen chat model counts only when it's in this computer's Ollama.
                var chosen = Settings.Shared.Connection is { } c && Ollama.IsOllama(c.BaseUrl) ? c.Model : null;
                var model = LocalOllama.PickChatModel(models, chosen, LibraryHost.PreferredChatModels);
                if (model == null)
                {
                    post(() => onDone(S.FilesNoChatModel));
                    return;
                }

                var hits = await lib.SearchAsync(question, 5, token);
                if (hits.Count == 0)
                {
                    post(() => onDone(S.FilesEmpty));
                    return;
                }
                var sources = hits.Select(h => h.Path).Distinct(LibraryIndex.PathComparer).ToList();
                post(() => onSources(sources, model));

                var body = new JsonObject
                {
                    ["model"] = model,
                    ["messages"] = new JsonArray
                    {
                        new JsonObject { ["role"] = "system", ["content"] = LibraryPrompt.System },
                        new JsonObject { ["role"] = "user", ["content"] = LibraryPrompt.User(question, hits) },
                    },
                    ["stream"] = true,
                    ["temperature"] = 0.2,
                    ["reasoning_effort"] = "none",
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, LocalOllama.Host + "/v1/chat/completions")
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(token);
                    post(() => onDone(S.FilesFailed($"{(int)response.StatusCode} {detail}")));
                    return;
                }
                using var stream = await response.Content.ReadAsStreamAsync(token);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var any = false;
                string? line;
                while ((line = await reader.ReadLineAsync(token)) != null)
                {
                    if (!line.StartsWith("data:")) continue;
                    var payload = line.Substring(5).Trim();
                    if (payload == "[DONE]") break;
                    JsonNode? obj;
                    try { obj = JsonNode.Parse(payload); } catch { continue; }
                    var piece = (string?)obj?["choices"]?[0]?["delta"]?["content"];
                    if (string.IsNullOrEmpty(piece)) continue;
                    any = true;
                    post(() => onText(piece));
                }
                post(() => onDone(any ? null : S.ErrEmpty(model)));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                post(() => onDone(S.FilesFailed(e.Message)));
            }
        });
    }
}
