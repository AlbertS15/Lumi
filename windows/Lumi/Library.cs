using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Lumi;

// "Ask my files" (experimental): the person's own documents, cut into pieces, turned into vectors by a small embedding
// model in the local Ollama and kept in the app's data folder. A question finds the closest pieces and the local
// model answers from them only. Nothing here talks to anything but Ollama on 127.0.0.1.
//
// This file has no UI and no settings in it, so the tests can build it on its own.

/// Cuts text into pieces of about 450 characters that overlap a little, ending them at a paragraph, a sentence or a
/// word where it can.
public static class Chunker
{
    public const int Size = 450;
    public const int Overlap = 80;

    /// One line ending, single spaces, at most one empty line in a row.
    public static string Normalize(string text)
    {
        var s = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ').Replace(' ', ' ').Replace("\0", "");
        s = Regex.Replace(s, "[ \f\v]+", " ");
        s = Regex.Replace(s, " *\n *", "\n");
        s = Regex.Replace(s, "\n{3,}", "\n\n");
        return s.Trim();
    }

    public static List<string> Split(string text, int size = Size, int overlap = Overlap)
    {
        if (size < 20) throw new ArgumentOutOfRangeException(nameof(size));
        if (overlap < 0 || overlap >= size / 2) throw new ArgumentOutOfRangeException(nameof(overlap));
        var t = Normalize(text);
        var chunks = new List<string>();
        var start = 0;
        while (start < t.Length)
        {
            var end = Math.Min(start + size, t.Length);
            if (end < t.Length) end = BreakPoint(t, start, end, size);
            var piece = t[start..end].Trim();
            if (piece.Length > 0) chunks.Add(piece);
            if (end >= t.Length) break;
            start = OverlapStart(t, Math.Max(end - overlap, start + 1), end);
        }
        return chunks;
    }

    /// The best place to end a piece in (start + 60% of size, end]: an empty line, then the end of a sentence or
    /// line, then any space; else a hard cut at end.
    private static int BreakPoint(string t, int start, int end, int size)
    {
        var min = start + size * 3 / 5;
        for (var i = end; i > min; i--)
            if (t[i - 1] == '\n' && t[i - 2] == '\n') return i;
        for (var i = end; i > min; i--)
            if (t[i - 1] == '\n' || (t[i - 1] == ' ' && ".!?…;:".IndexOf(t[i - 2]) >= 0)) return i;
        for (var i = end; i > min; i--)
            if (char.IsWhiteSpace(t[i - 1])) return i;
        return end;
    }

    /// Moves the start of the overlap forward to the beginning of a word, unless there's no space before end.
    private static int OverlapStart(string t, int from, int end)
    {
        if (from == 0 || char.IsWhiteSpace(t[from - 1])) return from;
        var i = from;
        while (i < end && !char.IsWhiteSpace(t[i])) i++;
        while (i < end && char.IsWhiteSpace(t[i])) i++;
        return i < end ? i : from;
    }
}

/// Cosine similarity and the top pieces for a question.
public static class Ranking
{
    public static float Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb)));
    }

    /// The k items closest to the query, best first; equal scores keep their order. Vectors of another length
    /// (from another embedding model) never match.
    public static List<(T Item, float Score)> Top<T>(float[] query, IEnumerable<T> items, Func<T, float[]> vector, int k)
    {
        if (k <= 0) return new();
        return items
            .Where(i => vector(i).Length == query.Length)
            .Select(i => (Item: i, Score: Cosine(query, vector(i))))
            .OrderByDescending(x => x.Score)
            .Take(k)
            .ToList();
    }
}

/// Plain text out of the supported files: .txt, .md, .pdf (its text layer) and .docx.
public static class DocText
{
    public static readonly string[] Extensions = { ".txt", ".md", ".markdown", ".pdf", ".docx" };

    /// Bigger files are skipped: they'd take long to index and are rarely notes.
    public const long MaxBytes = 20L * 1024 * 1024;

    public static bool Supported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static string Read(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => Pdf(path),
        ".docx" => Docx(path),
        _ => PlainText(File.ReadAllBytes(path)),
    };

    /// UTF-8 (with or without a BOM) or UTF-16 with a BOM; anything else is read as Windows-1251, the usual
    /// encoding of old Russian text files.
    public static string PlainText(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return new StreamReader(new MemoryStream(bytes), Encoding.Unicode, true).ReadToEnd();
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(1251).GetString(bytes);
            }
            catch
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }

    /// The text runs of word/document.xml, one paragraph per line pair.
    public static string Docx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry == null) return "";
        using var stream = entry.Open();
        return DocxXml(stream);
    }

    public static string DocxXml(Stream stream)
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var text = new StringBuilder();
        var inText = false;
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element when reader.NamespaceURI == W:
                    if (reader.LocalName == "t") inText = !reader.IsEmptyElement;
                    else if (reader.LocalName == "tab") text.Append(' ');
                    else if (reader.LocalName is "br" or "cr") text.Append('\n');
                    break;
                case XmlNodeType.EndElement when reader.NamespaceURI == W:
                    if (reader.LocalName == "t") inText = false;
                    else if (reader.LocalName == "p") text.Append("\n\n");
                    break;
                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace when inText:
                    text.Append(reader.Value);
                    break;
            }
        }
        return text.ToString();
    }

    /// The PDF's own text layer, word by word; a scan without one gives nothing and is skipped.
    public static string Pdf(string path)
    {
        var text = new StringBuilder();
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            double? lastBottom = null;
            foreach (var word in page.GetWords())
            {
                var box = word.BoundingBox;
                if (lastBottom is double bottom)
                    text.Append(Math.Abs(bottom - box.Bottom) > Math.Max(box.Height, 1) * 0.6 ? '\n' : ' ');
                text.Append(word.Text);
                lastBottom = box.Bottom;
            }
            text.Append("\n\n");
        }
        return text.ToString();
    }
}

/// One piece of a file with its vector.
public sealed record LibraryChunk(string Text, float[] Vector);

/// A file as it was when indexed; Skipped when it had no text or was too big.
public sealed class LibraryFile
{
    public string Path = "";
    public long Size;
    public long Modified;
    public bool Skipped;
    public List<LibraryChunk> Chunks = new();
}

/// A piece found for a question.
public sealed record LibraryHit(string Path, string Text, float Score);

/// The index on disk: a small binary file, read whole into memory.
public sealed class LibraryIndex
{
    private const string Magic = "LUMI-LIBRARY-1";

    public string Model = "";
    public Dictionary<string, LibraryFile> Files = new(PathComparer);

    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public int ChunkCount => Files.Values.Sum(f => f.Chunks.Count);

    public void Save(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        using (var w = new BinaryWriter(stream, Encoding.UTF8))
        {
            w.Write(Magic);
            w.Write(Model);
            w.Write(Files.Count);
            foreach (var f in Files.Values)
            {
                w.Write(f.Path);
                w.Write(f.Size);
                w.Write(f.Modified);
                w.Write(f.Skipped);
                w.Write(f.Chunks.Count);
                foreach (var c in f.Chunks)
                {
                    w.Write(c.Text);
                    w.Write(c.Vector.Length);
                    foreach (var x in c.Vector) w.Write(x);
                }
            }
        }
        // The pieces are the person's own text: on Linux only they may read it.
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, true);
    }

    /// An empty index when the file is missing, broken or from another version.
    public static LibraryIndex Load(string path)
    {
        var index = new LibraryIndex();
        try
        {
            if (!File.Exists(path)) return index;
            using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            if (r.ReadString() != Magic) return index;
            index.Model = r.ReadString();
            var files = r.ReadInt32();
            for (var i = 0; i < files; i++)
            {
                var f = new LibraryFile { Path = r.ReadString(), Size = r.ReadInt64(), Modified = r.ReadInt64(), Skipped = r.ReadBoolean() };
                var chunks = r.ReadInt32();
                for (var j = 0; j < chunks; j++)
                {
                    var text = r.ReadString();
                    var vector = new float[r.ReadInt32()];
                    for (var k = 0; k < vector.Length; k++) vector[k] = r.ReadSingle();
                    f.Chunks.Add(new LibraryChunk(text, vector));
                }
                index.Files[f.Path] = f;
            }
            return index;
        }
        catch
        {
            return new LibraryIndex();
        }
    }
}

/// A model in the local Ollama. Remote ones (Ollama's cloud models) run elsewhere and are never used here.
public sealed record LocalModel(string Name, bool Remote, string[] Capabilities)
{
    public bool CanChat => !Remote && (Capabilities.Length > 0
        ? Capabilities.Contains("completion")
        : !Name.Contains("embed", StringComparison.OrdinalIgnoreCase));

    public bool Is(string name) => Name == name || Name == name + ":latest";
}

/// Talks to the Ollama on this computer only; the address is fixed on purpose.
public static class LocalOllama
{
    public const string Host = "http://127.0.0.1:11434";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// The installed models, or null when Ollama isn't running.
    public static async Task<List<LocalModel>?> ModelsAsync(CancellationToken cancel = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var body = await Http.GetStringAsync(Host + "/api/tags", timeout.Token);
            return ParseModels(body);
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    public static List<LocalModel> ParseModels(string json)
    {
        var models = new List<LocalModel>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var list)) return models;
        foreach (var m in list.EnumerateArray())
        {
            if (!m.TryGetProperty("name", out var n) || n.GetString() is not { } name) continue;
            var remote = m.TryGetProperty("remote_host", out _) || m.TryGetProperty("remote_model", out _)
                || name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || name.Contains("-cloud", StringComparison.OrdinalIgnoreCase);
            var caps = m.TryGetProperty("capabilities", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : Array.Empty<string>();
            models.Add(new LocalModel(name, remote, caps));
        }
        return models;
    }

    /// The model to answer with: the preferred one if it's local and installed, else the first of `order` that is,
    /// else any local chat model. Null when there's none.
    public static string? PickChatModel(IReadOnlyList<LocalModel> models, string? preferred, IEnumerable<string> order)
    {
        var usable = models.Where(m => m.CanChat).ToList();
        if (preferred != null && usable.FirstOrDefault(m => m.Is(preferred)) is { } p) return p.Name;
        foreach (var name in order)
            if (usable.FirstOrDefault(m => m.Is(name)) is { } m) return m.Name;
        return usable.FirstOrDefault()?.Name;
    }

    /// Instructions some embedding models are trained with; others take the text as it is.
    public static string Prefix(string model, bool query, string? title)
    {
        if (model.StartsWith("embeddinggemma", StringComparison.OrdinalIgnoreCase))
            return query ? "task: search result | query: " : $"title: {(string.IsNullOrWhiteSpace(title) ? "none" : title)} | text: ";
        if (model.StartsWith("nomic-embed-text", StringComparison.OrdinalIgnoreCase))
            return query ? "search_query: " : "search_document: ";
        return "";
    }

    public static async Task<float[][]> EmbedAsync(string model, IReadOnlyList<string> texts, bool query, string? title, CancellationToken cancel)
    {
        var input = new JsonArray();
        foreach (var t in texts) input.Add(Prefix(model, query, title) + t);
        var body = new JsonObject { ["model"] = model, ["input"] = input, ["truncate"] = true };
        using var response = await Http.PostAsync(Host + "/api/embed",
            new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cancel);
        var text = await response.Content.ReadAsStringAsync(cancel);
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
        var result = doc.RootElement.GetProperty("embeddings").EnumerateArray()
            .Select(v => v.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();
        if (result.Length != texts.Count) throw new InvalidOperationException("Ollama returned " + result.Length + " embeddings for " + texts.Count);
        return result;
    }
}

/// What the model is told: the question and the pieces, and to answer from them only.
public static class LibraryPrompt
{
    public const string System =
        "You answer questions using only the excerpts from the user's own files given below. " +
        "If the excerpts don't contain the answer, say plainly that there is no information about it in the files; " +
        "never guess and never use outside knowledge. " +
        "Answer briefly, in the language of the question, and name the file the answer comes from.";

    public static string User(string question, IReadOnlyList<LibraryHit> hits)
    {
        var text = new StringBuilder("Excerpts from the user's files:\n\n");
        for (var i = 0; i < hits.Count; i++)
            text.Append('[').Append(i + 1).Append("] ").Append(Path.GetFileName(hits[i].Path)).Append('\n').Append(hits[i].Text).Append("\n\n");
        text.Append("Question: ").Append(question);
        return text.ToString();
    }
}

public enum LibraryState { Off, Indexing, Ready, NoOllama, NoModel, Error }

/// The index of the chosen folders, kept up to date in the background: only new and changed files are read again.
public sealed class Library
{
    /// Multilingual (100+ languages, Russian included), small (300M parameters, about 620 MB) and from the same Gemma
    /// family as Lumi's own models.
    public const string DefaultEmbedModel = "embeddinggemma";
    public const string DefaultEmbedSize = "620 MB";
    public const int MaxFiles = 20000;
    public const int MaxChunksPerFile = 2000;
    private const int Batch = 16;

    public static Library Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lumi", "library", "index.bin"));

    private readonly string _path;
    private readonly object _gate = new();
    private LibraryIndex? _index;
    private string[] _folders = Array.Empty<string>();
    private string _model = DefaultEmbedModel;
    private int _running;
    private volatile bool _again;

    public Library(string path) => _path = path;

    public LibraryState State { get; private set; } = LibraryState.Off;
    public string? Error { get; private set; }
    public int Done { get; private set; }
    public int Total { get; private set; }
    public int FileCount { get; private set; }
    public int ChunkCount { get; private set; }
    public int SkippedCount { get; private set; }
    public string Model => _model;

    /// Raised on a background thread whenever the state or the counts change.
    public event Action? Changed;

    /// New folders or another model: index again (only what's needed).
    public void Configure(IEnumerable<string> folders, string model)
    {
        var list = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(LibraryIndex.PathComparer).ToArray();
        var model2 = string.IsNullOrWhiteSpace(model) ? DefaultEmbedModel : model.Trim();
        lock (_gate)
        {
            if (list.SequenceEqual(_folders, LibraryIndex.PathComparer) && model2 == _model && _index != null) return;
            _folders = list;
            _model = model2;
        }
        Refresh();
    }

    /// Looks for new, changed and removed files; a call during a pass makes it start over when it's done.
    public void Refresh()
    {
        _again = true;
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        Task.Run(async () =>
        {
            try
            {
                while (_again)
                {
                    _again = false;
                    try { await PassAsync(); }
                    catch (Exception e) { Set(LibraryState.Error, e.Message); }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
            if (_again) Refresh();
        });
    }

    private void Set(LibraryState state, string? error = null)
    {
        State = state;
        Error = error;
        Changed?.Invoke();
    }

    private void Count()
    {
        lock (_gate)
        {
            var files = _index?.Files.Values ?? Enumerable.Empty<LibraryFile>();
            FileCount = files.Count(f => !f.Skipped);
            SkippedCount = files.Count(f => f.Skipped);
            ChunkCount = files.Sum(f => f.Chunks.Count);
        }
    }

    private static IEnumerable<string> Scan(string folder)
    {
        if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            // Hidden folders (.git, ~/.cache, dot files on Linux) and system files aren't the person's documents.
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(p => DocText.Supported(p) && !p.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar));
    }

    private async Task PassAsync()
    {
        string[] folders;
        string model;
        LibraryIndex index;
        lock (_gate)
        {
            _index ??= LibraryIndex.Load(_path);
            folders = _folders;
            model = _model;
            if (_index.Model != model)
            {
                _index.Files.Clear();
                _index.Model = model;
            }
            index = _index;
        }

        // What's on disk now; files no longer in a chosen folder leave the index (and the disk) at once.
        var found = new Dictionary<string, FileInfo>(LibraryIndex.PathComparer);
        foreach (var folder in folders)
            foreach (var path in Scan(folder))
            {
                if (found.Count >= MaxFiles) break;
                try { found[path] = new FileInfo(path); } catch { }
            }
        List<FileInfo> changed;
        lock (_gate)
        {
            var gone = index.Files.Keys.Where(p => !found.ContainsKey(p)).ToList();
            foreach (var path in gone) index.Files.Remove(path);
            changed = found.Values
                .Where(f => !index.Files.TryGetValue(f.FullName, out var old) || old.Size != f.Length || old.Modified != f.LastWriteTimeUtc.Ticks)
                .OrderBy(f => f.FullName, LibraryIndex.PathComparer)
                .ToList();
            if (gone.Count > 0 || !File.Exists(_path)) index.Save(_path);
        }
        Count();
        if (folders.Length == 0)
        {
            Set(LibraryState.Off);
            return;
        }
        if (changed.Count == 0)
        {
            Set(LibraryState.Ready);
            return;
        }

        var models = await LocalOllama.ModelsAsync();
        if (models == null)
        {
            Set(LibraryState.NoOllama);
            return;
        }
        if (!models.Any(m => m.Is(model)))
        {
            Set(LibraryState.NoModel);
            return;
        }

        Total = changed.Count;
        Done = 0;
        Set(LibraryState.Indexing);
        var lastSave = DateTime.UtcNow;
        foreach (var info in changed)
        {
            if (_again) break; // the folders changed: start over with them
            var entry = new LibraryFile { Path = info.FullName, Size = info.Length, Modified = info.LastWriteTimeUtc.Ticks };
            var pieces = new List<string>();
            if (info.Length <= DocText.MaxBytes)
            {
                try { pieces = Chunker.Split(DocText.Read(info.FullName)).Take(MaxChunksPerFile).ToList(); }
                catch { /* locked, broken or encrypted: skipped until it changes */ }
            }
            entry.Skipped = pieces.Count == 0;
            try
            {
                for (var i = 0; i < pieces.Count; i += Batch)
                {
                    var batch = pieces.Skip(i).Take(Batch).ToList();
                    var vectors = await LocalOllama.EmbedAsync(model, batch, false, Path.GetFileNameWithoutExtension(info.Name), CancellationToken.None);
                    for (var j = 0; j < batch.Count; j++) entry.Chunks.Add(new LibraryChunk(batch[j], vectors[j]));
                }
            }
            catch (HttpRequestException)
            {
                lock (_gate) index.Save(_path);
                Count();
                Set(LibraryState.NoOllama);
                return;
            }
            lock (_gate)
            {
                index.Files[entry.Path] = entry;
                if (DateTime.UtcNow - lastSave > TimeSpan.FromSeconds(20))
                {
                    index.Save(_path);
                    lastSave = DateTime.UtcNow;
                }
            }
            Done++;
            Count();
            Changed?.Invoke();
        }
        lock (_gate) index.Save(_path);
        Count();
        Set(_again ? LibraryState.Indexing : LibraryState.Ready);
    }

    /// The k pieces closest to the question.
    public async Task<List<LibraryHit>> SearchAsync(string question, int k, CancellationToken cancel)
    {
        List<(string Path, LibraryChunk Chunk)> all;
        string model;
        lock (_gate)
        {
            _index ??= LibraryIndex.Load(_path);
            model = _index.Model;
            all = _index.Files.Values.SelectMany(f => f.Chunks.Select(c => (f.Path, c))).ToList();
        }
        if (all.Count == 0) return new();
        var query = (await LocalOllama.EmbedAsync(model, new[] { question }, true, null, cancel))[0];
        return Ranking.Top(query, all, x => x.Chunk.Vector, k)
            .Select(x => new LibraryHit(x.Item.Path, x.Item.Chunk.Text, x.Score))
            .ToList();
    }
}
