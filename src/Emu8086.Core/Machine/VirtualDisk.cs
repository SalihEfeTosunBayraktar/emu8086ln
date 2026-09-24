namespace Emu8086.Core.Machine;

/// <summary>
/// Sandboxed DOS drive C: mapped to a host folder. DOS paths can never escape the root.
/// </summary>
public sealed class VirtualDisk : IDisposable
{
    public const int ErrorFileNotFound = 2;
    public const int ErrorPathNotFound = 3;
    public const int ErrorTooManyFiles = 4;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidHandle = 6;
    private const int FirstHandle = 5;
    private const int MaxHandles = 20;

    private readonly Dictionary<int, FileStream> _handles = new();
    private string[] _findResults = [];
    private int _findIndex;

    public VirtualDisk(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
    }

    public string RootPath { get; }
    /// <summary>Current directory relative to the root, DOS style without leading backslash.</summary>
    public string CurrentDirectory { get; private set; } = "";

    private void EnsureRoot() => Directory.CreateDirectory(RootPath);

    /// <summary>Maps a DOS path to a host path inside the root; null if it would escape.</summary>
    public string? MapPath(string dosPath)
    {
        string p = dosPath.Trim().Replace('/', '\\');
        if (p.Length >= 2 && p[1] == ':') p = p[2..];
        string relative = p.StartsWith('\\') ? p.TrimStart('\\') : Path.Combine(CurrentDirectory, p);
        string full = Path.GetFullPath(Path.Combine(RootPath, relative));
        string root = RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !full.Equals(RootPath, StringComparison.OrdinalIgnoreCase))
            return null;
        return full;
    }

    private static int ErrorFor(Exception e) => e switch
    {
        FileNotFoundException => ErrorFileNotFound,
        DirectoryNotFoundException => ErrorPathNotFound,
        UnauthorizedAccessException => ErrorAccessDenied,
        _ => ErrorAccessDenied,
    };

    private int AddHandle(FileStream stream)
    {
        for (int h = FirstHandle; h < MaxHandles; h++)
        {
            if (_handles.ContainsKey(h)) continue;
            _handles[h] = stream;
            return h;
        }
        stream.Dispose();
        return -ErrorTooManyFiles;
    }

    /// <returns>Handle (&gt;0) or negative DOS error code.</returns>
    public int Create(string dosPath)
    {
        EnsureRoot();
        var path = MapPath(dosPath);
        if (path == null) return -ErrorAccessDenied;
        try { return AddHandle(new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return -ErrorFor(e); }
    }

    public int Open(string dosPath, int mode)
    {
        EnsureRoot();
        var path = MapPath(dosPath);
        if (path == null) return -ErrorAccessDenied;
        var access = (mode & 3) switch
        {
            0 => FileAccess.Read,
            1 => FileAccess.Write,
            _ => FileAccess.ReadWrite,
        };
        try { return AddHandle(new FileStream(path, FileMode.Open, access, FileShare.ReadWrite)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return -ErrorFor(e); }
    }

    public int Close(int handle)
    {
        if (!_handles.Remove(handle, out var s)) return -ErrorInvalidHandle;
        s.Dispose();
        return 0;
    }

    public int Read(int handle, byte[] buffer)
    {
        if (!_handles.TryGetValue(handle, out var s)) return -ErrorInvalidHandle;
        try { return s.Read(buffer, 0, buffer.Length); }
        catch (Exception e) when (e is IOException or NotSupportedException) { return -ErrorAccessDenied; }
    }

    public int Write(int handle, ReadOnlySpan<byte> data)
    {
        if (!_handles.TryGetValue(handle, out var s)) return -ErrorInvalidHandle;
        try
        {
            if (data.Length == 0) s.SetLength(s.Position); // DOS: writing 0 bytes truncates
            else s.Write(data);
            s.Flush();
            return data.Length;
        }
        catch (Exception e) when (e is IOException or NotSupportedException) { return -ErrorAccessDenied; }
    }

    public long Seek(int handle, int origin, int offset)
    {
        if (!_handles.TryGetValue(handle, out var s)) return -ErrorInvalidHandle;
        var o = origin switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, _ => SeekOrigin.End };
        try { return s.Seek(offset, o); }
        catch (IOException) { return -ErrorAccessDenied; }
    }

    public int Delete(string dosPath)
    {
        var path = MapPath(dosPath);
        if (path == null) return -ErrorAccessDenied;
        if (!File.Exists(path)) return -ErrorFileNotFound;
        try { File.Delete(path); return 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return -ErrorAccessDenied; }
    }

    public int Rename(string from, string to)
    {
        var a = MapPath(from);
        var b = MapPath(to);
        if (a == null || b == null) return -ErrorAccessDenied;
        if (!File.Exists(a)) return -ErrorFileNotFound;
        try { File.Move(a, b); return 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return -ErrorAccessDenied; }
    }

    public int MakeDirectory(string dosPath)
    {
        EnsureRoot();
        var path = MapPath(dosPath);
        if (path == null) return -ErrorAccessDenied;
        if (Directory.Exists(path) || File.Exists(path)) return -ErrorAccessDenied;
        try { Directory.CreateDirectory(path); return 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return -ErrorPathNotFound; }
    }

    public int RemoveDirectory(string dosPath)
    {
        var path = MapPath(dosPath);
        if (path == null || path.Equals(RootPath, StringComparison.OrdinalIgnoreCase)) return -ErrorAccessDenied;
        if (!Directory.Exists(path)) return -ErrorPathNotFound;
        try { Directory.Delete(path); return 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return -ErrorAccessDenied; }
    }

    public int ChangeDirectory(string dosPath)
    {
        EnsureRoot();
        var path = MapPath(dosPath);
        if (path == null || !Directory.Exists(path)) return -ErrorPathNotFound;
        CurrentDirectory = Path.GetRelativePath(RootPath, path) is var rel && rel == "." ? "" : rel;
        return 0;
    }

    public int GetAttributes(string dosPath)
    {
        var path = MapPath(dosPath);
        if (path == null) return -ErrorAccessDenied;
        if (Directory.Exists(path)) return 0x10;
        if (!File.Exists(path)) return -ErrorFileNotFound;
        return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 ? 0x21 : 0x20;
    }

    /// <summary>Starts a wildcard search; returns the first match or null.</summary>
    public FileInfo? FindFirst(string pattern)
    {
        EnsureRoot();
        var full = MapPath(pattern);
        if (full == null) return null;
        string dir = Path.GetDirectoryName(full) ?? RootPath;
        string mask = Path.GetFileName(full);
        _findResults = Directory.Exists(dir) ? Directory.GetFiles(dir, string.IsNullOrEmpty(mask) ? "*" : mask) : [];
        _findIndex = 0;
        return FindNext();
    }

    public FileInfo? FindNext() =>
        _findIndex < _findResults.Length ? new FileInfo(_findResults[_findIndex++]) : null;

    public void CloseAll()
    {
        foreach (var s in _handles.Values) s.Dispose();
        _handles.Clear();
        CurrentDirectory = "";
    }

    public void Dispose() => CloseAll();
}
