using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Lumi.Tests;

public class ChunkerTests
{
    /// "w0 w1 … w{n-1}" with a full stop every 12 words, so every word is unique and easy to find.
    private static string Words(int n)
    {
        var text = new StringBuilder();
        for (var i = 0; i < n; i++)
        {
            if (i > 0) text.Append(' ');
            text.Append('w').Append(i);
            if (i % 12 == 11) text.Append('.');
        }
        return text.ToString();
    }

    private static List<string> Tokens(string chunk) =>
        Regex.Split(chunk, @"\s+").Select(t => t.TrimEnd('.')).Where(t => t.Length > 0).ToList();

    [Fact]
    public void EmptyTextGivesNoChunks()
    {
        Assert.Empty(Chunker.Split(""));
        Assert.Empty(Chunker.Split("   \n\n \t "));
    }

    [Fact]
    public void ShortTextIsOneChunk()
    {
        var chunks = Chunker.Split("  Lumi is a bar.\r\nIt answers.  ");
        Assert.Equal(new[] { "Lumi is a bar.\nIt answers." }, chunks);
    }

    [Fact]
    public void NormalizeKeepsParagraphsAndSquashesSpaces()
    {
        Assert.Equal("a\n\nb c", Chunker.Normalize("a\r\n\r\n\r\n\r\n   b\t\t c"));
    }

    [Fact]
    public void ChunksStayWithinTheSize()
    {
        var chunks = Chunker.Split(Words(2000));
        Assert.True(chunks.Count > 10);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, Chunker.Size));
        // Not cut needlessly short either: all but the last are at least 60% full.
        Assert.All(chunks.Take(chunks.Count - 1), c => Assert.True(c.Length >= Chunker.Size * 3 / 5 - 5, c.Length.ToString()));
    }

    [Fact]
    public void EveryWordLandsInSomeChunk()
    {
        const int n = 1500;
        var seen = Chunker.Split(Words(n)).SelectMany(Tokens).ToHashSet();
        for (var i = 0; i < n; i++) Assert.Contains("w" + i, seen);
    }

    [Fact]
    public void ChunksCutBetweenWords()
    {
        var all = Tokens(Words(1500)).ToHashSet();
        foreach (var chunk in Chunker.Split(Words(1500)))
        {
            var tokens = Tokens(chunk);
            Assert.Contains(tokens[0], all);
            Assert.Contains(tokens[^1], all);
        }
    }

    [Fact]
    public void NeighboursOverlap()
    {
        var chunks = Chunker.Split(Words(1500));
        for (var i = 0; i + 1 < chunks.Count; i++)
        {
            var next = Tokens(chunks[i + 1]);
            // The next chunk starts with words the previous one ended with.
            Assert.Contains(next[0], Tokens(chunks[i]));
            var shared = Tokens(chunks[i]).Intersect(next).Count();
            Assert.InRange(shared, 3, 30);
        }
    }

    [Fact]
    public void PrefersAParagraphBreak()
    {
        var first = string.Join(' ', Enumerable.Repeat("alpha", 60)); // 359 characters
        var second = string.Join(' ', Enumerable.Repeat("beta", 80));
        var chunks = Chunker.Split(first + "\n\n" + second);
        Assert.Equal(first, chunks[0]);
    }

    [Fact]
    public void PrefersTheEndOfASentence()
    {
        var chunks = Chunker.Split(Words(400));
        Assert.All(chunks.Take(chunks.Count - 1), c => Assert.EndsWith(".", c));
    }

    [Fact]
    public void TextWithoutSpacesIsStillCutAndFinishes()
    {
        var text = new string('a', 1000);
        var chunks = Chunker.Split(text);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, Chunker.Size));
        Assert.Equal(3, chunks.Count);
        Assert.Equal(1000 + 2 * Chunker.Overlap, chunks.Sum(c => c.Length));
    }

    [Fact]
    public void RejectsAnOverlapThatCouldStall()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Chunker.Split("text", 100, 60));
    }
}
