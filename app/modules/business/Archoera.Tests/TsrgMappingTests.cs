using Archoera.Infrastructure.Loaders.Remap;

namespace Archoera.Tests;

public sealed class TsrgMappingTests
{
    [Fact]
    public void V1_maps_classes_fields_and_methods()
    {
        var mapping = TsrgMapping.ParseText(
            """
            a net/minecraft/world/Item
            	b f_1_
            	c ()V m_1_
            """);

        Assert.True(mapping.TryMapClass("a", out var className));
        Assert.Equal("net/minecraft/world/Item", className);

        Assert.True(mapping.TryMapField("a", "b", "I", out var field));
        Assert.Equal("f_1_", field);

        Assert.True(mapping.TryMapMethod("a", "c", "()V", out var method));
        Assert.Equal("m_1_", method);    }

    [Fact]
    public void V1_field_without_descriptor_matches_any_descriptor()
    {
        var mapping = TsrgMapping.ParseText(
            """
            a net/minecraft/world/Item
            	b f_1_
            """);

        Assert.True(mapping.TryMapField("a", "b", "Ljava/lang/String;", out var field));
        Assert.Equal("f_1_", field);
    }

    [Fact]
    public void V2_skips_parameter_metadata()
    {
        var mapping = TsrgMapping.ParseText(
            """
            tsrg2 obf srg id
            a net/minecraft/world/Item 12
            	b f_1_ 13
            	c (I)V m_1_ 14
            		static
            		0 o p_1_ 15
            """);

        Assert.Equal("obf", mapping.SourceNamespace);
        Assert.Equal("srg", mapping.TargetNamespace);
        Assert.True(mapping.TryMapClass("a", out var className));
        Assert.Equal("net/minecraft/world/Item", className);
        Assert.True(mapping.TryMapField("a", "b", "I", out var field));
        Assert.Equal("f_1_", field);
        Assert.True(mapping.TryMapMethod("a", "c", "(I)V", out var method));
        Assert.Equal("m_1_", method);
    }

    [Fact]
    public void V2_maps_a_method_with_a_thousand_entries_before_and_after()
    {
        var mapping = TsrgMapping.ParseText(
            """
            tsrg2 left right
            eie com/mojang/blaze3d/vertex/BufferBuilder
            	a (DDD)Lein; m_5483_
            """);

        Assert.True(mapping.TryMapMethod("eie", "a", "(DDD)Lein;", out var mapped));
        Assert.Equal("m_5483_", mapped);
    }

    [Fact]
    public void Parameter_names_follow_the_mapping_slots()
    {
        var mapping = TsrgMapping.ParseText(
            """
            tsrg2 obf srg id
            a net/minecraft/Sample 1
            	<init> (IJLjava/lang/String;)V <init> 2
            		0 o p_1_ 3
            		1 o p_2_ 4
            		2 o p_3_ 5
            """);

        Assert.True(mapping.TryMapParameter("a", "<init>", "(IJLjava/lang/String;)V", false, 0, out var self));
        Assert.Equal("this", self);
        Assert.True(mapping.TryMapParameter("a", "<init>", "(IJLjava/lang/String;)V", false, 1, out var first));
        Assert.Equal("p_1_", first);
        Assert.True(mapping.TryMapParameter("a", "<init>", "(IJLjava/lang/String;)V", false, 2, out var wide));
        Assert.Equal("p_2_", wide);
        Assert.True(mapping.TryMapParameter("a", "<init>", "(IJLjava/lang/String;)V", false, 3, out var wideAgain));
        Assert.Equal("p_2_", wideAgain);
        Assert.True(mapping.TryMapParameter("a", "<init>", "(IJLjava/lang/String;)V", false, 4, out var third));
        Assert.Equal("p_3_", third);
    }

    [Fact]
    public void Unknown_member_returns_false()
    {
        var mapping = TsrgMapping.ParseText(
            """
            a net/minecraft/world/Item
            """);

        Assert.False(mapping.TryMapField("a", "missing", "I", out _));
        Assert.False(mapping.TryMapMethod("a", "missing", "()V", out _));
        Assert.False(mapping.TryMapClass("missing", out _));
    }

    [Fact]
    public void Member_before_class_is_rejected()
    {
        var error = Assert.Throws<ClassFileException>(
            () => TsrgMapping.ParseText("\tb f_1_"));

        Assert.Contains("before a class", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
