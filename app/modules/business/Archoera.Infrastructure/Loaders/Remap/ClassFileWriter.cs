using System.Text;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Writes a parsed class back without remapping. This is the round-trip oracle
/// for the reader and constant pool encoder: any byte difference here means the
/// parser lost information.
/// </summary>
internal static class ClassFileWriter
{
    public static byte[] WriteUnchanged(ClassFileModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        writer.Write((byte)0xCA);
        writer.Write((byte)0xFE);
        writer.Write((byte)0xBA);
        writer.Write((byte)0xBE);
        WriteU2(writer, (ushort)model.MinorVersion);
        WriteU2(writer, (ushort)model.MajorVersion);

        var pool = new ConstantPoolBuilder(model.Pool, copiesSource: true);
        pool.WritePool(writer);

        WriteU2(writer, (ushort)model.Access);
        WriteU2(writer, (ushort)model.ThisClass);
        WriteU2(writer, (ushort)model.SuperClass);
        WriteU2(writer, (ushort)model.Interfaces.Length);
        foreach (var interfaceIndex in model.Interfaces)
        {
            WriteU2(writer, (ushort)interfaceIndex);
        }

        WriteMembers(writer, model.Fields);
        WriteMembers(writer, model.Methods);
        WriteAttributes(writer, model.Attributes);

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteMembers(BinaryWriter writer, List<ClassMemberModel> members)
    {
        WriteU2(writer, (ushort)members.Count);
        foreach (var member in members)
        {
            WriteU2(writer, (ushort)member.Access);
            WriteU2(writer, (ushort)member.NameIndex);
            WriteU2(writer, (ushort)member.DescriptorIndex);
            WriteAttributes(writer, member.Attributes);
        }
    }

    private static void WriteAttributes(BinaryWriter writer, List<ClassAttributeModel> attributes)
    {
        WriteU2(writer, (ushort)attributes.Count);
        foreach (var attribute in attributes)
        {
            WriteU2(writer, (ushort)attribute.NameIndex);
            WriteU4(writer, checked((uint)attribute.Info.Length));
            writer.Write(attribute.Info);
        }
    }

    private static void WriteU2(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU4(BinaryWriter writer, uint value)
    {
        writer.Write(unchecked((byte)(value >> 24)));
        writer.Write(unchecked((byte)(value >> 16)));
        writer.Write(unchecked((byte)(value >> 8)));
        writer.Write(unchecked((byte)value));
    }
}
