using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Echo.Setup.Hotkeys;

namespace Echo.Platform.Linux;

/// <summary>
///     Takes the key away from the other programs of an X11 session (XGrabKey): while Echo runs, the key and
///     its combination reach Echo alone, as on Windows. A key another program has taken already cannot be taken.
/// </summary>
/// <param name="logger"><see cref="ILogger{X11HotkeyListener}"/></param>
[SupportedOSPlatform("linux")]
internal sealed class X11HotkeyListener(ILogger<X11HotkeyListener> logger) : IHotkeyListener
{
    /// <summary>
    ///     How often the thread looks whether it has to stop, while no key comes.
    /// </summary>
    private const int PollMilliseconds = 100;

    /// <summary>
    ///     How long starting waits for X11 to say whether the key could be taken.
    /// </summary>
    private static readonly TimeSpan _startTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Caps Lock and Num Lock change the modifier state of every key, so the key is taken with each of them
    ///     on and off; otherwise it would not work while Num Lock is on.
    /// </summary>
    private static readonly uint[] _lockStates = [0, X11Native.LockMask, X11Native.Mod2Mask, X11Native.LockMask | X11Native.Mod2Mask];

    /// <summary>
    ///     The last error X11 reported. X11 has one error handler for the whole process, and Echo talks to X11
    ///     from one thread at a time.
    /// </summary>
    private static volatile int _lastError;

    /// <summary>
    ///     Kept for as long as the process lives: X11 calls it whenever it reports an error.
    /// </summary>
    private static readonly X11Native.ErrorHandler _errorHandler = OnError;

    private readonly ILogger<X11HotkeyListener> _logger = logger;

    private uint _keysym;
    private uint _modifier;
    private Thread? _thread;
    private volatile bool _isStopping;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Pressed;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Released;

    /// <inheritdoc/>
    public void Bind(HotkeyChoice hotkey)
    {
        if (KeyMapperService.ParseMainKey(hotkey.Key) is not { } key || XKeys.Find(key) is not { } found)
            throw new Exception("Couldn't parse push to talk key!");

        _keysym = found.Keysym;
        _modifier = XKeys.FindModifier(hotkey.Modifier)?.Mask ?? 0;

        _logger.LogDebug("{Tab}{CheckedEmoji} Key is set up to [{Modifier}{Key}]",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, _modifier == 0 ? string.Empty : $"{hotkey.Modifier} + ", found.Name);
    }

    /// <inheritdoc/>
    public async Task StartAsync()
    {
        _logger.LogDebug("{KeyboardEmoji} Keyboard (X11):", LoggerConstants.KeyboardEmoji);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _thread = new Thread(() => Listen(started)) { IsBackground = true, Name = "Push-to-talk key" };
        _thread.Start();

        // Whether the key could be taken is known only once X11 has answered
        try
        {
            await started.Task.WaitAsync(_startTimeout);
        }
        catch (TimeoutException)
        {
            throw new Exception("X11 did not answer while the push-to-talk key was being set up.");
        }

        _logger.LogDebug("{Tab}{CheckedEmoji} The key is Echo's until it stops.",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _isStopping = true;
        _thread?.Join();
    }

    /// <summary>
    ///     Takes the key and hands its presses on until Echo stops. X11 is talked to from this thread only.
    /// </summary>
    private void Listen(TaskCompletionSource started)
    {
        IntPtr display = X11Native.OpenDisplay(null);

        if (display == IntPtr.Zero)
        {
            started.TrySetException(new Exception("Couldn't connect to the X11 display."));

            return;
        }

        IntPtr xEvent = Marshal.AllocHGlobal(X11Native.EventSize);
        ulong root = X11Native.DefaultRootWindow(display);
        int keycode = X11Native.KeysymToKeycode(display, _keysym);

        try
        {
            if (keycode == 0)
            {
                started.TrySetException(new Exception("The push-to-talk key is not on this keyboard."));

                return;
            }

            // A held key repeats as pairs of "up" and "down"; this way only the "down" repeats, and those
            // are left out below, so a held key never looks released
            X11Native.SetDetectableAutoRepeat(display, 1, out _);

            if (!Grab(display, keycode, root))
            {
                started.TrySetException(new Exception(
                    "Another program already uses this push-to-talk key. Pick another one with 'echooo setup'."));

                return;
            }

            started.TrySetResult();

            Loop(display, xEvent);

            foreach (uint state in _lockStates)
                X11Native.UngrabKey(display, keycode, _modifier | state, root);
        }
        finally
        {
            Marshal.FreeHGlobal(xEvent);
            X11Native.CloseDisplay(display);
        }
    }

    /// <summary>
    ///     Takes the key with every state of Caps Lock and Num Lock. Fails as a whole when X11 refuses any of them.
    /// </summary>
    private bool Grab(IntPtr display, int keycode, ulong root)
    {
        // The handler stays: the one X11 starts with ends the whole process on any error
        X11Native.SetErrorHandler(_errorHandler);

        _lastError = 0;

        foreach (uint state in _lockStates)
            X11Native.GrabKey(display, keycode, _modifier | state, root, 0, X11Native.GrabModeAsync, X11Native.GrabModeAsync);

        // Errors of a request arrive later; this waits for X11 to have handled them all
        X11Native.Sync(display, 0);

        if (_lastError == 0)
            return true;

        foreach (uint state in _lockStates)
            X11Native.UngrabKey(display, keycode, _modifier | state, root);

        X11Native.Sync(display, 0);

        return false;
    }

    private void Loop(IntPtr display, IntPtr xEvent)
    {
        var descriptors = new[] { new X11Native.PollDescriptor { Descriptor = X11Native.ConnectionNumber(display), Events = X11Native.PollIn } };
        bool isDown = false;

        while (!_isStopping)
        {
            // Waits for X11 a little at a time, so stopping is noticed without an event to wake the thread
            if (X11Native.Pending(display) == 0)
            {
                X11Native.Poll(descriptors, 1, PollMilliseconds);

                continue;
            }

            X11Native.NextEvent(display, xEvent);

            switch (Marshal.ReadInt32(xEvent))
            {
                case X11Native.KeyPress when !isDown:
                    isDown = true;
                    Pressed?.Invoke(this, new HotkeyEventArgs());

                    break;

                case X11Native.KeyRelease when isDown:
                    isDown = false;
                    Released?.Invoke(this, new HotkeyEventArgs());

                    break;
            }
        }
    }

    private static int OnError(IntPtr display, IntPtr errorEvent)
    {
        _lastError = Marshal.ReadByte(errorEvent, X11Native.ErrorCodeOffset);

        return 0;
    }
}