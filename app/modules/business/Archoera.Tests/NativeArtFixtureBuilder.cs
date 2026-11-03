using System.Buffers.Binary;
using System.Text;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

/// <summary>
/// Emits the smallest valid class files needed by the native ART reader tests.
/// Keeping the fixture in code avoids shipping any Mojang-derived bytes.
/// </summary>
internal static class NativeArtFixtureBuilder
{
    public static byte[] EmptyClass(string name = "fixture/Sample", int major = 65)
    {
        var entries = new List<byte[]>
        {
            Utf8Entry(name),
            SingleEntry(7, 1),
            Utf8Entry("java/lang/Object"),
            SingleEntry(7, 3),
        };

        return Build(entries, major, access: 0x0021, thisClass: 2, superClass: 4);
    }

    public static byte[] ClassWithClassAttribute(string attributeName)
    {
        var entries = new List<byte[]>
        {
            Utf8Entry("fixture/Sample"),
            SingleEntry(7, 1),
            Utf8Entry("java/lang/Object"),
            SingleEntry(7, 3),
            Utf8Entry(attributeName),
        };

        var attribute = new byte[2 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(attribute, 5);
        BinaryPrimitives.WriteUInt32BigEndian(attribute.AsSpan(2), 0);

        return Build(
            entries,
            major: 65,
            access: 0x0021,
            thisClass: 2,
            superClass: 4,
            attributes: [attribute]);
    }

    /// <summary>
    /// Builds a class whose pool ends with <c>Class(className)</c> preceded by
    /// <paramref name="paddingEntries"/> filler Utf8 entries, so the class
    /// constant can sit on either side of the 255 index boundary.
    /// </summary>
    public static byte[] ClassWithClassConstant(string className, int paddingEntries)
    {
        var entries = new List<byte[]>
        {
            Utf8Entry("fixture/Sample"),
            SingleEntry(7, 1),
            Utf8Entry("java/lang/Object"),
            SingleEntry(7, 3),
        };

        for (var index = 0; index < paddingEntries; index++)
        {
            entries.Add(Utf8Entry($"fixture/Filler{index}"));
        }

        entries.Add(Utf8Entry(className));
        entries.Add(SingleEntry(7, entries.Count));

        return Build(entries, major: 65, access: 0x0021, thisClass: 2, superClass: 4);
    }

    /// <summary>
    /// Builds a class with one method <c>m()V</c> whose LocalVariableTable uses
    /// <paramref name="localName"/> for slot 0, so the identifier fixer can be
    /// exercised without the ART oracle jars.
    /// </summary>
    public static byte[] ClassWithLocalVariableName(string localName)
    {
        var entries = new List<byte[]>
        {
            Utf8Entry("fixture/Sample"),
            SingleEntry(7, 1),
            Utf8Entry("java/lang/Object"),
            SingleEntry(7, 3),
            Utf8Entry("m"),
            Utf8Entry("()V"),
            Utf8Entry("Code"),
            Utf8Entry("LocalVariableTable"),
            Utf8Entry(localName),
            Utf8Entry("I"),
        };

        var localTable = new byte[2 + 10];
        BinaryPrimitives.WriteUInt16BigEndian(localTable, 1);
        BinaryPrimitives.WriteUInt16BigEndian(localTable.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(localTable.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(localTable.AsSpan(6), 9);
        BinaryPrimitives.WriteUInt16BigEndian(localTable.AsSpan(8), 10);
        BinaryPrimitives.WriteUInt16BigEndian(localTable.AsSpan(10), 0);

        var code = new byte[2 + 2 + 4 + 1 + 2 + 2 + 6 + localTable.Length];
        var offset = 0;
        WriteU2(code.AsSpan(offset), 0); // max_stack
        offset += 2;
        WriteU2(code.AsSpan(offset), 1); // max_locals
        offset += 2;
        BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(offset), 1); // code_length
        offset += 4;
        code[offset++] = 0xB1; // return
        WriteU2(code.AsSpan(offset), 0); // exception_table_length
        offset += 2;
        WriteU2(code.AsSpan(offset), 1); // attributes_count
        offset += 2;
        WriteU2(code.AsSpan(offset), 8); // attribute_name_index
        offset += 2;
        BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(offset), (uint)localTable.Length);
        offset += 4;
        localTable.CopyTo(code.AsSpan(offset));

        return Build(
            entries,
            major: 65,
            access: 0x0021,
            thisClass: 2,
            superClass: 4,
            methods: [Method(0x0001, 5, 6, 7, code)]);
    }

    private static byte[] Method(int access, int name, int descriptor, int attributeName, byte[] payload)
    {
        var method = new byte[8 + 6 + payload.Length];
        WriteU2(method.AsSpan(0), (ushort)access);
        WriteU2(method.AsSpan(2), (ushort)name);
        WriteU2(method.AsSpan(4), (ushort)descriptor);
        WriteU2(method.AsSpan(6), 1);
        WriteU2(method.AsSpan(8), (ushort)attributeName);
        BinaryPrimitives.WriteUInt32BigEndian(method.AsSpan(10), (uint)payload.Length);
        payload.CopyTo(method.AsSpan(14));
        return method;
    }

    private static byte[] Build(
        List<byte[]> entries,
        int major,
        int access,
        int thisClass,
        int superClass,
        IReadOnlyList<byte[]>? attributes = null,
        IReadOnlyList<byte[]>? methods = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write((byte)0xCA);
        writer.Write((byte)0xFE);
        writer.Write((byte)0xBA);
        writer.Write((byte)0xBE);
        WriteU2(writer, 0);
        WriteU2(writer, (ushort)major);
        WriteU2(writer, (ushort)(entries.Count + 1));
        foreach (var entry in entries)
        {
            writer.Write(entry);
        }

        WriteU2(writer, (ushort)access);
        WriteU2(writer, (ushort)thisClass);
        WriteU2(writer, (ushort)superClass);
        WriteU2(writer, 0); // interfaces
        WriteU2(writer, 0); // fields

        var methodList = methods ?? [];
        WriteU2(writer, (ushort)methodList.Count);
        foreach (var method in methodList)
        {
            writer.Write(method);
        }

        var list = attributes ?? [];
        WriteU2(writer, (ushort)list.Count);
        foreach (var attribute in list)
        {
            writer.Write(attribute);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Utf8Entry(string value)
    {
        var bytes = ConstantPool.EncodeModifiedUtf8(value);
        var entry = new byte[3 + bytes.Length];
        entry[0] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(1), (ushort)bytes.Length);
        bytes.CopyTo(entry, 3);
        return entry;
    }

    private static byte[] SingleEntry(byte tag, int value)
    {
        var entry = new byte[3];
        entry[0] = tag;
        BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(1), (ushort)value);
        return entry;
    }

    private static void WriteU2(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU2(Span<byte> target, int value)
        => BinaryPrimitives.WriteUInt16BigEndian(target, (ushort)value);
}
