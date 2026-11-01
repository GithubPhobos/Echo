namespace Echo.Setup.Hardware.Contracts;

/// <summary>
///     Finds out what hardware acceleration the machine can offer.
/// </summary>
internal interface IHardwareProbe
{
    Task<HardwareInfo> DetectAsync(CancellationToken cancellationToken);
}