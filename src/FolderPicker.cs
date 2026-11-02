using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pixelroom
{
    public static class FolderPicker
    {
        public static string[] SelectFolders(IWin32Window owner)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog(); IShellItemArray items = null;
            try
            {
                uint options; dialog.GetOptions(out options);
                dialog.SetOptions(options | 0x20u | 0x40u | 0x200u | 0x800u | 0x8u | 0x2000000u);
                dialog.SetTitle("Select folders to upload"); dialog.SetOkButtonLabel("Select folders");
                int result = dialog.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (result == unchecked((int)0x800704C7)) return new string[0]; Marshal.ThrowExceptionForHR(result);
                dialog.GetResults(out items); uint count; items.GetCount(out count); List<string> paths = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    IShellItem item = null; IntPtr name = IntPtr.Zero;
                    try { items.GetItemAt(i, out item); item.GetDisplayName(0x80058000, out name); paths.Add(Marshal.PtrToStringUni(name)); }
                    finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); if (item != null) Marshal.ReleaseComObject(item); }
                }
                return paths.ToArray();
            }
            finally { if (items != null) Marshal.ReleaseComObject(items); Marshal.ReleaseComObject(dialog); }
        }
        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }
        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, IntPtr types);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem selection);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName(out IntPtr name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int location);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray results);
            void GetSelectedItems(out IShellItemArray items);
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
        [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetPropertyStore(int flags, ref Guid iid, out IntPtr store);
            void GetPropertyDescriptionList(IntPtr key, ref Guid iid, out IntPtr list);
            void GetAttributes(uint flags, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }
    }
}
