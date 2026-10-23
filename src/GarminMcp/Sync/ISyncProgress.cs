namespace GarminMcp.Sync;

public interface ISyncProgress
{
    void SetPhase(string phase, int total);
    void Advance(int n = 1);
    void AddError(string message);
}
