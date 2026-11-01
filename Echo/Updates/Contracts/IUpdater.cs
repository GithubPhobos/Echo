namespace Echo.Updates.Contracts;

/// <summary>
///     Finds out about newer versions of Echo and downloads them: by itself or on /update, as the user has chosen.
///     A downloaded version becomes the one Echo starts from; the running Echo goes on as it is.
/// </summary>
internal interface IUpdater
{
    /// <summary>
    ///     What the working screen shows about the version now.
    /// </summary>
    UpdateStatus Status { get; }

    /// <summary>
    ///     Raised, on a background thread, when there is something to tell the user.
    /// </summary>
    event EventHandler<UpdateNotice>? Noticed;

    /// <summary>
    ///     Starts the work in the background, once: removes the versions left from before, then checks for a newer one,
    ///     once Echo is ready and every day after that.
    /// </summary>
    /// <param name="isJustUpdated">Whether this start comes right after an update, which was to the latest version.</param>
    /// <param name="ready">
    ///     Done once Echo is ready, or could not start: what the check tells comes after the lines of the start-up.
    /// </param>
    void Start(bool isJustUpdated, Task ready);

    /// <summary>
    ///     Downloads the newer version, for /update.
    /// </summary>
    UpdateStart Download();
}