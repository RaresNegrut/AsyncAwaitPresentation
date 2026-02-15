// Slide 23: Task.Run vs Task.Factory.StartNew
// Task.Factory.StartNew with async lambda returns Task<Task> — the outer
// task completes immediately, hiding failures in the inner task.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 23: Task.Run vs Task.Factory.StartNew ===\n");

// ❌ Task.Factory.StartNew with async — returns Task<Task>!
Console.WriteLine("1) Task.Factory.StartNew with async lambda (WRONG):");
var outerTask = Task.Factory.StartNew(async () =>
{
    await Task.Delay(200);
    throw new InvalidOperationException("Inner error!");
});
// outerTask is Task<Task> — the outer completes immediately
await outerTask; // This awaits the OUTER task only
Console.WriteLine($"   outerTask.Status: {outerTask.Status}");
Console.WriteLine($"   outerTask.Result.Status: {outerTask.Result.Status}");
Console.WriteLine("   ⚠️ Outer is RanToCompletion, but inner is Faulted!\n");

// ✅ Fix with .Unwrap()
Console.WriteLine("2) Task.Factory.StartNew + Unwrap (correct):");
try
{
    var unwrapped = Task.Factory.StartNew(async () =>
    {
        await Task.Delay(200);
        throw new InvalidOperationException("Inner error!");
    }).Unwrap();
    await unwrapped;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"   Caught: {ex.Message} ✅\n");
}

// ✅ Task.Run — handles unwrapping automatically
Console.WriteLine("3) Task.Run (best approach):");
try
{
    await Task.Run(async () =>
    {
        await Task.Delay(200);
        throw new InvalidOperationException("Inner error!");
    });
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"   Caught: {ex.Message} ✅");
}
