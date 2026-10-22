using Somning.Core.Engine;
using Somning.Engine.Protocol;
namespace Somning.App.Engine;
public static class ProtocolMapping
{
    public static EngineTranscription Map(ResultMessage result, EngineSessionInfo session, long queueMs = 0) =>
        new(result.Text, result.Outcome, result.Reason, null, false, (long)result.DurationMs,
            (long)result.VadMs, (long)result.InferMs, queueMs, session.LoadMs, session.WarmMs,
            result.Backend, result.Model, result.NoSpeech);
    public static EngineTranscription Map(ErrorMessage error, EngineSessionInfo session) =>
        new("", "error", null, error.Code, error.Code is not (ErrorCodes.InvalidPath or ErrorCodes.BadRequest),
            0, 0, 0, 0, session.LoadMs, session.WarmMs, session.Backend, session.Model, 0);
}
