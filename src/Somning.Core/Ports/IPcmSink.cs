namespace Somning.Core.Ports;

public interface IPcmSink
{
    void Write(ReadOnlySpan<byte> pcm16kMonoS16);
}
