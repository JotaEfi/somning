# Plan 03-01: Cloud Transcriber Contracts, Groq & OpenAI Clients, Error Mapping & Masking Summary

**Phase**: 03 - Cloud Engine  
**Wave**: 1  
**Status**: Completed  
**Requirements Satisfied**: STT-05, STT-06, APP-05, APP-06  

## Achievements
1. **Core Cloud Domain Contracts (`Somning.Core.Cloud`)**:
   - `CloudProvider`: string constants `None = "none"`, `Groq = "groq"`, `OpenAi = "openai"`; models `whisper-large-v3-turbo` (Groq), `whisper-1` (OpenAI); endpoints and normalization.
   - `ApiKeyMasker`: `Mask(key)` (keeps first/last 3 chars if > 8 chars, otherwise masks fully with `***`), `Redact(text, key)` ensures plaintext keys never reach logs or exceptions.
   - `CloudEngineConfig`: record storing `Provider`, `ApiKey` (`[JsonIgnore]`), `CustomVocabulary`, `CustomEndpoint`; safe `ToString()` masking the key.
   - `CloudTranscriptionException`: carries `ErrorCode` (`no_network`, `invalid_api_key`, `rate_limit`, `timeout`, `bad_request`, `server_error`), HTTP status code, and `IsRetryable`.
   - `ICloudTranscriptionClient` & `CloudTranscriptionResult`.
2. **HTTP Implementation (`CloudTranscriptionClient`)**:
   - Sends `MultipartFormDataContent` with: `file` (.wav), `model`, `language="pt"`, `response_format="json"`, and optional `prompt` (custom vocabulary).
   - `Authorization: Bearer <ApiKey>` header.
   - Strict exception mapping:
     - 401 Unauthorized -> `invalid_api_key`
     - 429 Too Many Requests -> `rate_limit`
     - 408 / Timeout -> `timeout`
     - 400 / 4xx -> `bad_request`
     - 5xx -> `server_error`
     - Socket/DNS/transport -> `no_network`
   - WAV audio duration reading and inference latency measurement.
   - Sanitizes and ensures keys never leak into exception messages or logs.
3. **Overlay pt-BR State Extension (`Somning.Core.Overlay`)**:
   - Added `CloudNoNetwork` ("Sem conexão com a internet"), `CloudInvalidKey` ("Chave de API inválida"), `CloudRateLimit` ("Limite de requisições excedido"), and `CloudTimeout` ("Tempo limite esgotado") to `OverlayNoticeKind` and `OverlayText.For`.
4. **Verification**:
   - 47 new unit tests in `Somning.Core.Tests/Cloud/` (284 total tests in Core).
   - Solution builds with 0 errors and 0 warnings (`TreatWarningsAsErrors` enabled).
