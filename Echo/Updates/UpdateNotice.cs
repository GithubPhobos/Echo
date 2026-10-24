namespace Echo.Updates;

/// <summary>
///     Something the updater tells the user in the log.
/// </summary>
/// <param name="Kind"><see cref="UpdateNoticeKind"/></param>
/// <param name="Version">The version it is about: the newer one, or the one in use for <see cref="UpdateNoticeKind.UpToDate"/>.</param>
/// <param name="Reason">What went wrong, in English, for the notices of failures.</param>
internal sealed record UpdateNotice(UpdateNoticeKind Kind, string Version, string? Reason = null);