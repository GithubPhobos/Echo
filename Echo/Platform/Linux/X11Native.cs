using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     The client library of X11, asked directly for the few things Echo needs: taking one key away from the other
///     programs, and telling which window is in front.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class X11Native
{
    private const string Library = "libX11.so.6";
    private const string LibC = "libc";

    /// <summary>
    ///     Event types: a key went down, a key came up.
    /// </summary>
    public const int KeyPress = 2;
    public const int KeyRelease = 3;

    /// <summary>
    ///     The modifier bits of Caps Lock and Num Lock, which are on or off without the user holding anything.
    /// </summary>
    public const uint LockMask = 2;
    public const uint Mod2Mask = 16;

    /// <summary>
    ///     GrabModeAsync: events keep flowing to everyone else while Echo holds its key.
    /// </summary>
    public const int GrabModeAsync = 1;

    /// <summary>
    ///     The error a grab gets when another program holds the same key.
    /// </summary>
    public const byte BadAccess = 10;

    /// <summary>
    ///     XA_WINDOW: the type of a property that holds a window.
    /// </summary>
    public const ulong WindowType = 33;

    /// <summary>
    ///     The size of XEvent, the union of all events: 24 machine words.
    /// </summary>
    public const int EventSize = 24 * 8;

    /// <summary>
    ///     The place of the error code in XErrorEvent: after the type (4 and 4 of padding), the display (8),
    ///     the resource (8) and the serial number (8).
    /// </summary>
    public const int ErrorCodeOffset = 32;

    /// <summary>
    ///     The place of the key code in XKeyEvent on 64-bit Linux: after the type (4 and 4 of padding), the serial
    ///     number (8), send_event (4 and 4), the display (8), the window, the root and the subwindow (8 each),
    ///     the time (8), x, y, x_root and y_root (4 each) and the state (4).
    /// </summary>
    public const int KeycodeOffset = 84;

    /// <summary>
    ///     POLLIN: data to read.
    /// </summary>
    public const short PollIn = 1;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);

    [StructLayout(LayoutKind.Sequential)]
    public struct PollDescriptor
    {
        public int Descriptor;
        public short Events;
        public short ReturnedEvents;
    }

    [DllImport(Library, EntryPoint = "XOpenDisplay")]
    public static extern IntPtr OpenDisplay(string? name);

    [DllImport(Library, EntryPoint = "XCloseDisplay")]
    public static extern int CloseDisplay(IntPtr display);

    [DllImport(Library, EntryPoint = "XDefaultRootWindow")]
    public static extern ulong DefaultRootWindow(IntPtr display);

    [DllImport(Library, EntryPoint = "XKeysymToKeycode")]
    public static extern byte KeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport(Library, EntryPoint = "XGrabKey")]
    public static extern int GrabKey(IntPtr display, int keycode, uint modifiers, ulong window, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(Library, EntryPoint = "XUngrabKey")]
    public static extern int UngrabKey(IntPtr display, int keycode, uint modifiers, ulong window);

    [DllImport(Library, EntryPoint = "XSync")]
    public static extern int Sync(IntPtr display, int discard);

    [DllImport(Library, EntryPoint = "XPending")]
    public static extern int Pending(IntPtr display);

    [DllImport(Library, EntryPoint = "XNextEvent")]
    public static extern int NextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport(Library, EntryPoint = "XConnectionNumber")]
    public static extern int ConnectionNumber(IntPtr display);

    [DllImport(Library, EntryPoint = "XSetErrorHandler")]
    public static extern IntPtr SetErrorHandler(ErrorHandler? handler);

    [DllImport(Library, EntryPoint = "XkbSetDetectableAutoRepeat")]
    public static extern int SetDetectableAutoRepeat(IntPtr display, int detectable, out int supported);

    [DllImport(Library, EntryPoint = "XInternAtom")]
    public static extern ulong InternAtom(IntPtr display, string name, int onlyIfExists);

    [DllImport(Library, EntryPoint = "XGetWindowProperty")]
    public static extern int GetWindowProperty(IntPtr display,
                                               ulong window,
                                               ulong property,
                                               long offset,
                                               long length,
                                               int delete,
                                               ulong requestedType,
                                               out ulong actualType,
                                               out int actualFormat,
                                               out ulong itemCount,
                                               out ulong bytesAfter,
                                               out IntPtr value);

    [DllImport(Library, EntryPoint = "XQueryTree")]
    public static extern int QueryTree(IntPtr display, ulong window, out ulong root, out ulong parent, out IntPtr children, out uint childCount);

    [DllImport(Library, EntryPoint = "XFree")]
    public static extern int Free(IntPtr data);

    [DllImport(LibC, EntryPoint = "poll", SetLastError = true)]
    public static extern int Poll([In, Out] PollDescriptor[] descriptors, ulong count, int timeoutMilliseconds);
}