using System.Net;

namespace VoicePromptTray;

internal sealed record RecognitionServerProbeResult(bool Success, string Message);

internal static class RecognitionServer
{
    public const int MinimumTimeoutSeconds = 5;
    public const int MaximumTimeoutSeconds = 600;

    public static string NormalizeUrl(string value)
    {
        string url = value.Trim();
        string? error = Validate(url, 60);
        if (error != null)
            throw new InvalidDataException(error);
        return url.TrimEnd('/');
    }

    public static string? Validate(string value, int timeoutSeconds)
    {
        string url = value.Trim();
        if (url.Length is 0 or > 2_048 ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return "Recognition server URL must be a valid HTTP or HTTPS base address without credentials, query, or fragment.";
        if (timeoutSeconds is < MinimumTimeoutSeconds or > MaximumTimeoutSeconds)
            return $"Recognition server wait must be {MinimumTimeoutSeconds}-{MaximumTimeoutSeconds} seconds.";
        return null;
    }

    public static bool IsLoopback(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri))
            return false;
        string host = uri.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
    }

    public static string PrivacyMessage(string value)
    {
        if (IsLoopback(value))
            return "Local server · audio stays on this PC.";
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri) && uri.Scheme == "https")
            return "Remote server · completed audio leaves this PC over HTTPS.";
        return "Warning · remote HTTP sends audio unencrypted.";
    }

    public static async Task<RecognitionServerProbeResult> ProbeAsync(
        string value,
        CancellationToken cancellationToken = default,
        HttpMessageHandler? handler = null)
    {
        string url;
        try
        {
            url = NormalizeUrl(value);
        }
        catch (InvalidDataException ex)
        {
            return new(false, ex.Message);
        }

        bool ownsHandler = handler == null;
        handler ??= new HttpClientHandler();
        using var client = new HttpClient(handler, ownsHandler)
        {
            Timeout = TimeSpan.FromSeconds(4),
        };
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url + "/health");
            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
                return new(true, $"Server ready · HTTP {(int)response.StatusCode}");
            return new(false, $"Server answered HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "Server check timed out after 4 seconds.");
        }
        catch (HttpRequestException ex)
        {
            return new(false, "Server not reachable · " + ShortMessage(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, "Server check failed · " + ShortMessage(ex.Message));
        }
    }

    private static string ShortMessage(string message)
    {
        string oneLine = message.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= 120 ? oneLine : oneLine[..117] + "…";
    }
}
