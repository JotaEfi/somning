using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Somning.Core.Cloud;
using Xunit;

namespace Somning.Core.Tests.Cloud;

public sealed class CloudTranscriptionClientTests
{
    private const string ApiKey = "gsk_live_1234567890abcdef";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- Request building ---------------------------------------------------------------------------------

    [Fact]
    public async Task Groq_request_uploads_a_multipart_wav_with_pt_language_and_turbo_model()
    {
        using var wav = new WavFile(seconds: 1);
        var handler = new CapturingHandler(_ => Ok("""{"text":"hoje é sexta-feira"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));
        var config = new CloudEngineConfig(CloudProvider.Groq, ApiKey);

        var result = await client.TranscribeAsync(wav.Path, config, Token);

        var request = Assert.IsType<CapturedRequest>(handler.Captured);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            new Uri("https://api.groq.com/openai/v1/audio/transcriptions"),
            request.Uri);
        Assert.Equal($"Bearer {ApiKey}", request.Authorization);
        Assert.Equal("whisper-large-v3-turbo", request.Fields["model"]);
        Assert.Equal("pt", request.Fields["language"]);
        Assert.Equal("json", request.Fields["response_format"]);
        Assert.Equal(Path.GetFileName(wav.Path), request.FileName);
        Assert.Equal(wav.Length, request.FileLength);
        Assert.False(request.Fields.ContainsKey("prompt"));

        Assert.Equal("hoje é sexta-feira", result.Text);
        Assert.Equal("whisper-large-v3-turbo", result.Model);
        Assert.Equal(1_000, result.DurationMs);
        Assert.True(result.InferMs >= 0);
    }

    [Fact]
    public async Task OpenAi_request_targets_the_openai_endpoint_with_whisper_1()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":"bom dia"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));
        var config = new CloudEngineConfig(CloudProvider.OpenAi, "sk-1234567890abcdef");

        var result = await client.TranscribeAsync(wav.Path, config, Token);

        var request = Assert.IsType<CapturedRequest>(handler.Captured);
        Assert.Equal(new Uri("https://api.openai.com/v1/audio/transcriptions"), request.Uri);
        Assert.Equal("whisper-1", request.Fields["model"]);
        Assert.Equal("pt", request.Fields["language"]);
        Assert.Equal("whisper-1", result.Model);
        Assert.Equal("bom dia", result.Text);
    }

    [Fact]
    public async Task Vocabulary_is_sent_as_the_prompt_field()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":"Somning"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));
        var config = new CloudEngineConfig(CloudProvider.Groq, ApiKey, "Somning, Groq, whisper");

        await client.TranscribeAsync(wav.Path, config, Token);

        Assert.Equal("Somning, Groq, whisper", handler.Captured!.Fields["prompt"]);
    }

    [Fact]
    public async Task Custom_endpoint_overrides_the_provider_default()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":"ok"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));
        var config = new CloudEngineConfig(
            CloudProvider.Groq,
            ApiKey,
            customEndpoint: "http://127.0.0.1:5055/v1/audio/transcriptions");

        await client.TranscribeAsync(wav.Path, config, Token);

        Assert.Equal(
            new Uri("http://127.0.0.1:5055/v1/audio/transcriptions"),
            handler.Captured!.Uri);
    }

    [Fact]
    public async Task Response_without_text_yields_an_empty_transcript()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":""}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var result = await client.TranscribeAsync(
            wav.Path,
            new CloudEngineConfig(CloudProvider.Groq, ApiKey),
            Token);

        Assert.Equal(string.Empty, result.Text);
    }

    // ---- Error mapping -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CloudErrorCodes.InvalidApiKey, 401, false)]
    [InlineData(HttpStatusCode.TooManyRequests, CloudErrorCodes.RateLimit, 429, true)]
    [InlineData(HttpStatusCode.RequestTimeout, CloudErrorCodes.Timeout, 408, true)]
    [InlineData(HttpStatusCode.InternalServerError, CloudErrorCodes.ServerError, 500, true)]
    [InlineData(HttpStatusCode.BadGateway, CloudErrorCodes.ServerError, 502, true)]
    public async Task Http_status_codes_map_to_explicit_error_codes(
        HttpStatusCode status,
        string expectedCode,
        int expectedStatus,
        bool expectedRetryable)
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Response(status, """{"error":{"message":"denied"}}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, ApiKey), Token));

        Assert.Equal(expectedCode, exception.ErrorCode);
        Assert.Equal(expectedStatus, exception.StatusCode);
        Assert.Equal(expectedRetryable, exception.IsRetryable);
        Assert.True(File.Exists(wav.Path));
    }

    [Fact]
    public async Task Bad_request_keeps_the_api_details_without_the_key()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(
            _ => Response(
                HttpStatusCode.BadRequest,
                "{\"error\":{\"message\":\"Invalid key " + ApiKey + " or bad audio\"}}"));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.OpenAi, ApiKey), Token));

        Assert.Equal(CloudErrorCodes.BadRequest, exception.ErrorCode);
        Assert.Contains("bad audio", exception.Message, StringComparison.Ordinal);
        Assert.Contains("gsk***def", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unauthorized_never_exposes_the_key_in_the_message()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(
            _ => Response(
                HttpStatusCode.Unauthorized,
                "{\"error\":{\"message\":\"Bearer " + ApiKey + " is invalid\"}}"));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, ApiKey), Token));

        Assert.Equal(CloudErrorCodes.InvalidApiKey, exception.ErrorCode);
        Assert.DoesNotContain(ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreachable_provider_maps_to_no_network()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(
            _ => throw new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused)));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, ApiKey), Token));

        Assert.Equal(CloudErrorCodes.NoNetwork, exception.ErrorCode);
        Assert.Null(exception.StatusCode);
        Assert.True(exception.IsRetryable);
        Assert.True(File.Exists(wav.Path));
    }

    [Fact]
    public async Task Transport_timeout_maps_to_timeout()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => throw new TaskCanceledException("The request timed out."));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, ApiKey), Token));

        Assert.Equal(CloudErrorCodes.Timeout, exception.ErrorCode);
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public async Task A_cancelled_token_is_reported_as_timeout()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":"never"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(
                wav.Path,
                new CloudEngineConfig(CloudProvider.Groq, ApiKey),
                cancellation.Token));

        Assert.Equal(CloudErrorCodes.Timeout, exception.ErrorCode);
    }

    [Fact]
    public async Task Invalid_json_on_success_maps_to_bad_request()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("not json"));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, ApiKey), Token));

        Assert.Equal(CloudErrorCodes.BadRequest, exception.ErrorCode);
    }

    [Fact]
    public async Task Json_without_a_text_field_maps_to_bad_request()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"segments":[]}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, ApiKey), Token));

        Assert.Equal(CloudErrorCodes.BadRequest, exception.ErrorCode);
    }

    // ---- Guards --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Missing_recording_fails_before_any_request_is_sent()
    {
        var handler = new CapturingHandler(_ => Ok("""{"text":"never"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => client.TranscribeAsync(
                Path.Combine(Path.GetTempPath(), $"somning-missing-{Guid.NewGuid():N}.wav"),
                new CloudEngineConfig(CloudProvider.Groq, ApiKey),
                Token));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_local_provider_is_rejected_before_any_request_is_sent()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":"never"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.None, ApiKey), Token));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_missing_key_classifies_as_invalid_api_key_without_a_request()
    {
        using var wav = new WavFile();
        var handler = new CapturingHandler(_ => Ok("""{"text":"never"}"""));
        var client = new CloudTranscriptionClient(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<CloudTranscriptionException>(
            () => client.TranscribeAsync(wav.Path, new CloudEngineConfig(CloudProvider.Groq, " "), Token));

        Assert.Equal(CloudErrorCodes.InvalidApiKey, exception.ErrorCode);
        Assert.False(exception.IsRetryable);
        Assert.Equal(0, handler.Calls);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    private static HttpResponseMessage Ok(string body) => Response(HttpStatusCode.OK, body);

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        Dictionary<string, string> Fields,
        string? FileName,
        long FileLength);

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public CapturedRequest? Captured { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            string? fileName = null;
            long fileLength = 0;

            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty;
                    if (name == "file")
                    {
                        fileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                        fileLength = (await part.ReadAsByteArrayAsync(cancellationToken)).Length;
                    }
                    else
                    {
                        fields[name] = await part.ReadAsStringAsync(cancellationToken);
                    }
                }
            }

            Captured = new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                fields,
                fileName,
                fileLength);

            return respond(request);
        }
    }

    private sealed class WavFile : IDisposable
    {
        public WavFile(double seconds = 1, int sampleRate = 16_000, short channels = 1)
        {
            const short bitsPerSample = 16;
            var byteRate = sampleRate * channels * bitsPerSample / 8;
            var dataSize = (int)(byteRate * seconds);

            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "somning-cloud-tests",
                Guid.NewGuid().ToString("N") + ".wav");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

            using var stream = new FileStream(Path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream, Encoding.ASCII);
            writer.Write("RIFF"u8);
            writer.Write(36 + dataSize);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * bitsPerSample / 8));
            writer.Write(bitsPerSample);
            writer.Write("data"u8);
            writer.Write(dataSize);
            writer.Write(new byte[dataSize]);
            writer.Flush();

            Length = new FileInfo(Path).Length;
        }

        public string Path { get; }

        public long Length { get; }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
