# Installs Echo for the current user, without administrator rights:
#
#   irm https://githubphobos.github.io/Echo/install.ps1 | iex
#
# A version can be given, which is also how an earlier version is put back:
#
#   & ([scriptblock]::Create((irm https://githubphobos.github.io/Echo/install.ps1))) 2.0.1
#
# Every version goes into a folder of its own, %LOCALAPPDATA%\Programs\echooo\versions\<version>, and the junction
# %LOCALAPPDATA%\Programs\echooo\current leads to the one in use. The user's PATH holds that junction once and for
# all. Echo keeps the same layout when it updates itself. ECHOOO_RELEASES points the script at other releases, such
# as a test server.

param([Parameter(Position = 0)][string]$Version)

function Install-Echo([string]$Version) {
    $ErrorActionPreference = 'Stop'

    # Windows PowerShell draws its progress bar so slowly that it holds a large download back
    $ProgressPreference = 'SilentlyContinue'

    $releases = if ($env:ECHOOO_RELEASES) { $env:ECHOOO_RELEASES.TrimEnd('/') } else { 'https://github.com/GithubPhobos/Echo/releases' }
    $archive = 'echooo-win-x64.zip'
    $root = Join-Path $env:LOCALAPPDATA 'Programs\echooo'
    $versions = Join-Path $root 'versions'
    $current = Join-Path $root 'current'

    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'Echo runs on 64-bit Windows only.'
    }

    # Windows PowerShell 5.1 does not offer TLS 1.2 by itself, and GitHub accepts nothing older
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $Version = "$Version".Trim().TrimStart('v', 'V')

    # The latest release is where GitHub redirects ".../releases/latest" to: ".../releases/tag/v2.0.1"
    if (-not $Version) {
        try {
            $request = [Net.HttpWebRequest]::Create("$releases/latest")
            $request.Method = 'HEAD'
            $request.AllowAutoRedirect = $false
            $response = $request.GetResponse()
            $location = $response.Headers['Location']
            $response.Close()
        }
        catch {
            throw "GitHub cannot be reached: $($_.Exception.Message)"
        }

        if ("$location" -notmatch '/releases/tag/v([^/]+)$') {
            throw 'there is no release of Echo yet.'
        }

        $Version = $Matches[1]
    }

    $download = "$releases/download/v$Version"
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('echooo-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary | Out-Null

    try {
        Write-Host "Downloading Echo $Version..."

        try {
            Invoke-WebRequest -UseBasicParsing "$download/$archive" -OutFile (Join-Path $temporary $archive)
            Invoke-WebRequest -UseBasicParsing "$download/SHA256SUMS" -OutFile (Join-Path $temporary 'SHA256SUMS')
        }
        catch {
            throw "Echo $Version could not be downloaded. Is there such a version? See $releases"
        }

        # Only the line of this archive matters: the list names the archives of every system
        $pattern = "^([0-9a-fA-F]{64}) [ *]$([regex]::Escape($archive))$"
        $line = Get-Content (Join-Path $temporary 'SHA256SUMS') | Where-Object { $_ -match $pattern } | Select-Object -First 1

        if (-not ($line -match $pattern)) {
            throw "the checksums of Echo $Version do not name $archive."
        }

        $expected = $Matches[1]

        # Counted by .NET itself: a Windows PowerShell started from PowerShell 7 may not find Get-FileHash
        $stream = [IO.File]::OpenRead((Join-Path $temporary $archive))

        try {
            $actual = -join ([Security.Cryptography.SHA256]::Create().ComputeHash($stream) | ForEach-Object { $_.ToString('x2') })
        }
        finally {
            $stream.Close()
        }

        if ($actual -ne $expected) {
            throw 'the downloaded archive is damaged (its checksum does not match). Try again.'
        }

        # Unpacked next to its place and moved there whole, so the folder of a version is never half full
        $target = Join-Path $versions $Version
        $partial = "$target.part"

        New-Item -ItemType Directory -Force -Path $versions | Out-Null

        if (Test-Path -LiteralPath $partial) {
            Remove-Item -LiteralPath $partial -Recurse -Force
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $temporary $archive), $partial)

        if (-not (Test-Path -LiteralPath (Join-Path $partial 'echooo.exe'))) {
            throw "the archive of Echo $Version has no echooo.exe."
        }

        if (Test-Path -LiteralPath $target) {
            try {
                Remove-Item -LiteralPath $target -Recurse -Force
            }
            catch {
                throw "Echo $Version is running from $target. Close it and run this again."
            }
        }

        Move-Item -LiteralPath $partial -Destination $target

        # Only the junction itself is taken away: removing it with its contents would delete the version it leads to
        $link = Get-Item -LiteralPath $current -Force -ErrorAction SilentlyContinue

        if ($link) {
            if (-not ($link.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "$current is a folder, not a link, so it is left as it is."
            }

            [IO.Directory]::Delete($current)
        }

        New-Item -ItemType Junction -Path $current -Target $target | Out-Null

        Add-ToPath $current

        Write-Host "Echo $Version is installed. Start it with: echooo" -ForegroundColor Green
    }
    finally {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Puts the folder on the user's PATH once, and on the PATH of this window. The user's PATH is read and written
# without expanding names such as %USERPROFILE% in it, so they stay what they are
function Add-ToPath([string]$folder) {
    $environment = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)

    try {
        $path = [string]$environment.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $entries = @($path.Split(';') | Where-Object { $_ })

        if ($entries -notcontains $folder) {
            $kind = if ($path) { $environment.GetValueKind('Path') } else { [Microsoft.Win32.RegistryValueKind]::ExpandString }
            $environment.SetValue('Path', (($entries + $folder) -join ';'), $kind)

            # Tells the running programs, such as the taskbar, that the environment changed, so new terminals see it
            if (-not ('EchoInstall.Native' -as [type])) {
                Add-Type -Namespace EchoInstall -Name Native -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
'@
            }

            $result = [UIntPtr]::Zero
            [EchoInstall.Native]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]$result) | Out-Null
        }
    }
    finally {
        $environment.Close()
    }

    if (@($env:Path.Split(';')) -notcontains $folder) {
        $env:Path = "$env:Path;$folder"
    }
}

# A failure is told without closing the window: "exit" would end the PowerShell the script runs in
try {
    Install-Echo $Version
}
catch {
    Write-Host "Echo was not installed: $($_.Exception.Message)" -ForegroundColor Red
}
