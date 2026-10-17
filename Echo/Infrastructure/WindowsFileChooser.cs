using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Infrastructure;

/// <summary>
///     The standard "Open" window of Windows, asked for directly: the application has no window toolkit of its own.
/// </summary>
internal static class WindowsFileChooser
{
    private const int BufferChars = 32 * 1024;

    private const int HideReadOnly = 0x00000004;
    private const int NoChangeDirectory = 0x00000008;
    private const int AllowMultiSelect = 0x00000200;
    private const int PathMustExist = 0x00000800;
    private const int FileMustExist = 0x00001000;
    private const int Explorer = 0x00080000;

    /// <summary>
    ///     Shows the chooser and returns the full paths of the picked files.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Task<IReadOnlyList<string>> ShowAsync(string title, string pattern)
    {
        var result = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        // The window belongs to whatever is in front, which is the terminal the user is typing in
        IntPtr owner = GetForegroundWindow();

        // Shell windows need a thread set up for them; the thread pool has none
        var thread = new Thread(() =>
        {
            try
            {
                result.SetResult(Show(owner, title, pattern));
            }
            catch (Exception exc)
            {
                result.SetException(exc);
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return result.Task;
    }

    /// <summary>
    ///     Turns what the chooser wrote into full paths. One file comes as its full path; several come as
    ///     their folder followed by the file names. The pieces are separated by a zero character.
    /// </summary>
    internal static IReadOnlyList<string> ParseSelection(string buffer)
    {
        string[] parts = buffer.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length <= 1
            ? parts
            : [.. parts.Skip(1).Select(name => Path.Combine(parts[0], name))];
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> Show(IntPtr owner, string title, string pattern)
    {
        IntPtr buffer = Marshal.AllocHGlobal(BufferChars * sizeof(char));

        try
        {
            // An empty string to start with: no file is suggested
            Marshal.WriteInt16(buffer, 0);

            var dialog = new OpenFileName
            {
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Owner = owner,
                Filter = $"{pattern}\0{pattern}\0\0",
                File = buffer,
                MaxFile = BufferChars,
                Title = title,
                Flags = Explorer | AllowMultiSelect | FileMustExist | PathMustExist | NoChangeDirectory | HideReadOnly
            };

            if (!GetOpenFileName(ref dialog))
                return [];

            // The answer ends with two zero characters in a row
            int length = 0;
            while (length < BufferChars - 1
                   && (Marshal.ReadInt16(buffer, length * sizeof(char)) != 0 || Marshal.ReadInt16(buffer, (length + 1) * sizeof(char)) != 0))
            {
                length++;
            }

            return ParseSelection(Marshal.PtrToStringUni(buffer, length) ?? string.Empty);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    /// <summary>
    ///     The OPENFILENAMEW structure of the Windows API; the order and size of the fields are fixed by it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public int StructSize;
        public IntPtr Owner;
        public IntPtr Instance;
        public string? Filter;
        public IntPtr CustomFilter;
        public int MaxCustomFilter;
        public int FilterIndex;
        public IntPtr File;
        public int MaxFile;
        public IntPtr FileTitle;
        public int MaxFileTitle;
        public string? InitialDirectory;
        public string? Title;
        public int Flags;
        public short FileOffset;
        public short FileExtension;
        public string? DefaultExtension;
        public IntPtr CustomData;
        public IntPtr Hook;
        public string? TemplateName;
        public IntPtr Reserved;
        public int ReservedFlags;
        public int FlagsEx;
    }
}