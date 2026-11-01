namespace Echo.Updates;

/// <summary>
///     The releases of Echo on GitHub. The latest one is asked of the GitHub API, which answers 60 times an hour
///     to anyone without signing in: Echo asks at every start and once a day.
/// </summary>
/// <param name="httpClient">The client for the API and the list of checksums.</param>
/// <param name="latestRelease">The address that describes the latest release.</param>
/// <param name="archiveName">The name of the archive for this system.</param>
internal sealed class GitHubReleaseSource(HttpClient httpClient, Uri latestRelease, string archiveName) : IReleaseSource
{
    /// <summary>
    ///     The latest release of Echo in the GitHub API. A release marked as a pre-release is never the latest one.
    /// </summary>
    public const string LatestRelease = "https://api.github.com/repos/GithubPhobos/Echo/releases/latest";

    /// <summary>
    ///     Environment variable that points Echo at other releases, such as a test server.
    /// </summary>
    public const string AddressVariable = "ECHOOO_RELEASES_URL";

    /// <summary>
    ///     The file of every release that lists the checksums of its archives, as the 'sha256sum' program writes them.
    /// </summary>
    public const string ChecksumsName = "SHA256SUMS";

    private readonly HttpClient _httpClient = httpClient;
    private readonly Uri _latestRelease = latestRelease;
    private readonly string _archiveName = archiveName;

    /// <summary>
    ///     The address of the latest release: the one of <see cref="AddressVariable"/> when it is set.
    /// </summary>
    /// <param name="variable">Reads an environment variable.</param>
    public static Uri Address(Func<string, string?> variable) =>
        Uri.TryCreate(variable(AddressVariable), UriKind.Absolute, out Uri? address) ? address : new Uri(LatestRelease);

    /// <inheritdoc/>
    public async Task<ReleaseInfo> LatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _latestRelease);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            return Read(document.RootElement);
        }
        catch (Exception exc) when (exc is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
        {
            throw new InvalidDataException($"The description of the latest release cannot be read: {exc.Message}", exc);
        }
    }

    /// <inheritdoc/>
    public async Task<string> ChecksumAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        if (release.ChecksumsUrl is not { } address)
            throw new InvalidDataException($"Echo {release.Version} has no {ChecksumsName}.");

        string checksums = await _httpClient.GetStringAsync(address, cancellationToken);

        foreach (string line in checksums.Split('\n'))
        {
            string[] parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // "<checksum>  <name>", or "<checksum> *<name>" for a file read as binary
            if (parts.Length == 2 && parts[1].TrimStart('*') == release.ArchiveName && IsChecksum(parts[0]))
                return parts[0];
        }

        throw new InvalidDataException($"{ChecksumsName} of Echo {release.Version} has no checksum for {release.ArchiveName}.");
    }

    /// <summary>
    ///     The release a description of the GitHub API names: its version from the tag, its archive for this system
    ///     and its list of checksums. A release may lack them, as the releases of Echo 1.x do: then it only tells
    ///     whether a newer version is out.
    /// </summary>
    private ReleaseInfo Read(JsonElement release)
    {
        string tag = release.GetProperty("tag_name").GetString() ?? string.Empty;

        if (ReleaseVersion.Parse(tag) is not { } version)
            throw new InvalidDataException($"The latest release is tagged '{tag}', which is not a version.");

        JsonElement? archive = null;
        JsonElement? checksums = null;

        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();

            if (name == _archiveName)
                archive = asset;
            else if (name == ChecksumsName)
                checksums = asset;
        }

        return new ReleaseInfo(
            version.ToString(3),
            _archiveName,
            archive is { } found ? Address(found) : null,
            archive?.GetProperty("size").GetInt64() ?? 0,
            checksums is { } list ? Address(list) : null);
    }

    private static Uri Address(JsonElement asset) =>
        new(asset.GetProperty("browser_download_url").GetString()!);

    private static bool IsChecksum(string text) =>
        text.Length == 64 && text.All(Uri.IsHexDigit);
}