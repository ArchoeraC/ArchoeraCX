using System.Diagnostics.CodeAnalysis;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// TSRG v1/v2 mapping reader. v1 is what <c>DOWNLOAD_MOJMAPS</c> and
/// <c>MERGE_MAPPING</c> emit for the installer; v2 is what modern MCP/NeoForm
/// zips contain (<c>config/joined.tsrg</c>). Both are stored with internal
/// names (slash separated) and JVM descriptors.
/// </summary>
internal sealed class TsrgMapping
{
    private readonly Dictionary<string, string> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fieldsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fieldsByDescriptor = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _methods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, string>> _parameters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _staticMethods = new(StringComparer.Ordinal);

    private TsrgMapping(string sourceNamespace, string targetNamespace)
    {
        SourceNamespace = sourceNamespace;
        TargetNamespace = targetNamespace;
    }

    public string SourceNamespace { get; }

    public string TargetNamespace { get; }

    public int ClassCount => _classes.Count;

    public int MemberCount => _methods.Count + _fieldsByName.Count;

    /// <summary>Identity mapping used by the fixer passes, which run after renaming.</summary>
    public static TsrgMapping Empty { get; } = new("src", "dst");

    public static TsrgMapping ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var reader = new StreamReader(
            File.OpenRead(path),
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return Parse(reader);
    }

    public static TsrgMapping ParseText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    public static TsrgMapping Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var source = "src";
        var target = "dst";
        var version = 1;
        var nameCount = 2;
        string? currentClass = null;
        string? currentMethod = null;
        var mapping = new TsrgMapping(source, target);
        var lineNumber = 0;

        for (var line = reader.ReadLine(); line is not null; line = reader.ReadLine())
        {
            lineNumber++;
            line = line.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line.StartsWith("tsrg2 ", StringComparison.Ordinal))
            {
                var header = SplitTokens(line);
                if (header.Length < 3)
                {
                    throw Invalid(lineNumber, "tsrg2 header needs source and target namespaces");
                }

                version = 2;
                source = header[1];
                target = header[2];
                nameCount = header.Length - 1;
                mapping = new TsrgMapping(source, target);
                continue;
            }

            var tabCount = CountLeadingTabs(line);
            if (tabCount >= 2)
            {
                if (currentMethod is not null)
                {
                    var nested = SplitTokens(line[tabCount..]);
                    if (nested.Length > 0 && nested[0] == "static")
                    {
                        mapping._staticMethods.Add(currentMethod);
                    }
                    else if (nested.Length >= 2 && int.TryParse(nested[0], out var parameterIndex))
                    {
                        var targetIndex = Math.Min(2, nested.Length - 1);
                        mapping._parameters[currentMethod][parameterIndex] = nested[targetIndex];
                    }
                }

                // Parameter names / flags belong to the current method; they do
                // not affect class or member remapping.
                continue;
            }

            if (tabCount == 1)
            {
                if (currentClass is null)
                {
                    throw Invalid(lineNumber, "member line appears before a class line");
                }

                var tokens = SplitTokens(line[1..]);
                if (tokens.Length < 2)
                {
                    throw Invalid(lineNumber, "member line needs at least a name and a target");
                }

                if (version == 1)
                {
                    if (tokens.Length >= 3)
                    {
                        currentMethod = mapping.AddMethod(currentClass, tokens[0], tokens[1], tokens[2]);
                    }
                    else
                    {
                        mapping.AddField(currentClass, tokens[0], null, tokens[1]);
                        currentMethod = null;
                    }
                }
                else if (tokens.Length >= 1 + nameCount && tokens[1].StartsWith('('))
                {
                    currentMethod = mapping.AddMethod(currentClass, tokens[0], tokens[1], tokens[2]);
                }
                else if (tokens.Length >= 1 + nameCount)
                {
                    mapping.AddField(currentClass, tokens[0], tokens[1], tokens[2]);
                    currentMethod = null;
                }
                else
                {
                    mapping.AddField(currentClass, tokens[0], null, tokens[1]);
                    currentMethod = null;
                }

                continue;
            }

            var classTokens = SplitTokens(line);
            if (classTokens.Length < 2)
            {
                throw Invalid(lineNumber, "class line needs a source and a target name");
            }

            currentClass = classTokens[0];
            currentMethod = null;
            if (version == 1 && classTokens.Length > 2)
            {
                // Some v1 writers append the numeric id; it is not a name.
                classTokens = [classTokens[0], classTokens[1]];
            }

            mapping.AddClass(classTokens[0], classTokens[1]);
        }

        return mapping;
    }

    public bool TryMapClass(string name, [NotNullWhen(true)] out string? mapped)
        => _classes.TryGetValue(name, out mapped);

    public bool TryMapField(
        string owner,
        string name,
        string? descriptor,
        [NotNullWhen(true)] out string? mapped)
    {
        if (descriptor is not null &&
            _fieldsByDescriptor.TryGetValue(FieldKey(owner, name, descriptor), out mapped))
        {
            return true;
        }

        return _fieldsByName.TryGetValue(FieldKey(owner, name, null), out mapped);
    }

    public bool TryMapMethod(
        string owner,
        string name,
        string descriptor,
        [NotNullWhen(true)] out string? mapped)
        => _methods.TryGetValue(MethodKey(owner, name, descriptor), out mapped);

    /// <summary>
    /// Maps a LocalVariableTable entry name the way ART's EnhancedRemapper does:
    /// explicit parameter names are indexed from the mapping, non-static methods
    /// reserve slot 0 for <c>this</c>, and long/double parameters occupy two
    /// slots with the same name.
    /// </summary>
    public bool TryMapParameter(
        string owner,
        string name,
        string descriptor,
        bool isStatic,
        int localIndex,
        out string mapped)
    {
        mapped = string.Empty;
        if (!_parameters.TryGetValue(MethodKey(owner, name, descriptor), out var explicitNames))
        {
            return false;
        }

        var sizes = ArgumentSizes(descriptor).ToList();
        if (sizes.Count == 0)
        {
            // ART only builds a parameter list for methods that take arguments.
            return false;
        }

        var names = new List<string?>();
        if (!isStatic)
        {
            names.Add("this");
        }

        for (var argumentIndex = 0; argumentIndex < sizes.Count; argumentIndex++)
        {
            // A missing explicit name stays null, and ART keeps the original
            // LocalVariableTable name in that case.
            string? value = explicitNames.TryGetValue(argumentIndex, out var explicitName)
                ? explicitName
                : null;
            names.Add(value);
            if (sizes[argumentIndex] == 2)
            {
                names.Add(value);
            }
        }

        if (localIndex < 0 || localIndex >= names.Count || names[localIndex] is not { } result)
        {
            return false;
        }

        mapped = result;
        return true;
    }

    public bool IsStaticMethod(string owner, string name, string descriptor)
        => _staticMethods.Contains(MethodKey(owner, name, descriptor));

    internal IReadOnlyDictionary<string, string> Classes => _classes;

    private void AddClass(string source, string target)
        => _classes[source] = target;

    private void AddField(string owner, string name, string? descriptor, string target)
    {
        if (descriptor is null)
        {
            _fieldsByName[FieldKey(owner, name, null)] = target;
        }
        else
        {
            _fieldsByDescriptor[FieldKey(owner, name, descriptor)] = target;
        }
    }

    private string AddMethod(string owner, string name, string descriptor, string target)
    {
        var key = MethodKey(owner, name, descriptor);
        _methods[key] = target;
        _parameters[key] = [];
        return key;
    }

    private static IEnumerable<int> ArgumentSizes(string descriptor)
    {
        if (descriptor.Length < 3 || descriptor[0] != '(')
        {
            yield break;
        }

        var index = 1;
        while (index < descriptor.Length && descriptor[index] != ')')
        {
            switch (descriptor[index])
            {
                case 'B':
                case 'C':
                case 'F':
                case 'I':
                case 'S':
                case 'Z':
                    index++;
                    yield return 1;
                    break;
                case 'J':
                case 'D':
                    index++;
                    yield return 2;
                    break;
                case 'L':
                {
                    var end = descriptor.IndexOf(';', index);
                    if (end < 0)
                    {
                        yield break;
                    }

                    index = end + 1;
                    yield return 1;
                    break;
                }

                case '[':
                {
                    index++;
                    while (index < descriptor.Length && descriptor[index] == '[')
                    {
                        index++;
                    }

                    if (index < descriptor.Length && descriptor[index] == 'L')
                    {
                        var end = descriptor.IndexOf(';', index);
                        if (end < 0)
                        {
                            yield break;
                        }

                        index = end + 1;
                    }
                    else
                    {
                        index++;
                    }

                    yield return 1;
                    break;
                }

                default:
                    yield break;
            }
        }
    }

    private static string FieldKey(string owner, string name, string? descriptor)
        => descriptor is null ? $"{owner}\0{name}" : $"{owner}\0{name}\0{descriptor}";

    private static string MethodKey(string owner, string name, string descriptor)
        => $"{owner}\0{name}\0{descriptor}";

    private static string[] SplitTokens(string value)
        => value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int CountLeadingTabs(string value)
    {
        var count = 0;
        while (count < value.Length && value[count] == '\t')
        {
            count++;
        }

        return count;
    }

    private static ClassFileException Invalid(int line, string message)
        => new($"TSRG line {line}: {message}");
}
