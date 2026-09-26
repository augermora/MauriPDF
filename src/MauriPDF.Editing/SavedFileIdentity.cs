using System.Security.Cryptography;

namespace MauriPDF.Editing;

/// <summary>A content-backed identity for the last file MauriPDF successfully published.</summary>
public sealed record SavedFileIdentity(long Length, DateTime LastWriteTimeUtc, string Sha256)
{
    public static SavedFileIdentity Capture(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileInfo before = new(Path.GetFullPath(path));
        if (!before.Exists) throw new FileNotFoundException("The saved PDF no longer exists.", before.FullName);
        long length = before.Length;
        DateTime timestamp = before.LastWriteTimeUtc;
        using FileStream stream = new(before.FullName, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        string hash = Convert.ToHexString(SHA256.HashData(stream));
        before.Refresh();
        if (!before.Exists || before.Length != length || before.LastWriteTimeUtc != timestamp)
            throw new IOException("The saved PDF changed while MauriPDF was checking it.");
        return new(length, timestamp, hash);
    }
}
