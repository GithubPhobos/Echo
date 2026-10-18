namespace Echo.Platform.Linux;

/// <summary>
///     How a distribution installs programs, told from its own description in '/etc/os-release'.
/// </summary>
/// <param name="Command">The command that installs packages, with "{0}" for the package.</param>
/// <param name="Family">Which family of distributions it serves, for the names of packages that differ.</param>
internal sealed record PackageManager(string Command, string Family)
{
    public const string Debian = "debian";
    public const string Fedora = "fedora";
    public const string Arch = "arch";
    public const string Suse = "suse";

    private const string OsRelease = "/etc/os-release";

    /// <summary>
    ///     The package manager of this system, or null for a distribution Echo does not know.
    /// </summary>
    public static PackageManager? Detect() =>
        File.Exists(OsRelease) ? FromOsRelease(File.ReadAllLines(OsRelease)) : null;

    /// <summary>
    ///     Reads the 'ID' and 'ID_LIKE' lines: a distribution names itself and the ones it is based on,
    ///     so Linux Mint says "ubuntu debian" and is installed to like Debian.
    /// </summary>
    internal static PackageManager? FromOsRelease(IEnumerable<string> lines)
    {
        string[] families =
        [
            .. lines
                .Where(line => line.StartsWith("ID=", StringComparison.Ordinal) || line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                .SelectMany(line => line[(line.IndexOf('=') + 1)..].Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries))
        ];

        if (families.Any(family => family is "debian" or "ubuntu"))
            return new PackageManager("sudo apt install {0}", Debian);

        if (families.Any(family => family is "fedora" or "rhel" or "centos"))
            return new PackageManager("sudo dnf install {0}", Fedora);

        if (families.Any(family => family is "arch"))
            return new PackageManager("sudo pacman -S {0}", Arch);

        if (families.Any(family => family.StartsWith("opensuse", StringComparison.Ordinal) || family is "suse"))
            return new PackageManager("sudo zypper install {0}", Suse);

        return null;
    }

    /// <summary>
    ///     The command that installs the package, by its name in this family of distributions.
    /// </summary>
    /// <param name="packages">The name of the package in each family; a family not listed uses the first name.</param>
    public string Install(IReadOnlyDictionary<string, string> packages) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, Command, PackageOf(packages));

    /// <summary>
    ///     The name of the package in this family of distributions.
    /// </summary>
    /// <param name="packages">The name of the package in each family; a family not listed uses the first name.</param>
    public string PackageOf(IReadOnlyDictionary<string, string> packages) =>
        packages.TryGetValue(Family, out string? name) ? name : packages.Values.First();

    /// <summary>
    ///     The command line that installs all the packages at once without asking anything but the password:
    ///     the program first, then its arguments.
    /// </summary>
    public IReadOnlyList<string> InstallCommand(IEnumerable<string> packages) =>
    [
        .. Family switch
        {
            Fedora => (string[])["sudo", "dnf", "install", "-y"],
            Arch => ["sudo", "pacman", "-S", "--needed", "--noconfirm"],
            Suse => ["sudo", "zypper", "--non-interactive", "install"],
            _ => ["sudo", "apt-get", "install", "-y"],
        },
        .. packages.Distinct(),
    ];
}