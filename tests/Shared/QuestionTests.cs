using Xunit;

namespace Lumi.Tests;

/// What the bar does with typed text: a question selects "Ask <model>", anything else lands on the first app or file.
/// Same rule on Linux and Windows (and in Sources/main.swift on the Mac).
public class QuestionTests
{
    [Theory]
    // Ends with a question mark.
    [InlineData("inch in cm?")]
    [InlineData("firefox?")]
    [InlineData("  weather?  ")]
    // Four words or more.
    [InlineData("best pizza near me")]
    [InlineData("rename all photos by date")]
    // Starts with a question word, in each interface language.
    [InlineData("what is rust")]
    [InlineData("How tall")]
    [InlineData("explain monads")]
    [InlineData("translate hello")]
    [InlineData("что такое DNS")]
    [InlineData("Как дела")]
    [InlineData("сколько времени")]
    [InlineData("wie spät")]
    [InlineData("warum")]
    [InlineData("übersetze hallo")]
    [InlineData("cómo estás")]
    [InlineData("qué hora")]
    [InlineData("comment ça")]
    [InlineData("pourquoi")]
    [InlineData("où est")]
    public void Questions_go_to_the_model(string text) =>
        Assert.True(LauncherWindow.LooksLikeQuestion(text), $"\"{text}\" should be treated as a question");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("firefox")]
    [InlineData("Terminal")]
    [InlineData("libre office")]
    [InlineData("report 2024.pdf")]
    [InlineData("visual studio code")]
    [InlineData("whatsapp")] // starts with "what" but is not the word "what"
    [InlineData("telegram desktop")]
    [InlineData("настройки")]
    public void Searches_stay_searches(string text) =>
        Assert.False(LauncherWindow.LooksLikeQuestion(text), $"\"{text}\" should be treated as a search");
}
