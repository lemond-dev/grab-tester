using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GrabTester;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void SendFn(byte ch, IntPtr data, nuint len, IntPtr ctx);

public sealed class GrabModule : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr CreateModuleFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int HandleFrameFn(IntPtr ctx, IntPtr payload, nuint len, SendFn fn, IntPtr fnCtx);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void ModuleShutdownFn(IntPtr ctx);

    IntPtr _lib;
    IntPtr _ctx;
    readonly CreateModuleFn _createModule;
    readonly HandleFrameFn _handleFrame;
    readonly ModuleShutdownFn _moduleShutdown;
    GCHandle _cbHandle;

    public GrabModule(string path)
    {
        _lib = NativeLibrary.Load(path);
        _createModule = GetExport<CreateModuleFn>("create_module");
        _handleFrame  = GetExport<HandleFrameFn>("handle_frame");
        _moduleShutdown = GetExport<ModuleShutdownFn>("module_shutdown");
        _ctx = _createModule();
        if (_ctx == IntPtr.Zero)
            throw new InvalidOperationException("create_module returned null");
    }

    T GetExport<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    public void Start(string userUid, string? appType, string? user, SendFn callback)
    {
        var dict = new Dictionary<string, string?> { ["user_uid"] = userUid };
        if (!string.IsNullOrWhiteSpace(appType)) dict["app_type"] = appType;
        if (!string.IsNullOrWhiteSpace(user))    dict["user"]     = user;
        var json = JsonSerializer.Serialize(dict);

        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var frame = new byte[1 + jsonBytes.Length];
        frame[0] = 0x01;
        jsonBytes.CopyTo(frame, 1);

        // Keep callback alive for the lifetime of the native call
        if (_cbHandle.IsAllocated) _cbHandle.Free();
        _cbHandle = GCHandle.Alloc(callback);

        SendFrame(frame, callback);
    }

    public void Cancel()
    {
        SendFrame(new byte[] { 0x02 }, NullSend);
    }

    unsafe void SendFrame(byte[] frame, SendFn cb)
    {
        fixed (byte* p = frame)
            _handleFrame(_ctx, (IntPtr)p, (nuint)frame.Length, cb, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_ctx != IntPtr.Zero)
        {
            _moduleShutdown(_ctx);
            _ctx = IntPtr.Zero;
        }
        if (_cbHandle.IsAllocated) _cbHandle.Free();
        if (_lib != IntPtr.Zero)
        {
            NativeLibrary.Free(_lib);
            _lib = IntPtr.Zero;
        }
    }

    static void NullSend(byte ch, IntPtr data, nuint len, IntPtr ctx) { }
}
