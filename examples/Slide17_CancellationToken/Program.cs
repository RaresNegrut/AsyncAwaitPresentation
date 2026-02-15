// Slide 17: CancellationToken — Fundamentals
// Demonstrates cooperative cancellation: polling and passing tokens.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 17: CancellationToken — Fundamentals ===\n");

using var cts = new CancellationTokenSource();

// Cancel after 1 second
cts.CancelAfter(TimeSpan.FromSeconds(1));

try
{
    await ProcessItemsAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Operation was cancelled! ✅");
}

static async Task ProcessItemsAsync(CancellationToken cancellationToken)
{
    for (int i = 0; i < 20; i++)
    {
        // Option 1: Poll — check if cancellation was requested
        cancellationToken.ThrowIfCancellationRequested();

        Console.WriteLine($"  Processing item {i}...");

        // Option 2: Pass token to framework methods
        await Task.Delay(200, cancellationToken);
    }
}
