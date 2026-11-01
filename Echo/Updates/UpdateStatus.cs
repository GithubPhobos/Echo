namespace Echo.Updates;

/// <summary>
///     What the working screen says about the version of Echo at one moment.
/// </summary>
/// <param name="Stage"><see cref="UpdateStage"/></param>
/// <param name="Installed">The version in use, which is always shown.</param>
/// <param name="Newer">The newer version that is out, being downloaded or downloaded; null when there is none.</param>
/// <param name="Progress">How much of the newer version is downloaded, from 0 to 1, while it is being downloaded.</param>
internal sealed record UpdateStatus(UpdateStage Stage, string Installed, string? Newer = null, double Progress = 0)
{
    /// <summary>
    ///     How much is downloaded, in whole percent.
    /// </summary>
    public int Percent => (int)Math.Floor(Math.Clamp(Progress, 0, 1) * 100);
}