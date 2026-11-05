using PowerBIQualityGate.Cli.Rendering;

namespace PowerBIQualityGate.Tests;

public sealed class PlainTextOutputWriterTests
{
    [Fact]
    public void NoColorOutput_FiltersAnsiCsiAcrossWriteBoundaries()
    {
        using var sink = new StringWriter();
        var console = ConsoleFactory.Create(sink, noColor: true);

        console.Profile.Out.Writer.Write("First ");
        console.Profile.Out.Writer.Write('\u001b');
        console.Profile.Out.Writer.Write("[1;38;2;140;240;179m");
        console.Profile.Out.Writer.Write("green");
        console.Profile.Out.Writer.Write("\u001b[0m plain");

        Assert.Equal("First green plain", sink.ToString());
        Assert.False(console.Profile.Capabilities.Ansi);
    }

    [Fact]
    public void NoColorOutput_FiltersOscLinksAndOtherControlCharacters()
    {
        using var sink = new StringWriter();
        var console = ConsoleFactory.Create(sink, noColor: true);

        console.Profile.Out.Writer.Write("A\u001b]8;;https://example.org\u001b\\B");
        console.Profile.Out.Writer.Write("\u001b]8;;\aC");
        console.Profile.Out.Writer.Write("\u0007D\r\nE\tF");

        Assert.Equal("ABCD\r\nE\tF", sink.ToString());
    }

    [Fact]
    public void NoColorOutput_PreservesOrdinaryUnicodeAndMarkupLookingText()
    {
        using var sink = new StringWriter();
        var console = ConsoleFactory.Create(sink, noColor: true);

        console.Profile.Out.Writer.Write("🌵 PBIQ [red]literal[/] ✓ Sales[ProductId]");

        Assert.Equal("🌵 PBIQ [red]literal[/] ✓ Sales[ProductId]", sink.ToString());
    }
}
