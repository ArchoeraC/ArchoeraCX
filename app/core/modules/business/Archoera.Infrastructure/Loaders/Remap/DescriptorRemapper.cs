using System.Text;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Maps JVM descriptors and generic signatures. Descriptors only contain class
/// names, so they are strict; signatures allow type variables, wildcards and
/// inner classes and are parsed structurally instead of with string replacement.
/// The output mirrors ASM's <c>SignatureReader</c>/<c>SignatureWriter</c> pair so
/// the remapped Utf8 is byte-identical to the one ASM produces.
/// </summary>
internal sealed class DescriptorRemapper
{
    private readonly Func<string, string> _mapClass;

    public DescriptorRemapper(Func<string, string> mapClass)
    {
        _mapClass = mapClass ?? throw new ArgumentNullException(nameof(mapClass));
    }

    public string MapDescriptor(string descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Length == 0)
        {
            return descriptor;
        }

        var builder = new StringBuilder(descriptor.Length + 16);
        var index = 0;
        while (index < descriptor.Length)
        {
            var character = descriptor[index];
            if (character == 'L')
            {
                var end = descriptor.IndexOf(';', index);
                if (end < 0)
                {
                    throw new ClassFileException($"descriptor '{descriptor}' has an unterminated class type");
                }

                var name = descriptor[(index + 1)..end];
                builder.Append('L').Append(_mapClass(name)).Append(';');
                index = end + 1;
                continue;
            }

            if (character is '[' or '(' or ')' or 'B' or 'C' or 'D' or 'F' or 'I' or 'J' or 'S' or 'Z' or 'V')
            {
                builder.Append(character);
                index++;
                continue;
            }

            throw new ClassFileException($"descriptor '{descriptor}' has an unexpected character '{character}'");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Maps a signature. <paramref name="typeSignature"/> selects the
    /// single-type grammar used by fields, record components and local variable
    /// type tables; otherwise the class or method grammar is auto-detected from
    /// the presence of a parameter list, exactly like ASM's reader.
    /// </summary>
    public string MapSignature(string signature, bool typeSignature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length == 0)
        {
            return signature;
        }

        var builder = new StringBuilder(signature.Length + 16);
        var index = 0;
        if (typeSignature)
        {
            ParseType(signature, ref index, builder);
        }
        else
        {
            if (signature[index] == '<')
            {
                ParseTypeParameters(signature, ref index, builder);
            }

            if (index < signature.Length && signature[index] == '(')
            {
                ParseMethodSignature(signature, ref index, builder);
            }
            else
            {
                ParseClassType(signature, ref index, builder);
                while (index < signature.Length)
                {
                    ParseClassType(signature, ref index, builder);
                }
            }
        }

        if (index != signature.Length)
        {
            throw new ClassFileException($"signature '{signature}' has trailing characters at {index}");
        }

        return builder.ToString();
    }

    private void ParseMethodSignature(string signature, ref int index, StringBuilder builder)
    {
        builder.Append('(');
        index++;
        while (index < signature.Length && signature[index] != ')')
        {
            ParseType(signature, ref index, builder);
        }

        if (index >= signature.Length)
        {
            throw new ClassFileException($"signature '{signature}' has an unterminated parameter list");
        }

        builder.Append(')');
        index++;
        if (index < signature.Length && signature[index] == 'V')
        {
            builder.Append('V');
            index++;
        }
        else
        {
            ParseType(signature, ref index, builder);
        }

        while (index < signature.Length && signature[index] == '^')
        {
            builder.Append('^');
            index++;
            if (index < signature.Length && signature[index] == 'T')
            {
                ParseTypeVariable(signature, ref index, builder);
            }
            else
            {
                ParseClassType(signature, ref index, builder);
            }
        }
    }

    private void ParseTypeParameters(string signature, ref int index, StringBuilder builder)
    {
        builder.Append('<');
        index++;
        while (true)
        {
            var start = index;
            while (index < signature.Length && signature[index] != ':')
            {
                index++;
            }

            if (index >= signature.Length)
            {
                throw new ClassFileException($"signature '{signature}' has an unterminated type parameter");
            }

            builder.Append(signature, start, index - start).Append(':');
            index++;
            if (index < signature.Length && signature[index] is 'L' or '[' or 'T')
            {
                ParseType(signature, ref index, builder);
            }

            while (index < signature.Length && signature[index] == ':')
            {
                builder.Append(':');
                index++;
                ParseType(signature, ref index, builder);
            }

            if (index >= signature.Length)
            {
                throw new ClassFileException($"signature '{signature}' has an unterminated type parameter list");
            }

            if (signature[index] == '>')
            {
                builder.Append('>');
                index++;
                return;
            }
        }
    }

    private void ParseType(string signature, ref int index, StringBuilder builder)
    {
        if (index >= signature.Length)
        {
            throw new ClassFileException($"signature '{signature}' is truncated");
        }

        switch (signature[index])
        {
            case 'L':
                ParseClassType(signature, ref index, builder);
                return;

            case 'T':
                ParseTypeVariable(signature, ref index, builder);
                return;

            case '[':
                builder.Append('[');
                index++;
                ParseType(signature, ref index, builder);
                return;

            case 'B':
            case 'C':
            case 'D':
            case 'F':
            case 'I':
            case 'J':
            case 'S':
            case 'Z':
            case 'V':
                builder.Append(signature[index]);
                index++;
                return;

            default:
                throw new ClassFileException(
                    $"signature '{signature}' has an unexpected character '{signature[index]}'");
        }
    }

    private void ParseTypeVariable(string signature, ref int index, StringBuilder builder)
    {
        var end = signature.IndexOf(';', index);
        if (end < 0)
        {
            throw new ClassFileException($"signature '{signature}' has an unterminated type variable");
        }

        builder.Append(signature, index, end - index + 1);
        index = end + 1;
    }

    private void ParseClassType(string signature, ref int index, StringBuilder builder)
    {
        if (index >= signature.Length || signature[index] != 'L')
        {
            throw new ClassFileException($"signature '{signature}' expected a class type at {index}");
        }

        builder.Append('L');
        index++;

        var start = index;
        while (index < signature.Length && signature[index] is not ('<' or '.' or ';'))
        {
            index++;
        }

        if (index == start)
        {
            throw new ClassFileException($"signature '{signature}' has an empty class name");
        }

        var fullName = signature[start..index];
        var mapped = _mapClass(fullName);
        builder.Append(mapped);

        if (index < signature.Length && signature[index] == '<')
        {
            ParseTypeArguments(signature, ref index, builder);
        }

        while (index < signature.Length && signature[index] == '.')
        {
            index++;
            var innerStart = index;
            while (index < signature.Length && signature[index] is not ('<' or '.' or ';'))
            {
                index++;
            }

            if (index == innerStart)
            {
                throw new ClassFileException($"signature '{signature}' has an empty inner class name");
            }

            var innerName = signature[innerStart..index];
            var innerFullName = fullName + "$" + innerName;
            var innerMapped = _mapClass(innerFullName);
            var outerPrefix = mapped + "$";
            var suffix = innerMapped.StartsWith(outerPrefix, StringComparison.Ordinal)
                ? innerMapped[outerPrefix.Length..]
                : innerMapped[(innerMapped.LastIndexOf('$') + 1)..];
            builder.Append('.').Append(suffix);
            fullName = innerFullName;
            mapped = innerMapped;

            if (index < signature.Length && signature[index] == '<')
            {
                ParseTypeArguments(signature, ref index, builder);
            }
        }

        if (index >= signature.Length || signature[index] != ';')
        {
            throw new ClassFileException($"signature '{signature}' has an unterminated class type");
        }

        builder.Append(';');
        index++;
    }

    private void ParseTypeArguments(string signature, ref int index, StringBuilder builder)
    {
        builder.Append('<');
        index++;
        while (index < signature.Length && signature[index] != '>')
        {
            var character = signature[index];
            if (character == '*')
            {
                builder.Append('*');
                index++;
            }
            else if (character is '+' or '-')
            {
                builder.Append(character);
                index++;
                ParseType(signature, ref index, builder);
            }
            else
            {
                ParseType(signature, ref index, builder);
            }
        }

        if (index >= signature.Length)
        {
            throw new ClassFileException($"signature '{signature}' has an unterminated type argument list");
        }

        builder.Append('>');
        index++;
    }
}
