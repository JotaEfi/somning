namespace Somning.Core.Ports;

/// <summary>Result of one hook liveness probe.</summary>
public enum ProbeOutcome { Seen, NotSeen, Inconclusive }

/// <summary>Probes whether the low-level keyboard hook still receives input.</summary>
public interface IHookLivenessProbe
{
    Task<ProbeOutcome> ProbeAsync(CancellationToken cancellationToken);
}
