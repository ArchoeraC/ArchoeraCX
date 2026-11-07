using System.Buffers.Binary;

namespace Archoera.Infrastructure.Loaders.Remap;

/// <summary>
/// Re-encodes a method body the way ASM's <c>MethodWriter</c> does. The reader
/// normalizes every instruction to its base opcode and the writer re-emits the
/// narrowest legal form, so a mapped constant index crossing 255 changes the
/// code length and therefore every branch, handler and debug offset. The
/// original offsets are recorded so the caller can patch the attributes that
/// refer to code positions.
/// </summary>
internal static class CodeRelocator
{
    private const byte Wide = 0xC4;
    private const byte Iinc = 0x84;
    private const byte Ret = 0xA9;
    private const byte Goto = 0xA7;
    private const byte Jsr = 0xA8;
    private const byte GotoW = 0xC8;
    private const byte JsrW = 0xC9;

    public sealed class Result
    {
        private readonly int[] _oldToNew;

        internal Result(byte[] code, int[] oldToNew, int oldLength)
        {
            Code = code;
            _oldToNew = oldToNew;
            OldLength = oldLength;
        }

        public byte[] Code { get; }

        public int OldLength { get; }

        public int MapOffset(int oldOffset)
        {
            if (oldOffset == OldLength)
            {
                return Code.Length;
            }

            if (oldOffset < 0 || oldOffset >= _oldToNew.Length)
            {
                throw new ClassFileException($"code offset {oldOffset} is outside the method");
            }

            var mapped = _oldToNew[oldOffset];
            if (mapped < 0)
            {
                throw new ClassFileException($"code offset {oldOffset} is not an instruction boundary");
            }

            return mapped;
        }

        /// <summary>Maps a half-open range <c>[start, end)</c> used by debug and handler tables.</summary>
        public (int Start, int Length) MapRange(int oldStart, int oldEnd)
        {
            var start = MapOffset(oldStart);
            var end = MapOffset(oldEnd);
            return (start, end - start);
        }
    }

    public static Result Relocate(
        byte[] code,
        ConstantPool source,
        RemapContext context,
        string className,
        string methodName)
    {
        var instructions = Decode(code, source, context, className, methodName);
        var (newOffsets, total) = Layout(instructions, code.Length, className, methodName);

        var output = new byte[total];
        foreach (var instruction in instructions)
        {
            Emit(output, instruction, newOffsets, code.Length, total);
        }

        return new Result(output, newOffsets, code.Length);
    }

    public static int InstructionLength(byte[] code, int offset)
    {
        var opcode = code[offset];
        if (opcode == 0xAA || opcode == 0xAB)
        {
            var padding = (4 - ((offset + 1) % 4)) % 4;
            var baseOffset = offset + 1 + padding;
            if (opcode == 0xAA)
            {
                var low = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 4));
                var high = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 8));
                return 1 + padding + 12 + (high - low + 1) * 4;
            }

            var pairs = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 4));
            return 1 + padding + 8 + pairs * 8;
        }

        if (opcode == Wide)
        {
            return code[offset + 1] == Iinc ? 6 : 4;
        }

        return opcode switch
        {
            0x10 or 0x12 or 0x15 or 0x16 or 0x17 or 0x18 or 0x19 or
            0x36 or 0x37 or 0x38 or 0x39 or 0x3A or 0xA9 or 0xBC => 2,

            0x11 or 0x13 or 0x14 or Iinc or
            0x99 or 0x9A or 0x9B or 0x9C or 0x9D or 0x9E or 0x9F or 0xA0 or
            0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0xA6 or 0xA7 or 0xA8 or
            0xB2 or 0xB3 or 0xB4 or 0xB5 or 0xB6 or 0xB7 or 0xB8 or
            0xBB or 0xBD or 0xC0 or 0xC1 or 0xC6 or 0xC7 => 3,

            0xB9 or 0xBA => 5,
            0xC5 => 4,
            0xC8 or 0xC9 => 5,
            _ => 1,
        };
    }

    private static List<Instruction> Decode(
        byte[] code,
        ConstantPool source,
        RemapContext context,
        string className,
        string methodName)
    {
        var result = new List<Instruction>();
        var offset = 0;
        while (offset < code.Length)
        {
            var opcode = code[offset];
            var instruction = new Instruction
            {
                OldOffset = offset,
                Opcode = opcode,
            };

            switch (opcode)
            {
                case 0x12:
                {
                    var oldIndex = code[offset + 1];
                    instruction.Kind = InstructionKind.Loadable;
                    instruction.ConstantIndex = MapLoadable(oldIndex, source, context);
                    instruction.WideConstant = IsWideConstant(oldIndex, source);
                    break;
                }

                case 0x13:
                case 0x14:
                {
                    var oldIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1));
                    instruction.Kind = InstructionKind.Loadable;
                    instruction.ConstantIndex = MapLoadable(oldIndex, source, context);
                    instruction.WideConstant = opcode == 0x14 || IsWideConstant(oldIndex, source);
                    break;
                }

                case >= 0xB2 and <= 0xB8:
                {
                    var oldIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1));
                    instruction.Kind = InstructionKind.MemberRef;
                    instruction.ConstantIndex = context.MapMemberReference(oldIndex);
                    break;
                }

                case 0xB9:
                {
                    var oldIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1));
                    instruction.Kind = InstructionKind.InvokeInterface;
                    instruction.ConstantIndex = context.MapMemberReference(oldIndex);
                    instruction.Extra = code[offset + 3];
                    instruction.Padding = code[offset + 4];
                    break;
                }

                case 0xBA:
                {
                    var oldIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1));
                    instruction.Kind = InstructionKind.InvokeDynamic;
                    instruction.ConstantIndex = context.MapInvokeDynamic(oldIndex);
                    instruction.Padding = code[offset + 3];
                    instruction.Extra = code[offset + 4];
                    break;
                }

                case 0xBB:
                case 0xBD:
                case 0xC0:
                case 0xC1:
                {
                    var oldIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1));
                    instruction.Kind = InstructionKind.ClassRef;
                    instruction.ConstantIndex = context.MapClassIndex(oldIndex);
                    break;
                }

                case 0xC5:
                {
                    var oldIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 1));
                    instruction.Kind = InstructionKind.MultiArrayNew;
                    instruction.ConstantIndex = context.MapClassIndex(oldIndex);
                    instruction.Extra = code[offset + 3];
                    break;
                }

                case >= 0x99 and <= 0xA8:
                case 0xC6:
                case 0xC7:
                {
                    instruction.Kind = InstructionKind.Branch2;
                    instruction.BranchTarget = offset + BinaryPrimitives.ReadInt16BigEndian(
                        code.AsSpan(offset + 1));
                    break;
                }

                case GotoW:
                case JsrW:
                {
                    instruction.Kind = InstructionKind.Branch4;
                    instruction.BranchTarget = offset + BinaryPrimitives.ReadInt32BigEndian(
                        code.AsSpan(offset + 1));
                    break;
                }

                case 0xAA:
                {
                    DecodeTableSwitch(code, offset, instruction);
                    break;
                }

                case 0xAB:
                {
                    DecodeLookupSwitch(code, offset, instruction);
                    break;
                }

                case Wide:
                {
                    DecodeWide(code, offset, instruction);
                    break;
                }

                case >= 0x1A and <= 0x2D:
                {
                    // xLOAD_n: ASM reports the generic opcode with an explicit index.
                    instruction.Kind = InstructionKind.VarInsn;
                    instruction.Opcode = (byte)(0x15 + (opcode - 0x1A) / 4);
                    instruction.VarIndex = (opcode - 0x1A) % 4;
                    break;
                }

                case >= 0x3B and <= 0x4E:
                {
                    // xSTORE_n.
                    instruction.Kind = InstructionKind.VarInsn;
                    instruction.Opcode = (byte)(0x36 + (opcode - 0x3B) / 4);
                    instruction.VarIndex = (opcode - 0x3B) % 4;
                    break;
                }

                case >= 0x15 and <= 0x19:
                case >= 0x36 and <= 0x3A:
                case Ret:
                {
                    instruction.Kind = InstructionKind.VarInsn;
                    instruction.VarIndex = code[offset + 1];
                    break;
                }

                case Iinc:
                {
                    instruction.Kind = InstructionKind.Iinc;
                    instruction.VarIndex = code[offset + 1];
                    instruction.Increment = (sbyte)code[offset + 2];
                    break;
                }

                default:
                {
                    var length = InstructionLength(code, offset);
                    instruction.Kind = InstructionKind.Copy;
                    instruction.Copy = code.AsSpan(offset, length).ToArray();
                    break;
                }
            }

            result.Add(instruction);
            offset += InstructionLength(code, offset);
        }

        return result;
    }

    private static void DecodeWide(byte[] code, int offset, Instruction instruction)
    {
        var opcode = code[offset + 1];
        var varIndex = BinaryPrimitives.ReadUInt16BigEndian(code.AsSpan(offset + 2));
        if (opcode == Iinc)
        {
            instruction.Kind = InstructionKind.Iinc;
            instruction.Opcode = Iinc;
            instruction.VarIndex = varIndex;
            instruction.Increment = BinaryPrimitives.ReadInt16BigEndian(code.AsSpan(offset + 4));
            return;
        }

        instruction.Kind = InstructionKind.VarInsn;
        instruction.Opcode = opcode;
        instruction.VarIndex = varIndex;
    }

    private static void DecodeTableSwitch(byte[] code, int offset, Instruction instruction)
    {
        var padding = (4 - ((offset + 1) % 4)) % 4;
        var baseOffset = offset + 1 + padding;
        var low = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 4));
        var high = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 8));
        var count = high - low + 1;
        var targets = new int[count + 1];
        targets[0] = offset + BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset));
        for (var index = 0; index < count; index++)
        {
            targets[index + 1] = offset + BinaryPrimitives.ReadInt32BigEndian(
                code.AsSpan(baseOffset + 12 + index * 4));
        }

        instruction.Kind = InstructionKind.TableSwitch;
        instruction.SwitchTargets = targets;
        instruction.SwitchLow = low;
        instruction.SwitchHigh = high;
    }

    private static void DecodeLookupSwitch(byte[] code, int offset, Instruction instruction)
    {
        var padding = (4 - ((offset + 1) % 4)) % 4;
        var baseOffset = offset + 1 + padding;
        var pairs = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 4));
        var targets = new int[pairs + 1];
        var keys = new int[pairs];
        targets[0] = offset + BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset));
        for (var index = 0; index < pairs; index++)
        {
            keys[index] = BinaryPrimitives.ReadInt32BigEndian(code.AsSpan(baseOffset + 8 + index * 8));
            targets[index + 1] = offset + BinaryPrimitives.ReadInt32BigEndian(
                code.AsSpan(baseOffset + 12 + index * 8));
        }

        instruction.Kind = InstructionKind.LookupSwitch;
        instruction.SwitchTargets = targets;
        instruction.SwitchKeys = keys;
    }

    /// <summary>
    /// Assigns new offsets and sizes. Switch padding depends on the new offset,
    /// and a narrow jump that no longer reaches its target is widened to
    /// <c>goto_w</c>/<c>jsr_w</c>, so the layout iterates until it is stable.
    /// </summary>
    private static (int[] Offsets, int Length) Layout(
        List<Instruction> instructions,
        int oldLength,
        string className,
        string methodName)
    {
        const int maxIterations = 16;
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var offsets = new int[oldLength];
            Array.Fill(offsets, -1);
            var offset = 0;
            foreach (var instruction in instructions)
            {
                offsets[instruction.OldOffset] = offset;
                instruction.NewOffset = offset;
                instruction.NewSize = SizeOf(instruction);
                offset += instruction.NewSize;
            }

            var widened = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Kind != InstructionKind.Branch2)
                {
                    continue;
                }

                var distance = Map(instruction.BranchTarget!.Value, offsets, oldLength, offset) -
                    instruction.NewOffset;
                if (distance is >= short.MinValue and <= short.MaxValue)
                {
                    continue;
                }

                if (instruction.Opcode is not (Goto or Jsr))
                {
                    throw new NotSupportedException(
                        $"{className}.{methodName}: conditional branch at bytecode offset " +
                        $"{instruction.OldOffset} no longer fits in 16 bits");
                }

                instruction.Kind = InstructionKind.Branch4;
                instruction.Opcode = instruction.Opcode == Goto ? GotoW : JsrW;
                widened = true;
            }

            if (!widened)
            {
                return (offsets, offset);
            }
        }

        throw new ClassFileException(
            $"{className}.{methodName}: bytecode layout did not converge");
    }

    private static int SizeOf(Instruction instruction) => instruction.Kind switch
    {
        InstructionKind.Copy => instruction.Copy!.Length,
        InstructionKind.Loadable => instruction.WideConstant || instruction.ConstantIndex >= 256 ? 3 : 2,
        InstructionKind.MemberRef or InstructionKind.ClassRef => 3,
        InstructionKind.InvokeInterface or InstructionKind.InvokeDynamic => 5,
        InstructionKind.MultiArrayNew => 4,
        InstructionKind.Branch2 => 3,
        InstructionKind.Branch4 => 5,
        InstructionKind.TableSwitch => 1 + SwitchPadding(instruction.NewOffset) + 12 +
            (instruction.SwitchHigh - instruction.SwitchLow + 1) * 4,
        InstructionKind.LookupSwitch => 1 + SwitchPadding(instruction.NewOffset) + 8 +
            instruction.SwitchKeys!.Length * 8,
        InstructionKind.VarInsn => VarSize(instruction.Opcode, instruction.VarIndex),
        InstructionKind.Iinc => IincSize(instruction.VarIndex, instruction.Increment),
        _ => throw new ClassFileException($"unknown instruction kind {instruction.Kind}"),
    };

    private static int SwitchPadding(int newOffset) => (4 - ((newOffset + 1) % 4)) % 4;

    private static int VarSize(byte opcode, int varIndex)
    {
        if (varIndex < 4 && opcode != Ret)
        {
            return 1;
        }

        return varIndex >= 256 ? 4 : 2;
    }

    private static int IincSize(int varIndex, int increment)
        => varIndex > 255 || increment > 127 || increment < -128 ? 6 : 3;

    private static void Emit(
        byte[] output,
        Instruction instruction,
        int[] newOffsets,
        int oldLength,
        int newLength)
    {
        var at = instruction.NewOffset;
        switch (instruction.Kind)
        {
            case InstructionKind.Copy:
                instruction.Copy!.CopyTo(output, at);
                return;

            case InstructionKind.Loadable:
                if (instruction.WideConstant)
                {
                    output[at] = 0x14;
                    BinaryPrimitives.WriteUInt16BigEndian(
                        output.AsSpan(at + 1),
                        (ushort)instruction.ConstantIndex);
                }
                else if (instruction.ConstantIndex >= 256)
                {
                    output[at] = 0x13;
                    BinaryPrimitives.WriteUInt16BigEndian(
                        output.AsSpan(at + 1),
                        (ushort)instruction.ConstantIndex);
                }
                else
                {
                    output[at] = 0x12;
                    output[at + 1] = (byte)instruction.ConstantIndex;
                }

                return;

            case InstructionKind.MemberRef:
            case InstructionKind.ClassRef:
                output[at] = instruction.Opcode;
                BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan(at + 1),
                    (ushort)instruction.ConstantIndex);
                return;

            case InstructionKind.InvokeInterface:
                output[at] = instruction.Opcode;
                BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan(at + 1),
                    (ushort)instruction.ConstantIndex);
                output[at + 3] = instruction.Extra;
                output[at + 4] = instruction.Padding;
                return;

            case InstructionKind.InvokeDynamic:
                output[at] = instruction.Opcode;
                BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan(at + 1),
                    (ushort)instruction.ConstantIndex);
                output[at + 3] = instruction.Padding;
                output[at + 4] = instruction.Extra;
                return;

            case InstructionKind.MultiArrayNew:
                output[at] = instruction.Opcode;
                BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan(at + 1),
                    (ushort)instruction.ConstantIndex);
                output[at + 3] = instruction.Extra;
                return;

            case InstructionKind.Branch2:
            {
                var target = Map(instruction.BranchTarget!.Value, newOffsets, oldLength, newLength);
                output[at] = instruction.Opcode;
                BinaryPrimitives.WriteInt16BigEndian(
                    output.AsSpan(at + 1),
                    (short)(target - at));
                return;
            }

            case InstructionKind.Branch4:
            {
                var target = Map(instruction.BranchTarget!.Value, newOffsets, oldLength, newLength);
                output[at] = instruction.Opcode;
                BinaryPrimitives.WriteInt32BigEndian(
                    output.AsSpan(at + 1),
                    target - at);
                return;
            }

            case InstructionKind.TableSwitch:
            {
                output[at] = instruction.Opcode;
                var baseOffset = at + 1 + SwitchPadding(at);
                BinaryPrimitives.WriteInt32BigEndian(
                    output.AsSpan(baseOffset),
                    Map(instruction.SwitchTargets![0], newOffsets, oldLength, newLength) - at);
                BinaryPrimitives.WriteInt32BigEndian(
                    output.AsSpan(baseOffset + 4),
                    instruction.SwitchLow);
                BinaryPrimitives.WriteInt32BigEndian(
                    output.AsSpan(baseOffset + 8),
                    instruction.SwitchHigh);
                for (var index = 1; index < instruction.SwitchTargets.Length; index++)
                {
                    BinaryPrimitives.WriteInt32BigEndian(
                        output.AsSpan(baseOffset + 8 + index * 4),
                        Map(instruction.SwitchTargets[index], newOffsets, oldLength, newLength) - at);
                }

                return;
            }

            case InstructionKind.LookupSwitch:
            {
                output[at] = instruction.Opcode;
                var baseOffset = at + 1 + SwitchPadding(at);
                BinaryPrimitives.WriteInt32BigEndian(
                    output.AsSpan(baseOffset),
                    Map(instruction.SwitchTargets![0], newOffsets, oldLength, newLength) - at);
                var pairs = instruction.SwitchKeys!.Length;
                BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(baseOffset + 4), pairs);
                for (var index = 0; index < pairs; index++)
                {
                    BinaryPrimitives.WriteInt32BigEndian(
                        output.AsSpan(baseOffset + 8 + index * 8),
                        instruction.SwitchKeys[index]);
                    BinaryPrimitives.WriteInt32BigEndian(
                        output.AsSpan(baseOffset + 12 + index * 8),
                        Map(instruction.SwitchTargets[index + 1], newOffsets, oldLength, newLength) - at);
                }

                return;
            }

            case InstructionKind.VarInsn:
                EmitVarInsn(output, at, instruction.Opcode, instruction.VarIndex);
                return;

            case InstructionKind.Iinc:
            {
                output[at] = Iinc;
                if (instruction.NewSize == 6)
                {
                    output[at] = Wide;
                    output[at + 1] = Iinc;
                    BinaryPrimitives.WriteUInt16BigEndian(
                        output.AsSpan(at + 2),
                        (ushort)instruction.VarIndex);
                    BinaryPrimitives.WriteInt16BigEndian(
                        output.AsSpan(at + 4),
                        (short)instruction.Increment);
                }
                else
                {
                    output[at + 1] = (byte)instruction.VarIndex;
                    output[at + 2] = (byte)(sbyte)instruction.Increment;
                }

                return;
            }

            default:
                throw new ClassFileException($"cannot emit instruction kind {instruction.Kind}");
        }
    }

    private static void EmitVarInsn(byte[] output, int at, byte opcode, int varIndex)
    {
        if (varIndex < 4 && opcode != Ret)
        {
            output[at] = opcode < 0x36
                ? (byte)(0x1A + (opcode - 0x15) * 4 + varIndex)
                : (byte)(0x3B + (opcode - 0x36) * 4 + varIndex);
            return;
        }

        if (varIndex >= 256)
        {
            output[at] = Wide;
            output[at + 1] = opcode;
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + 2), (ushort)varIndex);
            return;
        }

        output[at] = opcode;
        output[at + 1] = (byte)varIndex;
    }

    private static int Map(int oldOffset, int[] newOffsets, int oldLength, int newLength)
    {
        if (oldOffset == oldLength)
        {
            return newLength;
        }

        if (oldOffset < 0 || oldOffset >= newOffsets.Length || newOffsets[oldOffset] < 0)
        {
            throw new ClassFileException($"branch target {oldOffset} is not an instruction boundary");
        }

        return newOffsets[oldOffset];
    }

    private static bool IsWideConstant(int oldIndex, ConstantPool source)
        => source[oldIndex].Tag is ConstantPoolTag.Long or ConstantPoolTag.Double;

    private static int MapLoadable(int oldIndex, ConstantPool source, RemapContext context)
    {
        var entry = source[oldIndex];
        return entry.Tag switch
        {
            ConstantPoolTag.Integer => context.Pool.Integer(entry.First),
            ConstantPoolTag.Float => context.Pool.Float(BitConverter.Int32BitsToSingle(entry.First)),
            ConstantPoolTag.Long => context.Pool.Long(entry.Wide),
            ConstantPoolTag.Double => context.Pool.Double(BitConverter.Int64BitsToDouble(entry.Wide)),
            ConstantPoolTag.String => context.Pool.StringConstant(source.GetUtf8(entry.First)),
            ConstantPoolTag.Class => context.MapClassIndex(oldIndex),
            ConstantPoolTag.MethodType => context.Pool.MethodType(
                context.Descriptors.MapDescriptor(source.GetUtf8(entry.First))),
            ConstantPoolTag.MethodHandle => context.MapBootstrapValue(oldIndex),
            _ => throw new NotSupportedException($"ldc constant tag {entry.Tag} is not supported yet"),
        };
    }

    private enum InstructionKind
    {
        Copy,
        Loadable,
        MemberRef,
        InvokeInterface,
        InvokeDynamic,
        ClassRef,
        MultiArrayNew,
        Branch2,
        Branch4,
        TableSwitch,
        LookupSwitch,
        VarInsn,
        Iinc,
    }

    private sealed class Instruction
    {
        public int OldOffset;
        public int NewOffset;
        public int NewSize;
        public byte Opcode;
        public InstructionKind Kind;
        public byte[]? Copy;
        public int ConstantIndex;
        public bool WideConstant;
        public byte Extra;
        public byte Padding;
        public int VarIndex;
        public int Increment;
        public int? BranchTarget;
        public int[]? SwitchTargets;
        public int[]? SwitchKeys;
        public int SwitchLow;
        public int SwitchHigh;
    }
}
