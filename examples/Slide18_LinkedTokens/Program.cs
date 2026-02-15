// Slide 18: CancellationToken — Linked Tokens & Timeouts
// Demonstrates combining caller cancellation with a per-operation timeout.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 18: Linked Tokens & Timeouts ===\n");

// Simulate caller cancellation source (e.g., user hitting Cancel)
using var callerCts = new CancellationTokenSource();

// Scenario 1: timeout fires before caller cancels
Console.WriteLine("Scenario 1: Timeout fires (2s timeout, work takes 5s)...");
try
{
    await GenerateReportAsync(callerCts.Token, workDuration: 5000, timeoutMs: 2000);
}
catch (TimeoutException ex)
{
    Console.WriteLine($"  Caught: {ex.Message} ✅\n");
}

// Scenario 2: caller cancels before timeout
Console.WriteLine("Scenario 2: Caller cancels (after 500ms, timeout 10s)...");
using var callerCts2 = new CancellationTokenSource(500);
try
{
    await GenerateReportAsync(callerCts2.Token, workDuration: 5000, timeoutMs: 10000);
}
catch (OperationCanceledException)
{
    Console.WriteLine("  Caught: OperationCanceledException (caller cancelled) ✅");
}

static async Task<string> GenerateReportAsync(
    CancellationToken cancellationToken, int workDuration, int timeoutMs)
{
    using var cts = CancellationTokenSource
        .CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

    try
    {
        // Simulate long work
        await Task.Delay(workDuration, cts.Token);
        return "Report data";
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        // It was OUR timeout, not the caller's cancellation
        throw new TimeoutException("Report generation timed out.");
    }
    // If cancellationToken.IsCancellationRequested is true,
    // the OperationCanceledException propagates naturally.
}
