using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Archoera.Application.Loaders;
using Archoera.Application.Modules;
using Archoera.Infrastructure.Accounts;
using Archoera.Infrastructure.Modules;

namespace Archoera.Exports;

/// <summary>
/// NativeAOT module ABI v1 adapter. The table layout mirrors
/// <c>app/contracts/module/archoera_module_abi.h</c> exactly.
///
/// This layer only performs ABI marshalling, cancellation bookkeeping and
/// done-callback lifetime management. Method ownership lives in the
/// <see cref="ModuleMethodRegistry"/>; adding a feature means registering a
/// handler, not editing a growing if-chain here.
/// </summary>
public static unsafe class ModuleExports
{
    private const int AbiVersion = 1;
    private const int Ok = 0;
    private const int Invalid = 1;
    private const int Cancelled = 6;
    private const int Unsupported = 7;
    private const int Internal = 8;

    private static readonly ModuleMethodRegistry Registry =
        BusinessModuleRegistry.CreateDefault();

    private static readonly IntPtr ModuleTable = CreateModuleTable();

    [StructLayout(LayoutKind.Sequential)]
    private struct ModuleV1
    {
        public uint AbiVersion;
        public IntPtr Name;
        public IntPtr Version;
        public IntPtr Init;
        public IntPtr Call;
        public IntPtr Cancel;
        public IntPtr Shutdown;
    }

    [UnmanagedCallersOnly(
        EntryPoint = "archoera_module_entry",
        CallConvs = new[] { typeof(CallConvCdecl) })]
    public static IntPtr ArchoeraModuleEntry() => ModuleTable;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ModuleInit(IntPtr configJson, IntPtr* outSelf, IntPtr err)
    {
        _ = configJson;
        _ = err;

        if (outSelf is null)
        {
            return Invalid;
        }

        *outSelf = IntPtr.Zero;

        try
        {
            var handle = GCHandle.Alloc(new ModuleState(), GCHandleType.Normal);
            var self = GCHandle.ToIntPtr(handle);
            lock (HandlesGate)
            {
                LiveHandles.Add(self);
            }

            *outSelf = self;
            return Ok;
        }
        catch
        {
            return Internal;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ModuleCall(
        IntPtr self,
        ulong taskId,
        IntPtr methodPtr,
        IntPtr paramsPtr,
        IntPtr donePtr,
        IntPtr user)
    {
        if (self == IntPtr.Zero || methodPtr == IntPtr.Zero || donePtr == IntPtr.Zero)
        {
            return Invalid;
        }

        ModuleState? state = null;
        var entered = false;
        var doneInvoked = false;

        try
        {
            state = GCHandle.FromIntPtr(self).Target as ModuleState;
            if (state is null)
            {
                return Internal;
            }

            var method = Marshal.PtrToStringUTF8(methodPtr);
            if (!Registry.Supports(method))
            {
                // Not accepted: the host must not expect a done callback.
                return Unsupported;
            }

            string? paramsJson;
            try
            {
                paramsJson = paramsPtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(paramsPtr);
            }
            catch
            {
                return Invalid;
            }

            if (!state.TryBeginCall())
            {
                return Internal;
            }

            entered = true;

            var done = (delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void>)donePtr;
            var cancellationToken = state.Register(taskId);

            try
            {
                var json = Registry
                    .HandleAsync(method!, paramsJson, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                InvokeDone(done, user, Ok, json);
                doneInvoked = true;
                return Ok;
            }
            catch (OperationCanceledException)
            {
                if (!doneInvoked)
                {
                    InvokeDone(done, user, Cancelled, null);
                    doneInvoked = true;
                }

                return Ok;
            }
            catch (Exception exception)
            {
                if (!doneInvoked)
                {
                    InvokeDone(
                        done,
                        user,
                        Ok,
                        LoaderManifestJson.BuildError("E_INTERNAL", exception.Message));
                    doneInvoked = true;
                }

                return Ok;
            }
            finally
            {
                state.Unregister(taskId);
            }
        }
        catch
        {
            return doneInvoked ? Ok : Internal;
        }
        finally
        {
            if (entered)
            {
                state!.EndCall();
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int ModuleCancel(IntPtr self, ulong taskId)
    {
        try
        {
            if (self != IntPtr.Zero &&
                GCHandle.FromIntPtr(self).Target is ModuleState state)
            {
                state.Cancel(taskId);
            }
        }
        catch
        {
            // Cancel is idempotent and unknown tasks are ignored.
        }

        return Ok;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ModuleShutdown(IntPtr self)
    {
        if (self == IntPtr.Zero)
        {
            return;
        }

        GCHandle handle;
        try
        {
            lock (HandlesGate)
            {
                if (!LiveHandles.Remove(self))
                {
                    return;
                }
            }

            handle = GCHandle.FromIntPtr(self);
        }
        catch
        {
            return;
        }

        try
        {
            if (handle.Target is ModuleState state)
            {
                state.Shutdown();
            }
        }
        catch
        {
            // Shutdown must not throw across the ABI.
        }
        finally
        {
            try
            {
                handle.Free();
            }
            catch
            {
                // Nothing useful to do at this point.
            }
        }
    }

    private static void InvokeDone(
        delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, void> done,
        IntPtr user,
        int status,
        byte[]? json)
    {
        if (json is null)
        {
            done(user, status, IntPtr.Zero);
            return;
        }

        var buffer = (byte*)NativeMemory.Alloc((nuint)(json.Length + 1));
        try
        {
            json.AsSpan().CopyTo(new Span<byte>(buffer, json.Length));
            buffer[json.Length] = 0;
            done(user, status, (IntPtr)buffer);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    private static IntPtr CreateModuleTable()
    {
        var table = (ModuleV1*)NativeMemory.AllocZeroed((nuint)sizeof(ModuleV1));

        table->AbiVersion = AbiVersion;
        table->Name = Marshal.StringToCoTaskMemUTF8("archoera_cs");
        table->Version = Marshal.StringToCoTaskMemUTF8("0.1.0");
        table->Init = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr*, IntPtr, int>)&ModuleInit;
        table->Call = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ulong, IntPtr, IntPtr, IntPtr, IntPtr, int>)&ModuleCall;
        table->Cancel = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ulong, int>)&ModuleCancel;
        table->Shutdown = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, void>)&ModuleShutdown;

        return (IntPtr)table;
    }

    private static readonly object HandlesGate = new();
    private static readonly HashSet<IntPtr> LiveHandles = [];

    private sealed class ModuleState
    {
        private readonly object _gate = new();
        private readonly Dictionary<ulong, CancellationTokenSource> _tasks = [];
        private bool _shutdown;
        private int _activeCalls;

        public bool TryBeginCall()
        {
            lock (_gate)
            {
                if (_shutdown)
                {
                    return false;
                }

                _activeCalls++;
                return true;
            }
        }

        public void EndCall()
        {
            lock (_gate)
            {
                if (_activeCalls > 0)
                {
                    _activeCalls--;
                }

                Monitor.PulseAll(_gate);
            }
        }

        public CancellationToken Register(ulong taskId)
        {
            var source = new CancellationTokenSource();
            lock (_gate)
            {
                if (_tasks.Remove(taskId, out var previous))
                {
                    SafeCancel(previous);
                    previous.Dispose();
                }

                _tasks[taskId] = source;
            }

            return source.Token;
        }

        public void Unregister(ulong taskId)
        {
            CancellationTokenSource? source = null;
            lock (_gate)
            {
                if (_tasks.Remove(taskId, out var removed))
                {
                    source = removed;
                }
            }

            source?.Dispose();
        }

        public void Cancel(ulong taskId)
        {
            lock (_gate)
            {
                if (_tasks.TryGetValue(taskId, out var source))
                {
                    SafeCancel(source);
                }
            }
        }

        public void Shutdown()
        {
            List<CancellationTokenSource> pending;
            lock (_gate)
            {
                _shutdown = true;
                pending = [.. _tasks.Values];
            }

            foreach (var source in pending)
            {
                SafeCancel(source);
            }

            lock (_gate)
            {
                while (_activeCalls > 0)
                {
                    Monitor.Wait(_gate);
                }
            }
        }

        private static void SafeCancel(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Raced with Unregister; cancellation is best effort.
            }
            catch (AggregateException)
            {
                // A callback registered on the token failed; ignore it.
            }
        }
    }
}
