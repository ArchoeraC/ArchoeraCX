using System.Buffers.Binary;
using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

/// <summary>
/// Unit tests for the remap primitives that do not need the locally generated
/// ART oracle jars: signature grammar, local variable table merging and the
/// bytecode re-encoding rules ASM applies.
/// </summary>
public sealed class RemapPrimitiveTests
{
    [Fact]
    public void Type_parameter_bound_with_type_variable_is_remapped()
    {
        var mapper = new DescriptorRemapper(
            name => name == "ejc" ? "net/minecraft/Sample" : name);

        var mapped = mapper.MapSignature(
            "<T:Lejc<TT;>;>Ljava/lang/Object;",
            typeSignature: false);

        Assert.Equal("<T:Lnet/minecraft/Sample<TT;>;>Ljava/lang/Object;", mapped);
    }

    [Fact]
    public void Empty_class_bound_is_preserved()
    {
        var mapper = new DescriptorRemapper(name => name);

        var mapped = mapper.MapSignature(
            "<T::Ljava/io/Serializable;>Ljava/lang/Object;",
            typeSignature: false);

        Assert.Equal("<T::Ljava/io/Serializable;>Ljava/lang/Object;", mapped);
    }

    [Fact]
    public void Inner_class_suffix_uses_the_mapped_simple_name()
    {
        var mapper = new DescriptorRemapper(name => name switch
        {
            "pkg/Outer" => "com/example/Renamed",
            "pkg/Outer$Inner" => "com/example/Renamed$Nested",
            _ => name,
        });

        var mapped = mapper.MapSignature("Lpkg/Outer<TT;>.Inner;", typeSignature: true);

        Assert.Equal("Lcom/example/Renamed<TT;>.Nested;", mapped);
    }

    [Fact]
    public void Method_signature_remaps_parameters_return_and_throws()
    {
        var mapper = new DescriptorRemapper(
            name => name == "a" ? "com/example/A" : name);

        var mapped = mapper.MapSignature(
            "<X:La;>(La;[TX;)La;^La;^TX;",
            typeSignature: false);

        Assert.Equal(
            "<X:Lcom/example/A;>(Lcom/example/A;[TX;)Lcom/example/A;^Lcom/example/A;^TX;",
            mapped);
    }

    [Fact]
    public void Local_variable_type_table_is_merged_into_the_local_table()
    {
        var localTable = LocalTable(
            (start: 0, length: 10, name: 5, descriptor: 6, slot: 0),
            (start: 4, length: 6, name: 7, descriptor: 8, slot: 2));
        var typeTable = TypeTable(
            (start: 4, length: 6, name: 7, signature: 9, slot: 2),
            (start: 8, length: 2, name: 7, signature: 10, slot: 4));

        var entries = LocalVariableTables.Read(localTable, typeTable);

        // The type-table-only entry is dropped, like ClassReader does.
        Assert.Equal(2, entries.Count);
        Assert.Equal(-1, entries[0].SignatureIndex);
        Assert.Equal(9, entries[1].SignatureIndex);
        Assert.Equal(4, entries[1].StartPc);
        Assert.Equal(2, entries[1].Slot);
    }

    [Fact]
    public void Local_variable_type_table_matches_ignoring_length()
    {
        var localTable = LocalTable((start: 4, length: 6, name: 7, descriptor: 8, slot: 2));
        var typeTable = TypeTable((start: 4, length: 99, name: 7, signature: 9, slot: 2));

        var entry = Assert.Single(LocalVariableTables.Read(localTable, typeTable));

        Assert.Equal(9, entry.SignatureIndex);
    }

    [Theory]
    [InlineData(new byte[] { 0xB2, 0x00, 0x01 }, 3)]
    [InlineData(new byte[] { 0xB6, 0x00, 0x01 }, 3)]
    [InlineData(new byte[] { 0xB9, 0x00, 0x01, 0x00, 0x00 }, 5)]
    [InlineData(new byte[] { 0x12, 0x01 }, 2)]
    [InlineData(new byte[] { 0x13, 0x00, 0x01 }, 3)]
    [InlineData(new byte[] { 0xC5, 0x00, 0x01, 0x02 }, 4)]
    [InlineData(new byte[] { 0xC4, 0x15, 0x00, 0x01 }, 4)]
    [InlineData(new byte[] { 0xC4, 0x84, 0x00, 0x01, 0x00, 0x02 }, 6)]
    public void Instruction_lengths_match_the_jvm_encoding(byte[] code, int expected)
    {
        Assert.Equal(expected, CodeRelocator.InstructionLength(code, 0));
    }

    [Fact]
    public void Ldc_is_widened_when_the_mapped_class_index_crosses_255()
    {
        var model = ClassFileReader.Read(
            NativeArtFixtureBuilder.ClassWithClassConstant("orig/Name", paddingEntries: 249));
        var mapping = TsrgMapping.ParseText("orig/Name new/Name");
        var context = new RemapContext(
            model.Pool,
            mapping,
            new ConstantPoolBuilder(model.Pool, copiesSource: true));
        var classIndex = FindClassIndex(model.Pool, "orig/Name");
        Assert.Equal(255, classIndex);

        var code = new byte[] { 0x12, (byte)classIndex, 0x00, 0xB1 };
        var result = CodeRelocator.Relocate(code, model.Pool, context, "fixture/Sample", "m");

        Assert.Equal(5, result.Code.Length);
        Assert.Equal(0x13, result.Code[0]);
        Assert.True(BinaryPrimitives.ReadUInt16BigEndian(result.Code.AsSpan(1)) > 255);
        Assert.Equal(0x00, result.Code[3]);
        Assert.Equal(0xB1, result.Code[4]);
        Assert.Equal(3, result.MapOffset(2));
        Assert.Equal(4, result.MapOffset(3));
    }

    [Fact]
    public void Ldc_w_is_narrowed_when_the_mapped_class_index_fits()
    {
        var model = ClassFileReader.Read(
            NativeArtFixtureBuilder.ClassWithClassConstant("orig/Name", paddingEntries: 10));
        var mapping = TsrgMapping.ParseText("orig/Name orig/Name");
        var context = new RemapContext(
            model.Pool,
            mapping,
            new ConstantPoolBuilder(model.Pool, copiesSource: true));
        var classIndex = FindClassIndex(model.Pool, "orig/Name");
        Assert.True(classIndex < 256);

        var code = new byte[] { 0x13, (byte)(classIndex >> 8), (byte)classIndex, 0xB1 };
        var result = CodeRelocator.Relocate(code, model.Pool, context, "fixture/Sample", "m");

        Assert.Equal(3, result.Code.Length);
        Assert.Equal(0x12, result.Code[0]);
        Assert.Equal((byte)classIndex, result.Code[1]);
        Assert.Equal(0xB1, result.Code[2]);
        Assert.Equal(2, result.MapOffset(3));
    }

    [Fact]
    public void Wide_load_is_normalized_to_the_shortest_form()
    {
        var model = ClassFileReader.Read(NativeArtFixtureBuilder.EmptyClass());
        var context = new RemapContext(
            model.Pool,
            TsrgMapping.ParseText("fixture/Sample fixture/Sample"),
            new ConstantPoolBuilder(model.Pool, copiesSource: true));

        var code = new byte[] { 0xC4, 0x15, 0x00, 0x01, 0xB1 };
        var result = CodeRelocator.Relocate(code, model.Pool, context, "fixture/Sample", "m");

        Assert.Equal(new byte[] { 0x1B, 0xB1 }, result.Code);
        Assert.Equal(1, result.MapOffset(4));
    }

    [Fact]
    public void Branch_targets_are_remapped_after_a_width_change()
    {
        var model = ClassFileReader.Read(
            NativeArtFixtureBuilder.ClassWithClassConstant("orig/Name", paddingEntries: 249));
        var context = new RemapContext(
            model.Pool,
            TsrgMapping.ParseText("orig/Name new/Name"),
            new ConstantPoolBuilder(model.Pool, copiesSource: true));
        var classIndex = FindClassIndex(model.Pool, "orig/Name");

        // ifnonnull +5 (skip the widened ldc), ldc, return.
        var code = new byte[]
        {
            0xC7, 0x00, 0x05,
            0x12, (byte)classIndex,
            0xB1,
        };
        var result = CodeRelocator.Relocate(code, model.Pool, context, "fixture/Sample", "m");

        Assert.Equal(0xC7, result.Code[0]);
        Assert.Equal(6, BinaryPrimitives.ReadInt16BigEndian(result.Code.AsSpan(1)));
        Assert.Equal(6, result.MapOffset(5));
    }

    [Fact]
    public void Identifier_fixer_renames_invalid_local_variable_names()
    {
        var model = ClassFileReader.Read(
            NativeArtFixtureBuilder.ClassWithLocalVariableName("1bad/name"));
        var remapper = new ClassFileRemapper(
            model,
            TsrgMapping.Empty,
            hierarchy: null,
            ConstantPoolMode.CopyFromInput,
            ArtFixers.Identifiers);

        var bytes = remapper.Rewrite();
        var output = ClassFileReader.Read(bytes);

        Assert.True(remapper.Changed);
        Assert.True(HasUtf8(output.Pool, "lvt_0_1_"));
    }

    [Fact]
    public void Identifier_fixer_keeps_valid_local_variable_names()
    {
        var model = ClassFileReader.Read(
            NativeArtFixtureBuilder.ClassWithLocalVariableName("_value1"));
        var remapper = new ClassFileRemapper(
            model,
            TsrgMapping.Empty,
            hierarchy: null,
            ConstantPoolMode.CopyFromInput,
            ArtFixers.Identifiers);

        var output = ClassFileReader.Read(remapper.Rewrite());

        Assert.False(remapper.Changed);
        Assert.True(HasUtf8(output.Pool, "_value1"));
        Assert.False(HasUtf8(output.Pool, "lvt_0_1_"));
    }

    private static bool HasUtf8(ConstantPool pool, string value)
    {
        for (var index = 1; index < pool.Count; index++)
        {
            if (pool.TryGet(index) is { Tag: ConstantPoolTag.Utf8 } &&
                pool.GetUtf8(index) == value)
            {
                return true;
            }
        }

        return false;
    }

    private static int FindClassIndex(ConstantPool pool, string name)
    {
        for (var index = 1; index < pool.Count; index++)
        {
            var entry = pool.TryGet(index);
            if (entry is { Tag: ConstantPoolTag.Class } && pool.GetClassName(index) == name)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"class {name} is not in the pool");
    }

    private static byte[] LocalTable(
        params (int start, int length, int name, int descriptor, int slot)[] entries)
    {
        var payload = new byte[2 + entries.Length * 10];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)entries.Length);
        var offset = 2;
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), (ushort)entry.start);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 2), (ushort)entry.length);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 4), (ushort)entry.name);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 6), (ushort)entry.descriptor);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 8), (ushort)entry.slot);
            offset += 10;
        }

        return payload;
    }

    private static byte[] TypeTable(
        params (int start, int length, int name, int signature, int slot)[] entries)
    {
        var payload = new byte[2 + entries.Length * 10];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)entries.Length);
        var offset = 2;
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), (ushort)entry.start);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 2), (ushort)entry.length);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 4), (ushort)entry.name);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 6), (ushort)entry.signature);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset + 8), (ushort)entry.slot);
            offset += 10;
        }

        return payload;
    }
}
