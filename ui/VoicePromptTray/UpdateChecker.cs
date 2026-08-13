using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VoicePromptTray;

internal enum UpdateState
{
    UpToDate,
    Available,
    Unavailable,
}

internal enum UpdateChannel
{
    Stable,
    Preview,
}

internal sealed record ReleaseVersion(Version Number, string Prerelease = "") : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    public string Display => Number.ToString(3) + (IsPrerelease ? "-" + Prerelease : "");

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
            return 1;
        int core = Number.CompareTo(other.Number);
        if (core != 0)
            return core;
        if (!IsPrerelease)
            return other.IsPrerelease ? 1 : 0;
        if (!other.IsPrerelease)
            return -1;

        string[] left = Prerelease.Split('.');
        string[] right = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            bool leftNumeric = int.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out int leftNumber);
            bool rightNumeric = int.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out int rightNumber);
            int part = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric
                    ? -1
                    : rightNumeric
                        ? 1
                        : string.Compare(left[i], right[i], StringComparison.Ordinal);
            if (part != 0)
                return part;
        }
        return left.Length.CompareTo(right.Length);
    }

    public override string ToString() => Display;
}

internal sealed record UpdateResult(
    UpdateState State,
    ReleaseVersion CurrentVersion,
    ReleaseVersion? LatestVersion = null,
    string ReleaseUrl = "",
    UpdatePackage? Package = null,
    string Error = "");

internal sealed record ReleaseAsset(
    string Name,
    Uri DownloadUrl,
    long Size,
    string Digest = "");

internal sealed record UpdatePackage(
    ReleaseVersion Version,
    ReleaseAsset Archive,
    ReleaseAsset Checksums);

internal sealed record ReleaseDescriptor(
    ReleaseVersion Version,
    UpdatePackage? Package);

internal sealed class UpdateChecker
{
    internal const string LatestReleaseEndpoint =
        "https://api.github.com/repos/seNkoKG/VoicePrompt/releases/latest";
    internal const string PreviewReleaseEndpoint =
        "https://api.github.com/repos/seNkoKG/VoicePrompt/releases?per_page=20";

    private readonly HttpClient _client;

    public UpdateChecker(HttpClient? client = null)
    {
        _client = client ?? new HttpClient();
    }

    public async Task<UpdateResult> CheckAsync(
        string currentVersionTag,
        UpdateChannel channel = UpdateChannel.Stable,
        CancellationToken cancellationToken = default)
    {
        ReleaseVersion currentVersion = ParseReleaseTag(currentVersionTag, allowPrerelease: true) ??
            new ReleaseVersion(new Version(0, 0, 0));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            string endpoint = channel == UpdateChannel.Preview
                ? PreviewReleaseEndpoint
                : LatestReleaseEndpoint;
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"VoicePrompt/{currentVersion.Display}");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                return Unavailable(currentVersion, $"GitHub returned {(int)response.StatusCode}.");

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using JsonDocument document = await ReadBoundedJsonAsync(stream, timeout.Token);
            ReleaseDescriptor? latest = channel == UpdateChannel.Preview
                ? FindLatestPreview(document.RootElement)
                : ReadLatestStable(document.RootElement);
            if (latest is null)
            {
                string kind = channel == UpdateChannel.Preview ? "release" : "stable release";
                return Unavailable(currentVersion, $"GitHub did not return a valid {kind} tag.");
            }

            ReleaseVersion latestVersion = latest.Version;
            string tag = "v" + latestVersion.Display;
            string releaseUrl = "https://github.com/seNkoKG/VoicePrompt/releases/tag/" +
                Uri.EscapeDataString(tag);
            return new UpdateResult(
                latestVersion.CompareTo(currentVersion) > 0 ? UpdateState.Available : UpdateState.UpToDate,
                currentVersion,
                latestVersion,
                releaseUrl,
                latest.Package);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(currentVersion, "The update check timed out.");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(currentVersion, "The update check could not reach GitHub.");
        }
    }

    internal static Version? ParseVersionTag(string? tag)
    {
        ReleaseVersion? release = ParseReleaseTag(tag, allowPrerelease: false);
        return release?.Number;
    }

    internal static ReleaseVersion? ParseReleaseTag(string? tag, bool allowPrerelease)
    {
        string value = tag?.Trim() ?? "";
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        string[] versionParts = value.Split('-', 2);
        string[] core = versionParts[0].Split('.');
        if (core.Length != 3 ||
            !int.TryParse(core[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(core[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(core[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
            return null;

        string prerelease = versionParts.Length == 2 ? versionParts[1] : "";
        if (prerelease.Length > 0)
        {
            if (!allowPrerelease || prerelease.Split('.').Any(identifier =>
                    identifier.Length == 0 ||
                    identifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
                    (identifier.Length > 1 && identifier[0] == '0' && identifier.All(char.IsDigit))))
                return null;
        }
        else if (versionParts.Length == 2)
        {
            return null;
        }

        return new ReleaseVersion(new Version(major, minor, patch), prerelease);
    }

    private static ReleaseDescriptor? ReadLatestStable(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            IsTrue(root, "draft") || IsTrue(root, "prerelease") ||
            !root.TryGetProperty("tag_name", out JsonElement tagElement))
            return null;
        ReleaseVersion? version = ParseReleaseTag(tagElement.GetString(), allowPrerelease: false);
        return version is null ? null : new ReleaseDescriptor(version, ReadPackage(root, version));
    }

    private static ReleaseDescriptor? FindLatestPreview(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        ReleaseDescriptor? latest = null;
        foreach (JsonElement release in root.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || IsTrue(release, "draft") ||
                !release.TryGetProperty("tag_name", out JsonElement tagElement))
                continue;
            ReleaseVersion? candidate = ParseReleaseTag(tagElement.GetString(), allowPrerelease: true);
            if (candidate is not null && (latest is null || candidate.CompareTo(latest.Version) > 0))
                latest = new ReleaseDescriptor(candidate, ReadPackage(release, candidate));
        }
        return latest;
    }

    private static UpdatePackage? ReadPackage(JsonElement release, ReleaseVersion version)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array)
            return null;

        string tag = "v" + version.Display;
        string archiveName = $"VoicePrompt-{tag}-windows-x64.zip";
        string checksumName = $"VoicePrompt-{tag}-SHA256SUMS.txt";
        ReleaseAsset? archive = null;
        ReleaseAsset? checksums = null;
        bool duplicateArchive = false;
        bool duplicateChecksums = false;
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !asset.TryGetProperty("name", out JsonElement nameElement) ||
                !asset.TryGetProperty("size", out JsonElement sizeElement) ||
                !sizeElement.TryGetInt64(out long size) || size <= 0 ||
                (asset.TryGetProperty("state", out JsonElement stateElement) &&
                    !string.Equals(stateElement.GetString(), "uploaded", StringComparison.Ordinal)))
                continue;

            string name = nameElement.GetString() ?? "";
            if (name != archiveName && name != checksumName)
                continue;

            string expectedUrl = "https://github.com/seNkoKG/VoicePrompt/releases/download/" +
                Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);
            if (!asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ||
                !string.Equals(urlElement.GetString(), expectedUrl, StringComparison.Ordinal))
                continue;

            string digest = asset.TryGetProperty("digest", out JsonElement digestElement)
                ? digestElement.GetString() ?? ""
                : "";
            var parsed = new ReleaseAsset(name, new Uri(expectedUrl), size, digest);
            if (name == archiveName)
            {
                duplicateArchive |= archive is not null;
                archive ??= parsed;
            }
            else
            {
                duplicateChecksums |= checksums is not null;
                checksums ??= parsed;
            }
        }

        return archive is not null && checksums is not null &&
            !duplicateArchive && !duplicateChecksums
            ? new UpdatePackage(version, archive, checksums)
            : null;
    }

    private static bool IsTrue(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static async Task<JsonDocument> ReadBoundedJsonAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        const int maximumBytes = 512 * 1024;
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            if (buffer.Length + read > maximumBytes)
                throw new InvalidDataException("The release response was unexpectedly large.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
    }

    private static UpdateResult Unavailable(ReleaseVersion currentVersion, string error) =>
        new(UpdateState.Unavailable, currentVersion, Error: error);
}
