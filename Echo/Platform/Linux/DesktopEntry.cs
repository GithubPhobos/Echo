using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     Echo's launcher in the applications menu: a '.desktop' file named after Echo's application ID. A Wayland
///     desktop knows programs by their launchers: Ubuntu 26.04 refuses to register an application ID it has no
///     launcher for ("App info not found"), and then gives Echo no key. Until an installer puts the launcher in
///     place, Echo writes its own, for the current user.
/// </summary>
/// <param name="directory">The folder of the user's launchers, '~/.local/share/applications'.</param>
/// <param name="executable">The program the launcher starts.</param>
[SupportedOSPlatform("linux")]
internal sealed class DesktopEntry(string directory, string executable)
{
    private const string Extension = ".desktop";

    private readonly string _directory = directory;
    private readonly string _executable = executable;

    /// <summary>
    ///     Where the launcher is.
    /// </summary>
    public string FilePath => Path.Combine(_directory, DesktopPortal.ApplicationId + Extension);

    /// <summary>
    ///     The launcher of this copy of Echo, in the folder the XDG standard gives the user's launchers.
    /// </summary>
    /// <param name="variable">Reads an environment variable.</param>
    /// <param name="userHome">The home folder of the user.</param>
    public static DesktopEntry ForCurrentUser(Func<string, string?> variable, string userHome)
    {
        // The program itself, also when .NET started it from its library
        string program = Path.Combine(AppContext.BaseDirectory, ApplicationInfo.CommandName);

        return new DesktopEntry(
            Path.Combine(AppPaths.XdgDirectory(variable, "XDG_DATA_HOME", userHome, ".local", "share"), "applications"),
            File.Exists(program) ? program : Environment.ProcessPath ?? program);
    }

    /// <summary>
    ///     Writes the launcher unless it is there already, as it should be. A launcher that starts another copy of
    ///     Echo is written anew.
    /// </summary>
    /// <returns>Whether the launcher was written now: the desktop may take a moment to notice a new one.</returns>
    public bool Ensure()
    {
        string content = Content(_executable);

        try
        {
            if (File.Exists(FilePath) && File.ReadAllText(FilePath) == content)
                return false;

            Directory.CreateDirectory(_directory);

            // Written next to the target first, so the desktop never reads half a launcher
            string temporaryFile = FilePath + ".tmp";
            File.WriteAllText(temporaryFile, content);
            File.Move(temporaryFile, FilePath, overwrite: true);

            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            // Without the launcher the desktop says why it refuses; that is shown where the key is asked for
            return false;
        }
    }

    /// <summary>
    ///     The launcher: Echo in a terminal, in the Utilities and Accessibility parts of the menu. Its lines end the
    ///     Linux way also when Echo is built on Windows, where this text would take the line ends of the source file.
    /// </summary>
    internal static string Content(string executable) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=Echo
        Comment=Voice typing: hold a key and speak
        Exec={EscapeValue(Quote(executable))}
        Terminal=true
        Categories=Utility;Accessibility;

        """.ReplaceLineEndings("\n");

    /// <summary>
    ///     A path as the Exec line of a launcher takes it: one argument in double quotes, with the characters that
    ///     are special inside them escaped by a backslash, and '%' doubled, since a lone one starts a field code.
    /// </summary>
    internal static string Quote(string path)
    {
        var quoted = new StringBuilder("\"");

        foreach (char character in path)
        {
            if (character is '"' or '`' or '$' or '\\')
                quoted.Append('\\');
            else if (character == '%')
                quoted.Append('%');

            quoted.Append(character);
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>
    ///     A text value of a launcher: a backslash, a line break and a tab are written as "\\", "\n" and "\t". This
    ///     comes on top of <see cref="Quote"/>, so a backslash of the path takes four in the file.
    /// </summary>
    internal static string EscapeValue(string value)
    {
        var escaped = new StringBuilder();

        foreach (char character in value)
        {
            escaped.Append(character switch
            {
                '\\' => @"\\",
                '\n' => @"\n",
                '\t' => @"\t",
                '\r' => @"\r",
                _ => character.ToString()
            });
        }

        return escaped.ToString();
    }
}