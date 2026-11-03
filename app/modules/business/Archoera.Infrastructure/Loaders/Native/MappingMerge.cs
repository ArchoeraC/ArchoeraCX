using System.Text;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Reproduces <c>installertools --task MERGE_MAPPING</c>: project the left
/// TSRG2 file onto its first two namespaces, reverse the right ProGuard file,
/// replace the selected element kinds with the right file's names, and write a
/// two-column TSRG2 file. The output is hash-verified, so ordering (including
/// Java's HashMap overload tie-break) is part of the contract.
/// </summary>
internal static class MappingMerge
{
    public readonly record struct Options(
        bool Classes,
        bool Fields,
        bool Methods,
        bool Parameters);

    public static void Run(
        string leftPath,
        string rightPath,
        string outputPath,
        bool reverseLeft,
        bool reverseRight,
        Options options)
    {
        var left = ParseTsrg2(ReadLines(leftPath));
        var right = ParseProGuard(ReadLines(rightPath));
        if (reverseLeft)
        {
            left = Reverse(left);
        }

        if (reverseRight)
        {
            right = Reverse(right);
        }

        var merged = Merge(left, right, options);
        Write(merged, outputPath);
    }

    private static Mapping Merge(Mapping left, Mapping right, Options options)
    {
        var output = new Mapping();
        foreach (var package in left.Packages.Values)
        {
            var mapped = right.Packages.TryGetValue(package.Original, out var linked)
                ? linked.Mapped
                : package.Original;
            output.Packages[package.Original] = new PackageEntry(package.Original, mapped);
        }

        foreach (var source in left.Classes.Values)
        {
            var target = new ClassEntry(
                source.Original,
                options.Classes ? RemapClass(right, source.Original) : source.Mapped);
            right.Classes.TryGetValue(source.Original, out var link);

            foreach (var field in source.Fields.Values)
            {
                var mapped = field.Mapped;
                if (options.Fields &&
                    link is not null &&
                    link.Fields.TryGetValue(field.Original, out var linkedField))
                {
                    mapped = linkedField.Mapped;
                }

                target.Fields[field.Original] = new FieldEntry(
                    field.Original,
                    mapped,
                    field.Descriptor);
            }

            foreach (var methodKey in source.MethodOrder)
            {
                var method = source.Methods[methodKey];
                var mapped = method.Mapped;
                MethodEntry? linkedMethod = null;
                if (link is not null)
                {
                    link.Methods.TryGetValue(methodKey, out linkedMethod);
                }

                if (options.Methods && linkedMethod is not null)
                {
                    mapped = linkedMethod.Mapped;
                }

                var targetMethod = new MethodEntry(
                    method.Original,
                    mapped,
                    method.Descriptor,
                    method.IsStatic);
                foreach (var parameter in method.Parameters)
                {
                    var parameterName = parameter.Value.Mapped;
                    if (options.Parameters &&
                        linkedMethod is not null &&
                        linkedMethod.Parameters.TryGetValue(parameter.Key, out var linkedParameter))
                    {
                        parameterName = linkedParameter.Mapped;
                    }

                    targetMethod.Parameters[parameter.Key] = new ParameterEntry(
                        parameter.Value.Original,
                        parameterName);
                }

                target.Methods[methodKey] = targetMethod;
                target.MethodOrder.Add(methodKey);
            }

            output.Classes[target.Original] = target;
        }

        return output;
    }

    private static string RemapClass(Mapping mapping, string name)
    {
        if (mapping.Classes.TryGetValue(name, out var entry))
        {
            return entry.Mapped;
        }

        var dollar = name.LastIndexOf('$');
        if (dollar >= 0)
        {
            return RemapClass(mapping, name[..dollar]) + name[dollar..];
        }

        return name;
    }

    private static string RemapDescriptor(Mapping mapping, string descriptor)
    {
        var builder = new StringBuilder(descriptor.Length);
        for (var index = 0; index < descriptor.Length; index++)
        {
            var value = descriptor[index];
            if (value != 'L')
            {
                builder.Append(value);
                continue;
            }

            var end = descriptor.IndexOf(';', index + 1);
            if (end < 0)
            {
                builder.Append(descriptor[index..]);
                break;
            }

            var name = descriptor[(index + 1)..end];
            builder.Append('L').Append(RemapClass(mapping, name)).Append(';');
            index = end;
        }

        return builder.ToString();
    }

    private static Mapping Reverse(Mapping source)
    {
        var reversed = new Mapping();
        foreach (var package in source.Packages.Values)
        {
            reversed.Packages[package.Mapped] = new PackageEntry(package.Mapped, package.Original);
        }

        foreach (var entry in source.Classes.Values)
        {
            var target = new ClassEntry(entry.Mapped, entry.Original);
            foreach (var field in entry.Fields.Values)
            {
                target.Fields[field.Mapped] = new FieldEntry(
                    field.Mapped,
                    field.Original,
                    field.Descriptor);
            }

            foreach (var methodKey in entry.MethodOrder)
            {
                var method = entry.Methods[methodKey];
                var descriptor = RemapDescriptor(source, method.Descriptor);
                var targetMethod = new MethodEntry(
                    method.Mapped,
                    method.Original,
                    descriptor,
                    method.IsStatic);
                foreach (var parameter in method.Parameters)
                {
                    targetMethod.Parameters[parameter.Key] = new ParameterEntry(
                        parameter.Value.Mapped,
                        parameter.Value.Original);
                }

                var key = method.Mapped + descriptor;
                target.Methods[key] = targetMethod;
                target.MethodOrder.Add(key);
            }

            reversed.Classes[target.Original] = target;
        }

        return reversed;
    }

    private static Mapping ParseTsrg2(IReadOnlyList<string> rawLines)
    {
        var lines = new List<string>(rawLines.Count);
        foreach (var raw in rawLines)
        {
            var line = StripComment(raw);
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        var mapping = new Mapping();
        if (lines.Count == 0)
        {
            return mapping;
        }

        var header = JavaSplit(lines[0]);
        if (header.Length < 3 || header[0] != "tsrg2")
        {
            throw new InvalidDataException("left mapping is not TSRG2");
        }

        var nameCount = header.Length - 1;
        ClassEntry? currentClass = null;
        MethodEntry? currentMethod = null;
        for (var lineIndex = 1; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var parts = JavaSplit(line);
            if (line[0] != '\t')
            {
                if (parts.Length != nameCount)
                {
                    throw new InvalidDataException($"bad TSRG2 class line: {line}");
                }

                if (parts[0].EndsWith('/'))
                {
                    var original = parts[0][..^1];
                    var mapped = parts[1][..^1];
                    mapping.Packages[original] = new PackageEntry(original, mapped);
                    currentClass = null;
                }
                else
                {
                    currentClass = new ClassEntry(parts[0], parts[1]);
                    mapping.Classes[currentClass.Original] = currentClass;
                }

                currentMethod = null;
                continue;
            }

            if (line.Length > 1 && line[1] == '\t')
            {
                if (currentMethod is null)
                {
                    throw new InvalidDataException($"orphan TSRG2 method line: {line}");
                }

                var nested = JavaSplit(line[2..]);
                if (nested.Length == 1 && nested[0] == "static")
                {
                    currentMethod.IsStatic = true;
                }
                else if (nested.Length == nameCount + 1 &&
                         int.TryParse(nested[0], out var parameterIndex))
                {
                    currentMethod.Parameters[parameterIndex] = new ParameterEntry(
                        nested[1],
                        nested[2]);
                }
                else
                {
                    throw new InvalidDataException($"bad TSRG2 method line: {line}");
                }

                continue;
            }

            if (currentClass is null)
            {
                throw new InvalidDataException($"orphan TSRG2 member line: {line}");
            }

            var member = JavaSplit(line[1..]);
            if (member.Length == nameCount)
            {
                currentClass.Fields[member[0]] = new FieldEntry(member[0], member[1], null);
                currentMethod = null;
                continue;
            }

            if (member.Length != nameCount + 1)
            {
                throw new InvalidDataException($"bad TSRG2 member line: {line}");
            }

            (member[0], member[1]) = (member[1], member[0]);
            if (member[0].StartsWith('('))
            {
                var method = new MethodEntry(member[1], member[2], member[0], false);
                var key = method.Original + method.Descriptor;
                currentClass.Methods[key] = method;
                currentClass.MethodOrder.Add(key);
                currentMethod = method;
            }
            else
            {
                currentClass.Fields[member[1]] = new FieldEntry(member[1], member[2], member[0]);
                currentMethod = null;
            }
        }

        return mapping;
    }

    private static Mapping ParseProGuard(IReadOnlyList<string> rawLines)
    {
        var mapping = new Mapping();
        var lines = new List<string>(rawLines.Count);
        foreach (var raw in rawLines)
        {
            var line = StripComment(raw);
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        ClassEntry? currentClass = null;
        foreach (var raw in lines)
        {
            var line = raw.Replace('.', '/');
            if (!line.StartsWith("    ", StringComparison.Ordinal) && line.EndsWith(':'))
            {
                var arrow = line.Split(" -> ");
                currentClass = new ClassEntry(arrow[0], arrow[1][..^1]);
                mapping.Classes[currentClass.Original] = currentClass;
                continue;
            }

            if (line.Contains('(') && line.Contains(')'))
            {
                if (currentClass is null)
                {
                    throw new InvalidDataException($"orphan ProGuard method line: {line}");
                }

                var methodLine = line.Trim();
                if (methodLine.Contains(':'))
                {
                    var first = methodLine.IndexOf(':');
                    var second = methodLine.IndexOf(':', first + 1);
                    methodLine = methodLine[(second + 1)..];
                }

                var obfuscated = methodLine.Split(" -> ")[1];
                var firstSpace = methodLine.IndexOf(' ');
                var firstParen = methodLine.IndexOf('(');
                var lastParen = methodLine.IndexOf(')');
                var returnType = ToDescriptor(methodLine[..firstSpace]);
                var name = methodLine[(firstSpace + 1)..firstParen];
                var arguments = methodLine[(firstParen + 1)..lastParen]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries);
                var descriptor = new StringBuilder("(");
                foreach (var argument in arguments)
                {
                    descriptor.Append(ToDescriptor(argument));
                }

                descriptor.Append(')').Append(returnType);
                var method = new MethodEntry(name, obfuscated, descriptor.ToString(), false);
                var key = name + descriptor;
                currentClass.Methods[key] = method;
                currentClass.MethodOrder.Add(key);
                continue;
            }

            if (currentClass is null)
            {
                throw new InvalidDataException($"orphan ProGuard field line: {line}");
            }

            var field = JavaSplit(line.Trim());
            if (field.Length < 4)
            {
                throw new InvalidDataException($"bad ProGuard field line: {line}");
            }

            currentClass.Fields[field[1]] = new FieldEntry(
                field[1],
                field[3],
                ToDescriptor(field[0]));
        }

        return mapping;
    }

    private static string ToDescriptor(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            return "[" + ToDescriptor(type[..^2]);
        }

        return type switch
        {
            "int" => "I",
            "void" => "V",
            "boolean" => "Z",
            "byte" => "B",
            "char" => "C",
            "short" => "S",
            "double" => "D",
            "float" => "F",
            "long" => "J",
            _ when type.Contains('/') => "L" + type + ";",
            _ => throw new InvalidDataException($"unsupported ProGuard type: {type}"),
        };
    }

    private static void Write(Mapping mapping, string path)
    {
        using var writer = new StreamWriter(
            path,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.NewLine = "\n";
        writer.Write("tsrg2 left right\n");
        foreach (var package in mapping.Packages.Values.OrderBy(item => item.Original, StringComparer.Ordinal))
        {
            writer.Write((package.Original.Length == 0 ? "." : package.Original) + "/ ");
            writer.Write((package.Mapped.Length == 0 ? "." : package.Mapped) + "/\n");
        }

        foreach (var entry in mapping.Classes.Values.OrderBy(item => item.Original, StringComparer.Ordinal))
        {
            writer.Write(entry.Original + " " + entry.Mapped + "\n");
            foreach (var field in entry.Fields.Values.OrderBy(item => item.Original, StringComparer.Ordinal))
            {
                writer.Write('\t');
                writer.Write(field.Original);
                if (field.Descriptor is not null)
                {
                    writer.Write(' ');
                    writer.Write(field.Descriptor);
                }

                writer.Write(' ');
                writer.Write(field.Mapped);
                writer.Write('\n');
            }

            var order = JavaHashMap.Order(entry.MethodOrder)
                .OrderBy(key => entry.Methods[key].Original, StringComparer.Ordinal);
            foreach (var key in order)
            {
                var method = entry.Methods[key];
                writer.Write('\t' + method.Original + ' ' + method.Descriptor + ' ' + method.Mapped + "\n");
                if (method.IsStatic)
                {
                    writer.Write("\t\tstatic\n");
                }

                foreach (var parameter in method.Parameters.OrderBy(item => item.Key))
                {
                    writer.Write($"\t\t{parameter.Key} {parameter.Value.Original} " +
                        $"{parameter.Value.Mapped}\n");
                }
            }
        }
    }

    private static IReadOnlyList<string> ReadLines(string path)
    {
        var lines = new List<string>();
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    /// <summary>
    /// srgutils' <c>stripComment</c>, including the off-by-one that drops the
    /// character before a <c>#</c>.
    /// </summary>
    private static string StripComment(string value)
    {
        var hash = value.IndexOf('#');
        if (hash == 0)
        {
            return string.Empty;
        }

        if (hash > 0)
        {
            value = value[..(hash - 1)];
        }

        var end = value.Length;
        while (end > 1 && value[end - 1] == ' ')
        {
            end--;
        }

        return end == 0 ? string.Empty : value[..end];
    }

    private static string[] JavaSplit(string value)
    {
        var parts = value.Split(' ');
        var length = parts.Length;
        while (length > 0 && parts[length - 1].Length == 0)
        {
            length--;
        }

        if (length == parts.Length)
        {
            return parts;
        }

        var trimmed = new string[length];
        Array.Copy(parts, trimmed, length);
        return trimmed;
    }

    private sealed class Mapping
    {
        public Dictionary<string, PackageEntry> Packages { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ClassEntry> Classes { get; } = new(StringComparer.Ordinal);
    }

    private sealed record PackageEntry(string Original, string Mapped);

    private sealed class ClassEntry(string original, string mapped)
    {
        public string Original { get; } = original;

        public string Mapped { get; set; } = mapped;

        public Dictionary<string, FieldEntry> Fields { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, MethodEntry> Methods { get; } = new(StringComparer.Ordinal);

        public List<string> MethodOrder { get; } = [];
    }

    private sealed record FieldEntry(string Original, string Mapped, string? Descriptor);

    private sealed class MethodEntry(
        string original,
        string mapped,
        string descriptor,
        bool isStatic)
    {
        public string Original { get; } = original;

        public string Mapped { get; set; } = mapped;

        public string Descriptor { get; } = descriptor;

        public bool IsStatic { get; set; } = isStatic;

        public SortedDictionary<int, ParameterEntry> Parameters { get; } = [];
    }

    private sealed record ParameterEntry(string Original, string Mapped);
}
