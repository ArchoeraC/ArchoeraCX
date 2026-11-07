using System.Text;

namespace Archoera.Infrastructure.Launch;

/// <summary>
/// Splits a user-entered command line (<c>jvmArgs</c>, <c>gameArgs</c>,
/// <c>wrapperCommand</c>) without a shell. The rules are intentionally small
/// and platform-independent: whitespace separates arguments, single and double
/// quotes group text, and a backslash escapes <c>"</c> or <c>\</c> outside
/// single quotes. <c>""</c> produces an empty argument, which is the only way
/// to pass an empty value through a plain text field.
///
/// <para>Unclosed quotes and a trailing lone backslash are errors instead of
/// being silently repaired: a mistyped wrapper would otherwise start the game
/// with a surprising command line.</para>
/// </summary>
internal static class CommandLineTokenizer
{
    /// <summary>Tokenizes <paramref name="value"/> into arguments.</summary>
    /// <exception cref="FormatException">Unclosed quote or trailing backslash.</exception>
    public static IReadOnlyList<string> Split(string? value)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(value)) return result;

        var current = new StringBuilder();
        var hasToken = false;
        char quote = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote == '\0')
            {
                if (char.IsWhiteSpace(character))
                {
                    if (hasToken)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }

                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                    hasToken = true;
                    continue;
                }

                if (character == '\\')
                {
                    if (index + 1 >= value.Length)
                    {
                        // Windows paths commonly end in a separator; a lone
                        // trailing backslash is literal, not an escape.
                        current.Append(character);
                        hasToken = true;
                        continue;
                    }

                    var next = value[index + 1];
                    if (next is '"' or '\\')
                    {
                        current.Append(next);
                        index++;
                    }
                    else
                    {
                        // No shell: only the two documented escapes are special.
                        current.Append(character);
                    }

                    hasToken = true;
                    continue;
                }

                current.Append(character);
                hasToken = true;
                continue;
            }

            if (character == quote)
            {
                quote = '\0';
                hasToken = true;
                continue;
            }

            if (quote == '"' && character == '\\')
            {
                if (index + 1 >= value.Length)
                {
                    // A trailing separator is data; the quote is still open and
                    // the final check reports the real mistake.
                    current.Append(character);
                    hasToken = true;
                    continue;
                }

                var next = value[index + 1];
                if (next is '"' or '\\')
                {
                    current.Append(next);
                    index++;
                }
                else
                {
                    current.Append(character);
                }

                hasToken = true;
                continue;
            }

            current.Append(character);
            hasToken = true;
        }

        if (quote != '\0')
        {
            throw new FormatException(
                quote == '"' ? "unclosed double quote" : "unclosed single quote");
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
