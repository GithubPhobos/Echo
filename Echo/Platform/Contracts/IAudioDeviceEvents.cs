namespace Echo.Platform.Contracts;

/// <summary>
///     Says when the sound devices of the system change: one is plugged in or out, or comes back after the computer
///     slept. The system closes the sound of a device that goes away, and a program that had it open goes on hearing
///     nothing from it, and playing to nothing, even once it is back.
/// </summary>
internal interface IAudioDeviceEvents
{
    /// <summary>
    ///     The microphones of the system changed. Raised once they have settled, on a thread of its own.
    /// </summary>
    event EventHandler? MicrophonesChanged;

    /// <summary>
    ///     The loudspeakers of the system changed, or the system plays to other ones by default. Raised once they have
    ///     settled, on a thread of its own.
    /// </summary>
    event EventHandler? LoudspeakersChanged;
}