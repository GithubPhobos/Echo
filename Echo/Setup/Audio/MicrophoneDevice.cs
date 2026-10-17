namespace Echo.Setup.Audio;

/// <summary>
///     A recording device.
/// </summary>
/// <param name="Index">The device index used by the 'DeviceIndex' setting.</param>
/// <param name="Name">The name shown to the user.</param>
internal sealed record MicrophoneDevice(int Index, string Name);