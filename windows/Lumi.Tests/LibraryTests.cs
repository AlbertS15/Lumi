using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace Lumi.Tests;

public class RankingTests
{
    [Fact]
    public void CosineOfIdenticalVectorsIsOne() =>
        Assert.Equal(1f, Ranking.Cosine(new[] { 1f, 2f, 3f }, new[] { 2f, 4f, 6f }), 4);

    [Fact]
    public void CosineOfOrthogonalVectorsIsZero() =>
        Assert.Equal(0f, Ranking.Cosine(new[] { 1f, 0f }, new[] { 0f, 1f }), 4);

    [Fact]
    public void CosineOfOppositeVectorsIsMinusOne() =>
        Assert.Equal(-1f, Ranking.Cosine(new[] { 1f, 1f }, new[] { -1f, -1f }), 4);

    [Fact]
    public void CosineHandlesZeroAndMismatchedVectors()
    {
        Assert.Equal(0f, Ranking.Cosine(new[] { 0f, 0f }, new[] { 1f, 1f }));
        Assert.Equal(0f, Ranking.Cosine(new[] { 1f }, new[] { 1f, 1f }));
        Assert.Equal(0f, Ranking.Cosine(Array.Empty<float>(), Array.Empty<float>()));
    }

    private static readonly (string Name, float[] V)[] Items =
    {
        ("far", new[] { 0f, 1f }),
        ("near", new[] { 1f, 0.1f }),
        ("exact", new[] { 1f, 0f }),
        ("opposite", new[] { -1f, 0f }),
    };

    [Fact]
    public void TopReturnsBestFirst()
    {
        var top = Ranking.Top(new[] { 1f, 0f }, Items, i => i.V, 3);
        Assert.Equal(new[] { "exact", "near", "far" }, top.Select(t => t.Item.Name));
        Assert.True(top[0].Score >= top[1].Score && top[1].Score >= top[2].Score);
    }

    [Fact]
    public void TopCapsAtKAndHandlesSmallInput()
    {
        Assert.Single(Ranking.Top(new[] { 1f, 0f }, Items, i => i.V, 1));
        Assert.Equal(4, Ranking.Top(new[] { 1f, 0f }, Items, i => i.V, 10).Count);
        Assert.Empty(Ranking.Top(new[] { 1f, 0f }, Items, i => i.V, 0));
        Assert.Empty(Ranking.Top(new[] { 1f, 0f }, Array.Empty<(string, float[])>(), i => i.Item2, 5));
    }

    [Fact]
    public void TopIgnoresVectorsFromAnotherModel()
    {
        var items = new[] { ("old", new[] { 1f, 0f, 0f }), ("new", new[] { 0f, 1f }) };
        var top = Ranking.Top(new[] { 0f, 1f }, items, i => i.Item2, 5);
        Assert.Equal("new", Assert.Single(top).Item.Item1);
    }

    [Fact]
    public void EqualScoresKeepTheirOrder()
    {
        var items = new[] { ("a", new[] { 1f, 0f }), ("b", new[] { 2f, 0f }), ("c", new[] { 3f, 0f }) };
        var top = Ranking.Top(new[] { 1f, 0f }, items, i => i.Item2, 3);
        Assert.Equal(new[] { "a", "b", "c" }, top.Select(t => t.Item.Item1));
    }
}

public class LibraryIndexTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "lumi-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void IndexSurvivesSaveAndLoad()
    {
        var dir = Temp();
        var path = Path.Combine(dir, "index.bin");
        try
        {
            var index = new LibraryIndex { Model = "embeddinggemma" };
            var file = new LibraryFile { Path = "/notes/Привет.md", Size = 42, Modified = 123456789 };
            file.Chunks.Add(new LibraryChunk("Первый кусок", new[] { 0.5f, -1.25f }));
            file.Chunks.Add(new LibraryChunk("second piece", new[] { 3f, 4f }));
            index.Files[file.Path] = file;
            index.Files["/notes/empty.txt"] = new LibraryFile { Path = "/notes/empty.txt", Skipped = true };
            index.Save(path);

            var loaded = LibraryIndex.Load(path);
            Assert.Equal("embeddinggemma", loaded.Model);
            Assert.Equal(2, loaded.Files.Count);
            Assert.Equal(2, loaded.ChunkCount);
            var f = loaded.Files["/notes/Привет.md"];
            Assert.Equal(42, f.Size);
            Assert.Equal(123456789, f.Modified);
            Assert.Equal("Первый кусок", f.Chunks[0].Text);
            Assert.Equal(new[] { 0.5f, -1.25f }, f.Chunks[0].Vector);
            Assert.True(loaded.Files["/notes/empty.txt"].Skipped);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingBrokenOrForeignFilesGiveAnEmptyIndex()
    {
        var dir = Temp();
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Empty(LibraryIndex.Load(Path.Combine(dir, "none.bin")).Files);
            var junk = Path.Combine(dir, "junk.bin");
            File.WriteAllBytes(junk, new byte[] { 1, 2, 3, 4, 5 });
            Assert.Empty(LibraryIndex.Load(junk).Files);
            var good = Path.Combine(dir, "good.bin");
            var index = new LibraryIndex { Model = "m" };
            index.Files["a"] = new LibraryFile { Path = "a", Chunks = { new LibraryChunk("t", new[] { 1f }) } };
            index.Save(good);
            var bytes = File.ReadAllBytes(good);
            File.WriteAllBytes(good, bytes[..(bytes.Length - 3)]);
            Assert.Empty(LibraryIndex.Load(good).Files);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class DocTextTests
{
    [Fact]
    public void SupportedExtensions()
    {
        foreach (var name in new[] { "a.txt", "a.MD", "a.pdf", "a.docx", "a.markdown" }) Assert.True(DocText.Supported(name), name);
        foreach (var name in new[] { "a.doc", "a.png", "a", "a.txt.exe" }) Assert.False(DocText.Supported(name), name);
    }

    [Fact]
    public void PlainTextReadsUtf8WithAndWithoutBom()
    {
        Assert.Equal("Привет, мир", DocText.PlainText(Encoding.UTF8.GetBytes("Привет, мир")));
        Assert.Equal("Hello", DocText.PlainText(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Hello")).ToArray()));
    }

    [Fact]
    public void PlainTextReadsUtf16WithBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("Grüße")).ToArray();
        Assert.Equal("Grüße", DocText.PlainText(bytes));
    }

    [Fact]
    public void PlainTextFallsBackToWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal("Привет", DocText.PlainText(Encoding.GetEncoding(1251).GetBytes("Привет")));
    }

    [Fact]
    public void DocxTextComesOutByParagraph()
    {
        const string xml = "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            "<w:p><w:r><w:t>Hello</w:t></w:r><w:r><w:t xml:space=\"preserve\"> world</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>Второй</w:t></w:r><w:r><w:tab/><w:t>абзац</w:t></w:r></w:p></w:body></w:document>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        var chunks = Chunker.Split(DocText.DocxXml(stream));
        Assert.Equal("Hello world\n\nВторой абзац", Assert.Single(chunks));
    }

    [Fact]
    public void DocxRefusesExternalEntities()
    {
        const string xml = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>&x;</w:t></w:r></w:p></w:document>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        Assert.ThrowsAny<System.Xml.XmlException>(() => DocText.DocxXml(stream));
    }

    [Fact]
    public void ReadsAZipDocxAndAPlainFileFromDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumi-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var docx = Path.Combine(dir, "a.docx");
            using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
                w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>Contract 7</w:t></w:r></w:p></w:document>");
            Assert.Contains("Contract 7", DocText.Read(docx));
            var txt = Path.Combine(dir, "a.txt");
            File.WriteAllText(txt, "plain");
            Assert.Equal("plain", DocText.Read(txt));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class LocalOllamaTests
{
    private const string Tags = """
        {"models":[
          {"name":"lumi-gemma:latest","capabilities":["completion"]},
          {"name":"embeddinggemma:latest","capabilities":["embedding"]},
          {"name":"gpt-oss:120b-cloud","remote_host":"https://ollama.com:443","capabilities":["completion"]},
          {"name":"other:7b","capabilities":["completion","tools"]}
        ]}
        """;

    [Fact]
    public void CloudAndEmbeddingModelsNeverAnswer()
    {
        var models = LocalOllama.ParseModels(Tags);
        Assert.Equal(4, models.Count);
        Assert.False(models.Single(m => m.Name.StartsWith("gpt-oss")).CanChat);
        Assert.False(models.Single(m => m.Name.StartsWith("embeddinggemma")).CanChat);
        Assert.True(models.Single(m => m.Name.StartsWith("other")).CanChat);
    }

    [Fact]
    public void ChosenCloudModelIsReplacedByALocalOne()
    {
        var models = LocalOllama.ParseModels(Tags);
        Assert.Equal("other:7b", LocalOllama.PickChatModel(models, "other:7b", new[] { "lumi-gemma" }));
        Assert.Equal("lumi-gemma:latest", LocalOllama.PickChatModel(models, "gpt-oss:120b-cloud", new[] { "lumi-gemma" }));
        Assert.Equal("lumi-gemma:latest", LocalOllama.PickChatModel(models, null, new[] { "missing", "lumi-gemma" }));
        Assert.Equal("lumi-gemma:latest", LocalOllama.PickChatModel(models, null, Array.Empty<string>()));
    }

    [Fact]
    public void NoLocalChatModelGivesNull()
    {
        var models = LocalOllama.ParseModels("""{"models":[{"name":"x-cloud"},{"name":"nomic-embed-text:latest"}]}""");
        Assert.Null(LocalOllama.PickChatModel(models, "x-cloud", new[] { "x-cloud" }));
    }

    [Fact]
    public void ModelNameMatchingKnowsLatest()
    {
        var m = new LocalModel("embeddinggemma:latest", false, Array.Empty<string>());
        Assert.True(m.Is("embeddinggemma"));
        Assert.False(m.Is("embedding"));
    }

    [Fact]
    public void EmbeddingPrefixesFollowTheModel()
    {
        Assert.StartsWith("task: search result", LocalOllama.Prefix("embeddinggemma", true, null));
        Assert.Contains("title: Notes", LocalOllama.Prefix("embeddinggemma", false, "Notes"));
        Assert.Equal("search_query: ", LocalOllama.Prefix("nomic-embed-text", true, null));
        Assert.Equal("", LocalOllama.Prefix("bge-m3", false, null));
    }

    [Fact]
    public void PromptHoldsTheExcerptsAndTheQuestion()
    {
        var hits = new List<LibraryHit> { new("/docs/rent.md", "Rent is 500 a month.", 0.9f) };
        var user = LibraryPrompt.User("How much is rent?", hits);
        Assert.Contains("[1] rent.md", user);
        Assert.Contains("Rent is 500 a month.", user);
        Assert.EndsWith("Question: How much is rent?", user);
        Assert.Contains("no information", LibraryPrompt.System);
    }
}
