using GarminMcp.Sync;

namespace GarminMcp.Tests.Support;

public sealed class RecordingProgress : ISyncProgress
{
    public List<(string Phase, int Total)> Phases { get; } = [];
    public int Done { get; private set; }
    public List<string> Errors { get; } = [];

    public void SetPhase(string phase, int total) => Phases.Add((phase, total));
    public void Advance(int n = 1) => Done += n;
    public void AddError(string message) => Errors.Add(message);
}
