# Plan 02-06 Summary: First-Run Model Downloader & Verification

## Requirements Covered
- **STT-07**: "First run with no model downloads it with progress, resume and SHA-256 verification against a local test server, reporting retryable failures."
- **APP-05**: User-facing progress and notice messages in pt-BR.

## Work Delivered

### Task 1: Core Model Downloader with Resume & SHA-256 (OpenCode - DeepSeek V4.1 Flash Free)
- `Somning.Core/Model/ModelDownloadProgress.cs`: immutable progress snapshot (`BytesDownloaded`, `TotalBytes`, `Percent`, `SpeedBps`).
- `Somning.Core/Model/ModelDownloadExceptions.cs`: `ModelChecksumMismatchException` and `ModelDownloadFailedException(message, isRetryable)`.
- `Somning.Core/Model/ModelDownloadManager.cs`:
  - Downloads to `${destinationPath}.part` first.
  - Sends HTTP `Range: bytes={existingLength}-` header to resume partially downloaded transfers.
  - Handles HTTP 206 Partial Content (append) and resets when server signals `416 Range Not Satisfiable`.
  - Calculates accurate percentage and transfer speed.
  - Closes file streams before SHA-256 computation to prevent Windows lock conflicts.
  - Verifies SHA-256 checksum at completion: if valid, atomically moves `.part` to final destination; if invalid, deletes the corrupted partial file and throws `ModelChecksumMismatchException`.
  - `IsRetryable(Exception ex)` identifies transient HTTP status codes (408, 500, 502, 503, 504), network timeouts, and socket connection drops.
- 33 unit tests in `Somning.Core.Tests/Model/`.

### Task 2: App Integration & Local Server Tests (Antigravity)
- `Somning.App/Composition/AppComposition.cs`:
  - Added `EnsureModelAvailableAsync()` on background engine startup path.
  - Displays overlay notice `OverlayNoticeKind.ModelMissing` during download and auto-hides when complete.
- `Somning.Integration.Tests/Model/ModelDownloadIntegrationTests.cs`:
  - Built comprehensive tests using in-process dynamic `HttpListener`:
    1. `FullDownload_Succeeds_AndVerifiesSha256`: full 64 KB download verified with SHA-256.
    2. `ResumedDownload_UsesRangeHeader_AndSucceeds`: simulated 32 KB partial file, server receives `Range: bytes=32768-`, serves HTTP 206 Partial Content, client merges and passes SHA-256.
    3. `CorruptedDownload_ThrowsChecksumMismatch_AndDeletesCorruptedFile`: wrong expected checksum detected, throws `ModelChecksumMismatchException` and deletes partial file.
    4. `RetryableServerFailure_503_RetriesAndSucceeds`: transient HTTP 503 on first try, downloader automatically retries and succeeds on second attempt.

## Verification
- `Somning.Core.Tests`: 232/232 PASS (0 failures, 0 warnings).
- `Somning.Integration.Tests`: 49/49 PASS across all Phase 2 integration scenarios.
- 100% of Phase 2 criteria are satisfied.
