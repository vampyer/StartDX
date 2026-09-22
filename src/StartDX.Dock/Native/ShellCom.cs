using System.Runtime.InteropServices;

namespace StartDX.Dock.Native;

// COM interfaces for shell items: used to enumerate shell:AppsFolder (Win32 *and* packaged apps)
// and to fetch high-resolution icons through IShellItemImageFactory (up to 256px, alpha-correct).

internal enum SIGDN : uint
{
    NORMALDISPLAY = 0x00000000,
    PARENTRELATIVEPARSING = 0x80018001,
    DESKTOPABSOLUTEPARSING = 0x80028000,
    FILESYSPATH = 0x80058000,
}

[Flags]
internal enum SIIGBF : uint
{
    RESIZETOFIT = 0x00,
    BIGGERSIZEOK = 0x01,
    MEMORYONLY = 0x02,
    ICONONLY = 0x04,
    THUMBNAILONLY = 0x08,
    INCACHEONLY = 0x10,
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig] int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
    [PreserveSig] int GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint hint, out int piOrder);
}

[ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    [PreserveSig] int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem rgelt, out uint pceltFetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumShellItems ppenum);
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
}

internal static class ShellGuids
{
    public static readonly Guid IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    public static readonly Guid IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");
    /// <summary>BHID_EnumItems - bind handler that yields an IEnumShellItems for a folder item.</summary>
    public static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
}
