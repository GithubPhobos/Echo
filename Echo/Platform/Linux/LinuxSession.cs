namespace Echo.Platform.Linux;

/// <summary>
///     Tells which display server the user's session runs on, from the variables the session sets for every program.
/// </summary>
internal static class LinuxSession
{
    /// <summary>
    ///     Decides from the session variables.
    /// </summary>
    /// <param name="variable">Reads an environment variable.</param>
    /// <remarks>
    ///     The session type is the surest sign. Without it, a Wayland display wins over an X11 one: a Wayland
    ///     session also runs X11 programs and so sets both, while an X11 session never sets the Wayland one.
    /// </remarks>
    public static DisplayServer Detect(Func<string, string?> variable)
    {
        string? type = variable("XDG_SESSION_TYPE");

        if (string.Equals(type, "wayland", StringComparison.OrdinalIgnoreCase))
            return DisplayServer.Wayland;

        if (string.Equals(type, "x11", StringComparison.OrdinalIgnoreCase))
            return DisplayServer.X11;

        if (!string.IsNullOrEmpty(variable("WAYLAND_DISPLAY")))
            return DisplayServer.Wayland;

        return string.IsNullOrEmpty(variable("DISPLAY")) ? DisplayServer.None : DisplayServer.X11;
    }
}