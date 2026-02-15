// Slide 15: TaskScheduler — ConcurrentExclusiveSchedulerPair
// Shows how exclusive scheduling serializes access without locks.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 15: TaskScheduler — ConcurrentExclusiveSchedulerPair ===\n");

var pair = new ConcurrentExclusiveSchedulerPair();
var exclusive = pair.ExclusiveScheduler;

// Schedule 3 tasks on the exclusive scheduler — they run one at a time
var tasks = new Task[3];
for (int i = 0; i < 3; i++)
{
    int id = i;
    tasks[i] = Task.Factory.StartNew(async () =>
    {
        Console.WriteLine($"  Task {id} start  - Thread: {Environment.CurrentManagedThreadId}, Scheduler: exclusive");
        await Task.Delay(300);
        Console.WriteLine($"  Task {id} finish - Thread: {Environment.CurrentManagedThreadId}");
    }, CancellationToken.None, TaskCreationOptions.None, exclusive).Unwrap();
}

await Task.WhenAll(tasks);
Console.WriteLine("\nAll tasks completed. They ran one-at-a-time on the exclusive scheduler.");
