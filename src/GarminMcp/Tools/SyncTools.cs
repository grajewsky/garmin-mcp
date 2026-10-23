using System.ComponentModel;
using GarminMcp.Sync;
using ModelContextProtocol.Server;

namespace GarminMcp.Tools;

[McpServerToolType]
public sealed class SyncTools(SyncJobRunner runner)
{
    [McpServerTool(Name = "sync")]
    [Description("Start fetching data from Garmin Connect into the local database. Returns immediately; poll sync_status for progress. Without dates: fetches everything missing and refreshes the last 7 days. With from/to (YYYY-MM-DD, inclusive): re-fetches that range.")]
    public async Task<string> Sync(
        [Description("Range start, YYYY-MM-DD, inclusive (optional)")] string? from = null,
        [Description("Range end, YYYY-MM-DD, inclusive (optional)")] string? to = null)
    {
        if (!ToolJson.TryParseDate(from, out var fromDate))
            return ToolJson.Error($"Invalid 'from' date '{from}'; expected YYYY-MM-DD.");
        if (!ToolJson.TryParseDate(to, out var toDate))
            return ToolJson.Error($"Invalid 'to' date '{to}'; expected YYYY-MM-DD.");
        if (fromDate is { } f && toDate is { } t && f > t)
            return ToolJson.Error("'from' must not be after 'to'.");

        return ToolJson.Serialize(await runner.StartAsync(fromDate, toDate));
    }

    [McpServerTool(Name = "sync_status")]
    [Description("Show progress of the latest sync: status (running, completed, failed, auth_failed, interrupted, never_run), current phase, items done/total, recent errors and the time of the last successful sync.")]
    public async Task<string> SyncStatus()
    {
        var view = await runner.GetStatusAsync();
        return view is null ? ToolJson.Serialize(new { Status = "never_run" }) : ToolJson.Serialize(view);
    }
}
