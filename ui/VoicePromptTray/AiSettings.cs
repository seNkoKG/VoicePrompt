using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace VoicePromptTray;

internal sealed class AiSettings
{
    public string Mode { get; set; } = "off";
    public string Endpoint { get; set; } = "http://127.0.0.1:11434/v1/chat/completions";
    public string Model { get; set; } = "qwen2.5:3b";
    public int TimeoutMs { get; set; } = 900;
    public string ApiKeyProtected { get; set; } = "";
}

internal static class AiSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoicePrompt AI v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        ref DataBlob entropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static AiSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AiSettings();
        try
        {
            return Normalize(JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), JsonOptions));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AiSettings();
        }
    }

    private static AiSettings Normalize(AiSettings? settings)
    {
        var defaults = new AiSettings();
        settings ??= defaults;
        settings.Mode = settings.Mode?.Trim().ToLowerInvariant() ?? "off";
        if (settings.Mode is not ("off" or "grammar" or "prompt"))
            settings.Mode = "off";
        settings.Endpoint = string.IsNullOrWhiteSpace(settings.Endpoint) ? defaults.Endpoint : settings.Endpoint.Trim();
        settings.Model = string.IsNullOrWhiteSpace(settings.Model) ? defaults.Model : settings.Model.Trim();
        settings.TimeoutMs = Math.Clamp(settings.TimeoutMs, 400, 3000);
        settings.ApiKeyProtected ??= "";
        return settings;
    }

    public static void Save(string path, AiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, true);
    }

    public static string? Validate(AiSettings settings)
    {
        if (settings.Mode is not ("off" or "grammar" or "prompt"))
            return "Choose Off, Grammar, or Prompt mode.";
        if (settings.Mode == "off")
            return null;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
            return "AI endpoint must be a complete HTTP or HTTPS URL.";
        if (string.IsNullOrWhiteSpace(settings.Model))
            return "Enter the AI model name exposed by your provider.";
        if (settings.TimeoutMs is < 400 or > 3000)
            return "AI maximum wait must be between 400 and 3000 ms.";
        return null;
    }

    public static string ProtectApiKey(string apiKey)
    {
        byte[] plain = Encoding.UTF8.GetBytes(apiKey);
        try
        {
            return Convert.ToBase64String(Protect(plain));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static string UnprotectApiKey(string protectedApiKey)
    {
        byte[] plain = Unprotect(Convert.FromBase64String(protectedApiKey));
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] Protect(byte[] plain)
    {
        DataBlob input = Allocate(plain);
        DataBlob entropy = Allocate(Entropy);
        try
        {
            if (!CryptProtectData(ref input, "VoicePrompt", ref entropy, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return CopyAndFree(output);
        }
        finally
        {
            ZeroAndFree(input);
            ZeroAndFree(entropy);
        }
    }

    private static byte[] Unprotect(byte[] cipher)
    {
        DataBlob input = Allocate(cipher);
        DataBlob entropy = Allocate(Entropy);
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, 0, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return CopyAndFree(output);
        }
        finally
        {
            ZeroAndFree(input);
            ZeroAndFree(entropy);
        }
    }

    private static DataBlob Allocate(byte[] data)
    {
        IntPtr pointer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, pointer, data.Length);
        return new DataBlob { Size = data.Length, Data = pointer };
    }

    private static byte[] CopyAndFree(DataBlob blob)
    {
        try
        {
            var result = new byte[blob.Size];
            Marshal.Copy(blob.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (blob.Data != IntPtr.Zero)
            {
                if (blob.Size > 0)
                    Marshal.Copy(new byte[blob.Size], 0, blob.Data, blob.Size);
                LocalFree(blob.Data);
            }
        }
    }

    private static void ZeroAndFree(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        if (blob.Size > 0)
            Marshal.Copy(new byte[blob.Size], 0, blob.Data, blob.Size);
        Marshal.FreeHGlobal(blob.Data);
    }
}
