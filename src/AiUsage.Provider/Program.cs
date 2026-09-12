using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;

namespace AiUsage.Provider;

static class Program
{
    public const string ClassId = "D43F207B-60E5-40DB-A084-980655913C8C";
    [DllImport("ole32.dll")] static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("ole32.dll")] static extern int CoRegisterClassObject([MarshalAs(UnmanagedType.LPStruct)] Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] static extern int CoRevokeClassObject(uint cookie);
    [MTAThread]
    static void Main()
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
        var factory = new ProviderFactory();
        try {
            Marshal.ThrowExceptionForHR(CoRegisterClassObject(new(ClassId), factory, 4, 1, out var cookie));
            try { WidgetProvider.Exit.WaitOne(); }
            finally { CoRevokeClassObject(cookie); GC.KeepAlive(factory); }
        } catch (Exception e) {
            Directory.CreateDirectory(LocalStore.Folder);
            File.WriteAllText(Path.Combine(LocalStore.Folder, "provider-error.txt"), e.ToString());
        } finally { CoUninitialize(); }
    }
}

[ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IClassFactory
{
    [PreserveSig] int CreateInstance(nint outer, ref Guid iid, out nint instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool value);
}

[ComVisible(true)]
sealed class ProviderFactory : IClassFactory
{
    // Keep one provider alive for all host COM activations.
    WidgetProvider? provider;
    readonly object gate = new();
    public int CreateInstance(nint outer, ref Guid iid, out nint instance)
    {
        instance = 0;
        if (outer != 0) return unchecked((int)0x80040110);
        try {
            lock (gate) {
                provider ??= new WidgetProvider();
                var unknown = WinRT.MarshalInspectable<IWidgetProvider>.FromManaged(provider);
                try { return Marshal.QueryInterface(unknown, in iid, out instance); }
                finally { Marshal.Release(unknown); }
            }
        } catch (Exception e) { return Marshal.GetHRForException(e); }
    }
    public int LockServer(bool value) => 0;
}
