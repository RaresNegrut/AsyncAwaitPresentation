#nullable enable
// Slide 8: ConfigureAwait(false) — What It Does
// Demonstrates that ConfigureAwait(false) skips SynchronizationContext capture.
// We install a custom SynchronizationContext to make the effect visible.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 8: ConfigureAwait(false) ===\n");

// Install a custom SynchronizationContext so we can see the difference
var customCtx = new CustomSyncContext();
SynchronizationContext.SetSynchronizationContext(customCtx);

Console.WriteLine($"SyncContext before: {SynchronizationContext.Current?.GetType().Name ?? "null"}");

await WithCapturedContext();
await WithoutCapturedContext();

static async Task WithCapturedContext()
{
    Console.WriteLine("\n--- ConfigureAwait(true) (default) ---");
    Console.WriteLine($"  Before await - SyncCtx: {SynchronizationContext.Current?.GetType().Name ?? "null"}");

    await Task.Delay(100); // default: ConfigureAwait(true)

    Console.WriteLine($"  After await  - SyncCtx: {SynchronizationContext.Current?.GetType().Name ?? "null"}");
    Console.WriteLine($"  Continuation was posted back to our custom context.");
}

static async Task WithoutCapturedContext()
{
    Console.WriteLine("\n--- ConfigureAwait(false) ---");
    Console.WriteLine($"  Before await - SyncCtx: {SynchronizationContext.Current?.GetType().Name ?? "null"}");

    await Task.Delay(100).ConfigureAwait(false);

    Console.WriteLine($"  After await  - SyncCtx: {SynchronizationContext.Current?.GetType().Name ?? "null"}");
    Console.WriteLine($"  Continuation ran on ThreadPool — context was NOT captured.");
}

// A minimal custom SynchronizationContext that posts to the ThreadPool
// but identifies itself so we can see when it's present.
class CustomSyncContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        // Restore this context on the callback thread so it stays "sticky"
        ThreadPool.QueueUserWorkItem(_ =>
        {
            SetSynchronizationContext(this);
            d(state);
        });
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);
}
