using System.Runtime.InteropServices;

namespace DevHub.Core.Services;

/// <summary>Recycle-bin deletion via the Windows shell (SHFileOperation with FOF_ALLOWUNDO).</summary>
public sealed class ShellFileOperations : IFileOperations
{
    private const ushort FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    public Task RecycleAsync(string path, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            var operation = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                // Double null-terminated list of paths.
                pFrom = path + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
            };

            var result = SHFileOperation(ref operation);
            if (result != 0 || operation.fAnyOperationsAborted)
            {
                throw new IOException($"Could not move '{path}' to the Recycle Bin (error {result}).");
            }
        }, cancellationToken);
    }

    public Task<string> RenameFolderAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var trimmed = newName.Trim();
            if (trimmed.Length == 0)
            {
                throw new ArgumentException("The name cannot be empty.");
            }

            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("The name contains characters that are not allowed in folder names.");
            }

            var parent = Path.GetDirectoryName(path)
                ?? throw new ArgumentException($"'{path}' has no parent folder.");
            var target = Path.Combine(parent, trimmed);

            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (Directory.Exists(target) || File.Exists(target))
            {
                throw new IOException($"A folder named '{trimmed}' already exists.");
            }

            Directory.Move(path, target);
            return target;
        }, cancellationToken);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }
}
