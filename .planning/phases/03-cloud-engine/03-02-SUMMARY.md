# Plan 03-02: Mock HTTP Fixture Suite, Recording Retention & Cloud Adapter Integration Summary

**Phase**: 03 - Cloud Engine  
**Wave**: 2  
**Status**: Completed  
**Requirements Satisfied**: STT-05, STT-06, APP-02, APP-05, APP-06  

## Achievements
1. **`RouterTranscriber : ITranscriber` (`Somning.App.Engine`)**:
   - Seamlessly routes between local Whisper supervisor (`EngineSupervisor`) and cloud transcription client (`CloudTranscriptionClient`) based on `UserSettings.CloudProvider`.
   - Decrypts DPAPI-encrypted API keys using `ISettingsProtector` (`DpapiSettingsProtector`) on demand; if missing or invalid, returns `invalid_api_key` without exposing ciphertext.
   - On cloud transcription failures (`CloudTranscriptionException`), returns `Outcome="error"`, `ErrorCode=ex.ErrorCode` with `Retryable=true` and redacted message.
   - **Critical**: Never performs silent fallback to local Whisper, ensuring that user intent is respected and recordings remain intact for retry.
2. **Overlay Notices Integration (`EffectRunner`)**:
   - `EffectRunner.HandleOutcomeOverlay` inspects `metrics.Reason` through `OverlayNotices.ForErrorCode`.
   - Displays appropriate pt-BR messages:
     - `no_network` -> `OverlayNoticeKind.CloudNoNetwork` ("Sem conexão com a internet")
     - `invalid_api_key` -> `OverlayNoticeKind.CloudInvalidKey` ("Chave de API inválida")
     - `rate_limit` -> `OverlayNoticeKind.CloudRateLimit` ("Limite de requisições excedido")
     - `timeout` -> `OverlayNoticeKind.CloudTimeout` ("Tempo limite esgotado")
   - Automatically hides after 1800ms.
3. **App Composition Integration (`AppComposition`)**:
   - Instantiates `RouterTranscriber` wrapping `EngineSupervisor`, `CloudTranscriptionClient`, `() => Settings`, and `DpapiSettingsProtector`.
   - Injects `RouterTranscriber` as `ITranscriber` into `EffectRunner`.
4. **Integration Test Suite (`Somning.Integration.Tests.Cloud`)**:
   - `RouterTranscriberTests` (13 tests): Local delegation, Groq happy path, OpenAI happy path, missing key rejection, DPAPI decryption failure, exception redaction, no silent fallback.
   - `CloudIntegrationTests` (12 tests): In-process dynamic `HttpListener` test server executing tests against `tests/fixtures/tracer/hoje.wav`:
     - Groq 200 OK multipart upload.
     - OpenAI 200 OK multipart upload.
     - 401 Unauthorized handling with recording preservation.
     - 429 Too Many Requests handling.
     - 408/Timeout and connection refused handling.
     - Real API tests (Groq/OpenAI) with graceful skipping when environment keys are absent.
5. **Verification**:
   - `Somning.Core.Tests`: 294/294 passed.
   - `Somning.Integration.Tests` (Cloud): 23 passed, 2 skipped (real API without env vars).
   - `Somning.sln` builds with 0 warnings and 0 errors.
