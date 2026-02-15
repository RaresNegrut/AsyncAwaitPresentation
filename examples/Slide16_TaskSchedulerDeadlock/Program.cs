// Slide 16: TaskScheduler — The Sticky Deadlock Trap
// Custom TaskSchedulers are "sticky" — they flow through await continuations.
// This can deadlock if the scheduler has limited concurrency.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 16: TaskScheduler — Sticky Deadlock Trap ===\n");

// --- DEADLOCK scenario (with timeout so the demo doesn't hang forever) ---
Console.WriteLine("1) DEADLOCK scenario: 3 async tasks on 2-thread scheduler...");
var scheduler = new ConcurrentExclusiveSchedulerPair(
    TaskScheduler.Default, maxConcurrencyLevel: 2).ConcurrentScheduler;

using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
try
{
    var tasks = new Task[3];
    for (int i = 0; i < 3; i++)
    {
        int id = i;
        tasks[i] = Task.Factory.StartNew(async () =>
        {
            Console.WriteLine($"   Task {id} starting on Thread {Environment.CurrentManagedThreadId}");
            // ⚠️ Continuation stays on the limited scheduler!
            await Task.Delay(500);
            Console.WriteLine($"   Task {id} done on Thread {Environment.CurrentManagedThreadId}");
        }, cts.Token, TaskCreationOptions.None, scheduler).Unwrap();
    }
    await Task.WhenAll(tasks).WaitAsync(cts.Token);
    Console.WriteLine("   Completed (no deadlock occurred this time).");
}
catch (OperationCanceledException)
{
    Console.WriteLine("   ⏱️ Timed out — likely deadlocked!\n");
}

// --- FIX with ConfigureAwait(false) ---
Console.WriteLine("2) FIX: ConfigureAwait(false) escapes the custom scheduler...");
var tasks2 = new Task[3];
for (int i = 0; i < 3; i++)
{
    int id = i;
    tasks2[i] = Task.Factory.StartNew(async () =>
    {
        Console.WriteLine($"   Task {id} starting on Thread {Environment.CurrentManagedThreadId}");
        await Task.Delay(500).ConfigureAwait(false); // ✅ escape to ThreadPool
        Console.WriteLine($"   Task {id} done on Thread {Environment.CurrentManagedThreadId}");
    }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap();
}
await Task.WhenAll(tasks2);
Console.WriteLine("   ✅ All completed — no deadlock!");
