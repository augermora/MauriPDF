using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MauriPDF.Editing;

internal static class FilePathIdentity
{
    public static void RejectSourceAlias(string sourcePath, string destinationPath)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase)
            || (File.Exists(destinationPath) && Get(sourcePath) == Get(destinationPath)))
            throw new IOException("The destination resolves to the currently open source PDF. Choose a different file.");
    }

    private static FileIdentity Get(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info))
            throw new IOException($"Could not establish file identity for '{path}'.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        return new(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    private readonly record struct FileIdentity(uint Volume, ulong Index);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
