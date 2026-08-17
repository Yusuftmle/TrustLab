using System.Diagnostics;
using TrustLab.Domain.Models;

namespace TrustLab.Telemetry;

public sealed class ExecutionTracer
{
    private readonly Stopwatch _stopwatch = new();
    private readonly List<AgentStepTrace> _traces = new();
    private readonly object _lock = new();

    public void Start()
    {
        lock (_lock)
        {
            _traces.Clear();
            _stopwatch.Restart();
        }
    }

    public void RecordStep(
        string stepName,
        AgentWorkflowState state,
        IReadOnlyDictionary<string, object>? metadata = null,
        string? notes = null)
    {
        lock (_lock)
        {
            long elapsed = _stopwatch.ElapsedMilliseconds;
            _traces.Add(new AgentStepTrace(stepName, state, elapsed, metadata, notes));
        }
    }

    public IReadOnlyList<AgentStepTrace> GetTraces()
    {
        lock (_lock)
        {
            return _traces.ToList();
        }
    }

    public long TotalElapsedMilliseconds
    {
        get
        {
            lock (_lock)
            {
                return _stopwatch.ElapsedMilliseconds;
            }
        }
    }
}
