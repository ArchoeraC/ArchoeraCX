using Archoera.Infrastructure.Launch;

namespace Archoera.Tests;

/// <summary>
/// The tokenizer is the only place user text becomes an argument list, so its
/// quoting and error rules are pinned here for JVM args, game args and the
/// wrapper command alike.
/// </summary>
public sealed class CommandLineTokenizerTests
{
    [Fact]
    public void Plain_words_are_split_on_whitespace()
    {
        Assert.Equal(
            ["-Xmx4G", "--demo", "--width", "1280"],
            CommandLineTokenizer.Split("-Xmx4G   --demo\t--width\n1280"));
    }

    [Fact]
    public void Double_quotes_group_text_and_keep_inner_spaces()
    {
        Assert.Equal(
            ["--title", "My World", "-Dname=a b"],
            CommandLineTokenizer.Split("--title \"My World\" -Dname=\"a b\""));
    }

    [Fact]
    public void Single_quotes_group_text_without_escapes()
    {
        Assert.Equal(
            ["prime-run", "my wrapper", @"C:\Games\Java"],
            CommandLineTokenizer.Split("prime-run 'my wrapper' 'C:\\Games\\Java'"));
    }

    [Fact]
    public void Adjacent_quoted_and_plain_segments_form_one_argument()
    {
        Assert.Equal(
            ["--path=/opt/my games/bin"],
            CommandLineTokenizer.Split("--path=\"/opt/my games\"/bin"));
    }

    [Fact]
    public void Empty_quotes_produce_an_empty_argument()
    {
        Assert.Equal([string.Empty], CommandLineTokenizer.Split("\"\""));
        Assert.Equal(["a", string.Empty, "b"], CommandLineTokenizer.Split("a '' b"));
    }

    [Fact]
    public void Backslash_escapes_quotes_and_backslashes()
    {
        Assert.Equal(["a\"b"], CommandLineTokenizer.Split("a\\\"b"));
        Assert.Equal([@"a\b"], CommandLineTokenizer.Split("a\\\\b"));
        Assert.Equal([@"a\b"], CommandLineTokenizer.Split("a\\b"));
    }

    [Fact]
    public void Trailing_backslash_is_literal_for_windows_paths()
    {
        Assert.Equal([@"C:\natives\"], CommandLineTokenizer.Split(@"C:\natives\"));
    }

    [Fact]
    public void Empty_input_yields_no_arguments()
    {
        Assert.Empty(CommandLineTokenizer.Split(null));
        Assert.Empty(CommandLineTokenizer.Split(string.Empty));
        Assert.Empty(CommandLineTokenizer.Split("   \t "));
    }

    [Theory]
    [InlineData("\"unterminated")]
    [InlineData("'unterminated")]
    [InlineData("\"abc\\")]
    public void Unclosed_quotes_are_errors(string value)
    {
        Assert.Throws<FormatException>(() => CommandLineTokenizer.Split(value));
    }
}
