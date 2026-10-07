using System.Runtime.InteropServices;
namespace Papergraph;
// Foundation's trash operation handles duplicate names and volumes; never replace a trash item.
internal static class MacTrash
{
    const string ObjC="/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)]static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)]static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern IntPtr Send(IntPtr receiver,IntPtr selector);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern IntPtr SendString(IntPtr receiver,IntPtr selector,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")]static extern IntPtr SendObject(IntPtr receiver,IntPtr selector,IntPtr value);
    [DllImport(ObjC,EntryPoint="objc_msgSend")][return:MarshalAs(UnmanagedType.I1)]static extern bool Trash(IntPtr receiver,IntPtr selector,IntPtr url,IntPtr result,out IntPtr error);
    internal static void Move(string path)
    {
        var text=SendString(objc_getClass("NSString"),sel_registerName("stringWithUTF8String:"),Path.GetFullPath(path));
        var url=SendObject(objc_getClass("NSURL"),sel_registerName("fileURLWithPath:"),text);
        var manager=Send(objc_getClass("NSFileManager"),sel_registerName("defaultManager"));
        if(!Trash(manager,sel_registerName("trashItemAtURL:resultingItemURL:error:"),url,IntPtr.Zero,out var error))throw new IOException("Could not move the graph to Trash.");
    }
}
