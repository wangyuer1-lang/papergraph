using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;
namespace Papergraph;

// Window.Icon is a no-op in Avalonia's macOS backend. Set NSApplication's icon
// explicitly as well, including launches from the executable or `dotnet run`.
internal static class MacApplicationIcon
{
    const string ObjC="/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)]static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)]static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern IntPtr Send(IntPtr receiver,IntPtr selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern IntPtr SendObject(IntPtr receiver,IntPtr selector,IntPtr value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern IntPtr SendBytes(IntPtr receiver,IntPtr selector,IntPtr bytes,nuint length);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern nuint SendLength(IntPtr receiver,IntPtr selector);
    internal static bool NativeIconApplied {get;private set;}
    static Stream Resource(string extension)=>Assembly.GetExecutingAssembly().GetManifestResourceStream("Papergraph.ApplicationIcon."+extension)??throw new InvalidOperationException("Missing application icon.");
    internal static WindowIcon WindowIcon()
    {
        using var stream=Resource("ico");return new WindowIcon(stream);
    }
    internal static void Apply()
    {
        if(!OperatingSystem.IsMacOS())return;
        using var stream=Resource("icns");using var memory=new MemoryStream();stream.CopyTo(memory);var bytes=memory.ToArray();
        var buffer=Marshal.AllocHGlobal(bytes.Length);IntPtr image=IntPtr.Zero;
        try
        {
            Marshal.Copy(bytes,0,buffer,bytes.Length);
            var data=SendBytes(objc_getClass("NSData"),sel_registerName("dataWithBytes:length:"),buffer,(nuint)bytes.Length);
            var allocated=Send(objc_getClass("NSImage"),sel_registerName("alloc"));
            image=SendObject(allocated,sel_registerName("initWithData:"),data);
            if(image==IntPtr.Zero)throw new InvalidOperationException("Could not decode application icon.");
            var app=Send(objc_getClass("NSApplication"),sel_registerName("sharedApplication"));
            SendObject(app,sel_registerName("setApplicationIconImage:"),image);
            // AppKit copies and color-converts the assigned image. Its TIFF
            // encoding can differ even when the displayed icon is identical.
            var actualIcon=Send(app,sel_registerName("applicationIconImage"));
            NativeIconApplied=actualIcon!=IntPtr.Zero;
            if(Program.SelfTest)
            {
                File.WriteAllBytes(Path.Combine(Program.DataDirectory,"app-icon-expected.tiff"),ImageData(image));
                File.WriteAllBytes(Path.Combine(Program.DataDirectory,"app-icon-native.tiff"),ImageData(actualIcon));
            }
        }
        finally
        {
            if(image!=IntPtr.Zero)Send(image,sel_registerName("release"));Marshal.FreeHGlobal(buffer);
        }
    }
    static byte[] ImageData(IntPtr image)
    {
        if(image==IntPtr.Zero)return [];
        var data=Send(image,sel_registerName("TIFFRepresentation"));if(data==IntPtr.Zero)return [];
        var length=checked((int)SendLength(data,sel_registerName("length")));var bytes=new byte[length];
        Marshal.Copy(Send(data,sel_registerName("bytes")),bytes,0,length);return bytes;
    }
}
