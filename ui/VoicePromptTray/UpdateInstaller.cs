using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VoicePromptTray;

internal sealed record UpdateProgress(string Message, int? Percentage = null);

internal sealed record StagedUpdate(
    ReleaseVersion Version,
    string Directory,
    string InstallerPath);

internal sealed class UpdateInstaller
{
    internal const long MaximumArchiveBytes = 256L * 1024 * 1024;
    internal const long MaximumExtractedBytes = 512L * 1024 * 1024;
    internal const int MaximumEntries = 512;
    private const int MaximumChecksumBytes = 256 * 1024;

    private static readonly string[] RequiredFiles =
    {
        "VoicePromptTray.exe",
        "install.ps1",
        "version.txt",
        "requirements.txt",
        "run_daemon.pyw",
        "LICENSE.txt",
        "THIRD_PARTY_NOTICES.txt",
        "PRIVACY.md",
        "TERMS.md",
        "scripts/apply_patches.ps1",
        "scripts/shortcut_manager.ps1",
        "scripts/runtime_meter.py",
        "scripts/ai_rewriter.py",
        "scripts/transcript_history.py",
        "scripts/text_corrections.py",
        "scripts/slang_retry.py",
        "scripts/decoding_options.py",
        "scripts/buffered_transcription.py",
        "scripts/output_mode.py",
        "scripts/app_profiles.py",
        "scripts/text_snippets.py",
        "scripts/voice_commands.py",
        "scripts/smart_formatter.py",
        "scripts/windows_context.py",
        "scripts/selection_commands.py",
        "scripts/windows_hotkey.py",
    };

    private static readonly Regex ChecksumLine = new(
        "^(?<hash>[0-9a-fA-F]{64})[ \\t]+\\*?(?<name>[^\\r\\n]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, byte> ActiveStages =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient _client;

    public UpdateInstaller(HttpClient? client = null)
    {
        _client = client ?? new HttpClient();
    }

    public async Task<StagedUpdate> PrepareAsync(
        UpdatePackage package,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidatePackage(package);
        ValidateAsset(package.Archive, MaximumArchiveBytes, ".zip");
        ValidateAsset(package.Checksums, MaximumChecksumBytes, "SHA256SUMS.txt");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        string stageRoot = Path.Combine(
            Path.GetTempPath(),
            $"VoicePrompt-Update-v{package.Version.Display}-{Guid.NewGuid():N}");
        string downloadRoot = Path.Combine(stageRoot, "download");
        string extractRoot = Path.Combine(stageRoot, "package");
        Directory.CreateDirectory(downloadRoot);
        File.WriteAllText(Path.Combine(stageRoot, ".voiceprompt-update-stage"), "1", Encoding.ASCII);
        ActiveStages.TryAdd(Path.GetFullPath(stageRoot), 0);

        try
        {
            progress?.Report(new UpdateProgress("Downloading verification data…", 2));
            string checksumPath = Path.Combine(downloadRoot, package.Checksums.Name);
            await DownloadAsync(
                package.Checksums,
                checksumPath,
                MaximumChecksumBytes,
                null,
                timeout.Token);
            await ValidateDownloadedDigestAsync(
                checksumPath,
                package.Checksums.Digest,
                "checksum file",
                timeout.Token);

            string checksumText = await ReadBoundedTextAsync(
                checksumPath,
                MaximumChecksumBytes,
                timeout.Token);
            string expectedHash = ReadChecksum(checksumText, package.Archive.Name);
            ValidateApiDigest(package.Archive.Digest, expectedHash);

            progress?.Report(new UpdateProgress("Downloading update…", 5));
            string archivePath = Path.Combine(downloadRoot, package.Archive.Name);
            var archiveProgress = new Progress<int>(percentage =>
                progress?.Report(new UpdateProgress("Downloading update…", 5 + (percentage * 65 / 100))));
            await DownloadAsync(
                package.Archive,
                archivePath,
                MaximumArchiveBytes,
                archiveProgress,
                timeout.Token);

            progress?.Report(new UpdateProgress("Verifying SHA-256 checksum…", 74));
            string actualHash = await ComputeSha256Async(archivePath, timeout.Token);
            if (!HashesMatch(expectedHash, actualHash))
                throw new InvalidDataException("The downloaded update failed its SHA-256 verification.");

            progress?.Report(new UpdateProgress("Preparing the verified installer…", 82));
            ExtractVerifiedArchive(archivePath, extractRoot, package.Version);
            progress?.Report(new UpdateProgress("Update is ready to install.", 100));
            return new StagedUpdate(
                package.Version,
                stageRoot,
                Path.Combine(extractRoot, "install.ps1"));
        }
        catch
        {
            TryDeleteDirectory(stageRoot);
            throw;
        }
        finally
        {
            ActiveStages.TryRemove(Path.GetFullPath(stageRoot), out _);
        }
    }

    public static Process Launch(StagedUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!File.Exists(update.InstallerPath))
            throw new FileNotFoundException("The verified update installer is missing.", update.InstallerPath);

        string? packageRoot = Path.GetDirectoryName(update.InstallerPath);
        if (string.IsNullOrWhiteSpace(packageRoot))
            throw new InvalidDataException("The verified update directory is invalid.");

        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException("Windows PowerShell could not be found.", powershell);

        var start = new ProcessStartInfo
        {
            FileName = powershell,
            WorkingDirectory = packageRoot,
            UseShellExecute = true,
        };
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(update.InstallerPath);
        return Process.Start(start) ?? throw new InvalidOperationException("The update installer did not start.");
    }

    internal static void CleanupStagedUpdates(
        string? temporaryRoot = null,
        TimeSpan? minimumAge = null)
    {
        string root = Path.GetFullPath(temporaryRoot ?? Path.GetTempPath()).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
            return;
        string rootPrefix = root + Path.DirectorySeparatorChar;
        TimeSpan age = minimumAge ?? TimeSpan.FromMinutes(1);
        DateTime threshold = DateTime.UtcNow - age;

        foreach (string candidate in Directory.EnumerateDirectories(
            root,
            "VoicePrompt-Update-v*",
            SearchOption.TopDirectoryOnly))
        {
            try
            {
                string resolved = Path.GetFullPath(candidate);
                string marker = Path.Combine(resolved, ".voiceprompt-update-stage");
                if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
                    ActiveStages.ContainsKey(resolved) ||
                    !File.Exists(marker) ||
                    File.GetLastWriteTimeUtc(marker) > threshold ||
                    File.ReadAllText(marker, Encoding.ASCII).Trim() != "1")
                    continue;
                TryDeleteDirectory(resolved);
            }
            catch
            {
            }
        }
    }

    internal static string ReadChecksum(string content, string fileName)
    {
        string? match = null;
        foreach (string line in content.Replace("\r", "").Split('\n'))
        {
            Match parsed = ChecksumLine.Match(line.Trim());
            if (!parsed.Success || !string.Equals(
                    parsed.Groups["name"].Value,
                    fileName,
                    StringComparison.Ordinal))
                continue;
            if (match is not null)
                throw new InvalidDataException("The checksum file contains duplicate update entries.");
            match = parsed.Groups["hash"].Value.ToLowerInvariant();
        }

        return match ?? throw new InvalidDataException(
            "The checksum file does not contain the selected update package.");
    }

    internal static void ExtractVerifiedArchive(
        string archivePath,
        string destination,
        ReleaseVersion version)
    {
        if (Directory.Exists(destination))
            throw new IOException("The update staging directory already exists.");
        Directory.CreateDirectory(destination);
        string destinationPrefix = Path.GetFullPath(destination).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredBytes = 0;
        long extractedBytes = 0;
        int entryCount = 0;

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                entryCount++;
                if (entryCount > MaximumEntries)
                    throw new InvalidDataException("The update archive contains too many files.");
                if (IsLinkOrReparsePoint(entry))
                    throw new InvalidDataException("The update archive contains an unsupported link.");

                declaredBytes = checked(declaredBytes + entry.Length);
                if (entry.Length > MaximumArchiveBytes || declaredBytes > MaximumExtractedBytes)
                    throw new InvalidDataException("The update archive is unexpectedly large.");

                string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase) ||
                    !extractedPaths.Add(target))
                    throw new InvalidDataException("The update archive contains an unsafe or duplicate path.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using Stream input = entry.Open();
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] buffer = new byte[81920];
                long entryBytes = 0;
                while (true)
                {
                    int read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    entryBytes = checked(entryBytes + read);
                    extractedBytes = checked(extractedBytes + read);
                    if (entryBytes > entry.Length || extractedBytes > MaximumExtractedBytes)
                        throw new InvalidDataException("The update archive expanded beyond its declared size.");
                    output.Write(buffer, 0, read);
                }
                if (entryBytes != entry.Length)
                    throw new InvalidDataException("The update archive contains an incomplete file.");
            }

            foreach (string required in RequiredFiles)
            {
                string requiredPath = Path.Combine(
                    destination,
                    required.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(requiredPath))
                    throw new InvalidDataException($"The update package is incomplete: {required}");
            }

            string packagedVersion = File.ReadAllText(Path.Combine(destination, "version.txt"), Encoding.UTF8).Trim();
            if (!string.Equals(packagedVersion, version.Display, StringComparison.Ordinal))
                throw new InvalidDataException("The update package version does not match its release.");
        }
        catch
        {
            TryDeleteDirectory(destination);
            throw;
        }
    }

    private async Task DownloadAsync(
        ReleaseAsset asset,
        string destination,
        long maximumBytes,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        request.Headers.UserAgent.ParseAdd("VoicePrompt-Updater/1.0");
        using HttpResponseMessage response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"GitHub returned {(int)response.StatusCode} for {asset.Name}.");
        if (response.Content.Headers.ContentLength is { } contentLength &&
            (contentLength <= 0 || contentLength > maximumBytes || contentLength != asset.Size))
            throw new InvalidDataException("The update download size does not match its release metadata.");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            written = checked(written + read);
            if (written > maximumBytes || written > asset.Size)
                throw new InvalidDataException("The update download exceeded its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            progress?.Report((int)Math.Min(100, written * 100 / asset.Size));
        }

        if (written != asset.Size)
            throw new InvalidDataException("The update download was incomplete.");
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadBoundedTextAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > maximumBytes)
            throw new InvalidDataException("The checksum file is unexpectedly large.");
        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task ValidateDownloadedDigestAsync(
        string path,
        string digest,
        string description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return;
        const string prefix = "sha256:";
        string actual = await ComputeSha256Async(path, cancellationToken);
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !HashesMatch(digest[prefix.Length..], actual))
            throw new InvalidDataException($"GitHub's {description} digest is invalid.");
    }

    private static bool HashesMatch(string expected, string actual)
    {
        try
        {
            byte[] expectedBytes = Convert.FromHexString(expected);
            byte[] actualBytes = Convert.FromHexString(actual);
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidateApiDigest(string digest, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return;
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !HashesMatch(digest[prefix.Length..], expectedHash))
            throw new InvalidDataException("GitHub's asset digest does not match the published checksum.");
    }

    private static void ValidateAsset(ReleaseAsset asset, long maximumBytes, string expectedSuffix)
    {
        if (asset.Size <= 0 || asset.Size > maximumBytes ||
            asset.DownloadUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(asset.DownloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !asset.Name.EndsWith(expectedSuffix, StringComparison.Ordinal))
            throw new InvalidDataException("The release contains invalid update metadata.");
    }

    private static void ValidatePackage(UpdatePackage package)
    {
        string tag = "v" + package.Version.Display;
        string archiveName = $"VoicePrompt-{tag}-windows-x64.zip";
        string checksumName = $"VoicePrompt-{tag}-SHA256SUMS.txt";
        string root = "https://github.com/seNkoKG/VoicePrompt/releases/download/" +
            Uri.EscapeDataString(tag) + "/";
        if (package.Archive.Name != archiveName || package.Checksums.Name != checksumName ||
            package.Archive.DownloadUrl.AbsoluteUri != root + Uri.EscapeDataString(archiveName) ||
            package.Checksums.DownloadUrl.AbsoluteUri != root + Uri.EscapeDataString(checksumName))
            throw new InvalidDataException("The update package does not match its release tag.");
    }

    private static bool IsLinkOrReparsePoint(ZipArchiveEntry entry)
    {
        int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
        bool unixLink = unixType == 0xA000;
        bool windowsReparse = (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
        return unixLink || windowsReparse;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return;
            var directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return;
            Directory.Delete(directory.FullName, recursive: true);
        }
        catch
        {
        }
    }
}
