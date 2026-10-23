using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Somning.Core.Cloud;

/// <summary>
/// Uploads a WAV recording to Groq or OpenAI with a multipart form (file, model, language <c>pt</c>,
/// JSON response and optional vocabulary prompt) and maps every failure to an explicit
/// <see cref="CloudErrorCodes"/> value so the caller can show a pt-BR notice and retry (STT-05, STT-06).
/// The API key only ever travels in the <c>Authorization</c> header and is redacted from messages (APP-06).
/// </summary>
public sealed class CloudTranscriptionClient : ICloudTranscriptionClient
{
    public const string PortugueseLanguage = "pt";
    public const string JsonResponseFormat = "json";

    private const int UploadBufferSize = 81_920;
    private const int MaxErrorDetailChars = 300;
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromSeconds(90) };

    private readonly HttpClient httpClient;

    public CloudTranscriptionClient(HttpClient? httpClient = null) =>
        this.httpClient = httpClient ?? DefaultHttpClient;

    public async Task<CloudTranscriptionResult> TranscribeAsync(
        string wavPath,
        CloudEngineConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);
        ArgumentNullException.ThrowIfNull(config);

        // A misconfigured router reaching the client is a programming error, but a blank key is a
        // user-facing failure that must classify like any other rejected key (STT-06).
        if (!CloudProvider.IsCloud(config.Provider))
            throw new ArgumentException(
                $"Provider '{config.Provider}' does not support cloud transcription.",
                nameof(config));
        if (!config.HasApiKey)
            throw new CloudTranscriptionException(
                CloudErrorCodes.InvalidApiKey,
                "A cloud API key is required.");

        if (!File.Exists(wavPath))
            throw new FileNotFoundException("The recording to transcribe was not found.", wavPath);

        var model = config.ResolveModel();
        var endpoint = config.ResolveEndpoint();
        var durationMs = ReadWavDurationMs(wavPath);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = BuildRequest(wavPath, config, model, endpoint);
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
                throw MapFailure(response.StatusCode, body, config);

            var text = ParseText(body, response.StatusCode);
            return new CloudTranscriptionResult(text, durationMs, stopwatch.ElapsedMilliseconds, model);
        }
        catch (CloudTranscriptionException)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new CloudTranscriptionException(
                CloudErrorCodes.Timeout,
                "Cloud transcription timed out.",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new CloudTranscriptionException(
                CloudErrorCodes.NoNetwork,
                "Cloud transcription provider is unreachable.",
                innerException: exception);
        }
    }

    private static HttpRequestMessage BuildRequest(
        string wavPath,
        CloudEngineConfig config,
        string model,
        Uri endpoint)
    {
        var audio = new StreamContent(new FileStream(
            wavPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            UploadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan));
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        var form = new MultipartFormDataContent();
        form.Add(audio, "file", Path.GetFileName(wavPath));
        form.Add(new StringContent(model, Encoding.UTF8), "model");
        form.Add(new StringContent(PortugueseLanguage, Encoding.UTF8), "language");
        form.Add(new StringContent(JsonResponseFormat, Encoding.UTF8), "response_format");
        if (config.CustomVocabulary is { Length: > 0 } vocabulary)
            form.Add(new StringContent(vocabulary, Encoding.UTF8), "prompt");

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static CloudTranscriptionException MapFailure(
        HttpStatusCode status,
        string body,
        CloudEngineConfig config)
    {
        var code = (int)status;
        var errorCode = code switch
        {
            401 => CloudErrorCodes.InvalidApiKey,
            429 => CloudErrorCodes.RateLimit,
            408 => CloudErrorCodes.Timeout,
            400 => CloudErrorCodes.BadRequest,
            >= 500 => CloudErrorCodes.ServerError,
            _ => CloudErrorCodes.BadRequest
        };

        var details = DescribeBody(body, config);
        return new CloudTranscriptionException(
            errorCode,
            $"Cloud transcription failed with HTTP {code} ({errorCode}).{details}",
            code);
    }

    private static string ParseText(string body, HttpStatusCode status)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                return text.GetString() ?? string.Empty;
            }
        }
        catch (JsonException exception)
        {
            throw new CloudTranscriptionException(
                CloudErrorCodes.BadRequest,
                "Cloud provider returned an invalid JSON payload.",
                (int)status,
                exception);
        }

        throw new CloudTranscriptionException(
            CloudErrorCodes.BadRequest,
            "Cloud provider returned a payload without a text field.",
            (int)status);
    }

    /// <summary>One-line, length-bounded and key-redacted error detail for exception messages.</summary>
    private static string DescribeBody(string body, CloudEngineConfig config)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;

        var redacted = ApiKeyMasker.Redact(body, config.ApiKey);
        var singleLine = string.Join(' ', redacted.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (singleLine.Length > MaxErrorDetailChars)
            singleLine = singleLine[..MaxErrorDetailChars] + "...";

        return $" Details: {singleLine}";
    }

    /// <summary>PCM WAV duration from the <c>fmt </c> byte rate and the <c>data</c> chunk size.</summary>
    private static long ReadWavDurationMs(string wavPath)
    {
        try
        {
            using var stream = new FileStream(wavPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (stream.Length < 12) return 0;

            if (ReadChunkId(reader) != "RIFF") return 0;
            reader.ReadInt32();
            if (ReadChunkId(reader) != "WAVE") return 0;

            var byteRate = 0;
            while (stream.Position + 8 <= stream.Length)
            {
                var id = ReadChunkId(reader);
                var size = reader.ReadInt32();
                if (size < 0) return 0;

                if (id == "fmt ")
                {
                    reader.ReadInt16(); // audio format
                    reader.ReadInt16(); // channel count
                    reader.ReadInt32(); // sample rate
                    byteRate = reader.ReadInt32();
                    Skip(stream, size - 12);
                }
                else if (id == "data")
                {
                    if (byteRate <= 0) return 0;
                    var dataSize = size;
                    if (dataSize <= 0 || stream.Position + dataSize > stream.Length)
                        dataSize = (int)Math.Min(int.MaxValue, stream.Length - stream.Position);
                    return (long)dataSize * 1000L / byteRate;
                }
                else
                {
                    Skip(stream, size);
                }
            }

            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        static void Skip(Stream stream, int bytes)
        {
            if (bytes > 0) stream.Seek(bytes, SeekOrigin.Current);
        }
    }

    private static string ReadChunkId(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return bytes.Length == 4 ? Encoding.ASCII.GetString(bytes) : string.Empty;
    }
}
