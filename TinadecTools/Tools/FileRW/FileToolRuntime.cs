using System.Collections.Concurrent;
using AsyncLocks;

namespace TinadecTools.Tools.FileRW;

internal sealed class FileSlot(string path)
{
    public FileAccessor File { get; } = new(path);
    public AsyncReaderWriterLock RwLock { get; } = new();
}

internal static class FileToolRuntime
{
    private static readonly ConcurrentDictionary<string, FileSlot> Locks = new();

    public static FileSlot GetFileHandle(string path)
    {
        return Locks.GetOrAdd(path, p => new FileSlot(p));
    }

    public static string ResolvePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Path.GetFullPath(filePath);
    }
}
