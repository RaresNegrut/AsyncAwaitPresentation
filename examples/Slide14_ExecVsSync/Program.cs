#nullable enable
// Slide 14: ExecutionContext vs SynchronizationContext — Summary
// ConfigureAwait(false) drops SynchronizationContext but NOT ExecutionContext.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 14: ExecutionContext vs SynchronizationContext ===\n");

var userId = new AsyncLocal<string>();

userId.Value = "alice";
SynchronizationContext.SetSynchronizationContext(new DemoSyncContext());

Console.WriteLine($"Before: userId={userId.Value}, SyncCtx={SynchronizationContext.Current?.GetType().Name}");

await Task.Delay(100).ConfigureAwait(false);

Console.WriteLine($"After:  userId={userId.Value}, SyncCtx={SynchronizationContext.Current?.GetType().Name ?? "null"}");
Console.WriteLine();
Console.WriteLine("ExecutionContext (AsyncLocal) → still flows ✅");
Console.WriteLine("SynchronizationContext        → dropped by ConfigureAwait(false) ❌");

class DemoSyncContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            SetSynchronizationContext(this);
            d(state);
        });
    }
}
