using System.Collections.Concurrent;

namespace PersonalAgent.Common;

/// <summary>
/// In-memory, per-session queue of short human-readable status lines an agent tool can
/// publish while it's still working (e.g. one line per ingredient as a Bing search
/// resolves), so the frontend can poll GET /api/agent/progress and show them as a
/// "typing..." trail while the main POST /api/agent/ask call is still in flight.
/// Not durable/shared across scaled-out instances - fine for a UX nicety, not
/// correctness-critical (worst case: a scaled-out instance just shows no progress lines).
/// </summary>
public sealed class AgentProgressTracker
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _bySession = new();

    public void Publish(string sessionId, string message)
    {
        var queue = _bySession.GetOrAdd(sessionId, static _ => new ConcurrentQueue<string>());
        queue.Enqueue(message);
    }

    /// <summary>Returns and removes all messages queued so far for this session.</summary>
    public IReadOnlyList<string> Drain(string sessionId)
    {
        if (!_bySession.TryGetValue(sessionId, out var queue))
        {
            return [];
        }

        var results = new List<string>();
        while (queue.TryDequeue(out var message))
        {
            results.Add(message);
        }
        return results;
    }
}
