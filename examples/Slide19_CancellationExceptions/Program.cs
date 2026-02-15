// Slide 19: CancellationToken — Exception Handling
// Always catch OperationCanceledException, not TaskCanceledException.
// Check IsCancellationRequested to distinguish your cancellation from others.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 19: CancellationToken — Exception Handling ===\n");

// Scenario 1: OUR token triggers cancellation
Console.WriteLine("Scenario 1: Our token triggers cancellation...");
using var cts1 = new CancellationTokenSource(300);
await ExecuteWithRetryAsync("Our cancel", cts1.Token);

// Scenario 2: Internal timeout (not our token)
Console.WriteLine("\nScenario 2: Internal timeout (not our token)...");
using var cts2 = new CancellationTokenSource(); // never cancelled
await ExecuteWithRetryAsync("Internal timeout", cts2.Token);

static async Task ExecuteWithRetryAsync(string label, CancellationToken ct)
{
    try
    {
        // Simulate an operation with an internal timeout
        using var internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        internalCts.CancelAfter(500);

        await Task.Delay(2000, internalCts.Token);
        Console.WriteLine($"  [{label}] Completed successfully.");
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // ✅ Clean cancellation — the caller asked us to stop
        Console.WriteLine($"  [{label}] Cancelled by caller (our token). ✅");
    }
    catch (OperationCanceledException)
    {
        // ⚠️ Not our token — could be internal timeout
        Console.WriteLine($"  [{label}] Cancelled by internal timeout (not our token). ⚠️");
    }
}
