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

internal sealed record UpdateResult(
    UpdateState State,
    Version CurrentVersion,
    Version? LatestVersion = null,
    string ReleaseUrl = "",
    string Error = "");

internal sealed class UpdateChecker
{
    internal const string LatestReleaseEndpoint =
        "https://api.github.com/repos/seNkoKG/VoicePrompt/releases/latest";

    private readonly HttpClient _client;

    public UpdateChecker(HttpClient? client = null)
    {
        _client = client ?? new HttpClient();
    }

    public async Task<UpdateResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"VoicePrompt/{currentVersion.ToString(3)}");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK)
                return Unavailable(currentVersion, $"GitHub returned {(int)response.StatusCode}.");

            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using JsonDocument document = await ReadBoundedJsonAsync(stream, timeout.Token);
            if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tagElement))
                return Unavailable(currentVersion, "The release response did not contain a version tag.");

            string tag = tagElement.GetString() ?? "";
            Version? latestVersion = ParseVersionTag(tag);
            if (latestVersion is null)
                return Unavailable(currentVersion, "The latest release tag was not a valid stable version.");

            string releaseUrl = "https://github.com/seNkoKG/VoicePrompt/releases/tag/" +
                Uri.EscapeDataString(tag);
            return new UpdateResult(
                latestVersion > Normalize(currentVersion) ? UpdateState.Available : UpdateState.UpToDate,
                Normalize(currentVersion),
                latestVersion,
                releaseUrl);
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
        string value = tag?.Trim() ?? "";
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];
        if (value.Contains('-', StringComparison.Ordinal) ||
            !Version.TryParse(value, out Version? version) ||
            version.Major < 0 || version.Minor < 0 || version.Build < 0)
            return null;
        return Normalize(version);
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

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

    private static UpdateResult Unavailable(Version currentVersion, string error) =>
        new(UpdateState.Unavailable, Normalize(currentVersion), Error: error);
}
