namespace Somning.Engine.Protocol;
public static class ErrorCodes
{
    public const string BackendInitFailed = "backend_init_failed", GpuError = "gpu_error",
        ModelMissing = "model_missing", InvalidPath = "invalid_path", BadRequest = "bad_request",
        TranscribeFailed = "transcribe_failed", Cancelled = "cancelled";
}
