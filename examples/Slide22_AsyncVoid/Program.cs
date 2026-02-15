#nullable enable
// Slide 22: async void — The Danger Zone
// async void exceptions crash the process (in console apps w/o SynchronizationContext).
// Always prefer async Task.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 22: async void — The Danger Zone ===\n");

// --- async Task: exception is captured, observable ---
Console.WriteLine("1) async Task — exception is stored on the Task:");
// DoWorkAsyncTask();'
Console.WriteLine(SynchronizationContext.Current);
try
{
    await DoWorkAsyncTask();
}
catch (Exception ex)
{
    Console.WriteLine($"   Caught: {ex.Message} ✅ (Task was faulted)\n");
}

// --- async void: cannot observe the exception ---
Console.WriteLine("2) async void — exception goes to SynchronizationContext/ThreadPool:");
Console.WriteLine("   The AsyncVoidMethodBuilder posts the exception to SynchronizationContext.Post.");
Console.WriteLine("   Without a SyncContext (console app), it would go to the ThreadPool and crash.\n");

// We install a custom SynchronizationContext to intercept the exception safely.
// Without this, the exception would be thrown on the ThreadPool → process crash.
// var ctx = new AsyncVoidSyncContext(tcs);
// SynchronizationContext.SetSynchronizationContext(ctx);
Console.WriteLine(SynchronizationContext.Current);
try
{
    DoWorkAsyncVoid();
}
catch (Exception e)
{
    Console.WriteLine("We'll never get here", e);
}

// --- Methods ---
static async Task DoWorkAsyncTask()
{
    throw new InvalidOperationException("Oops from async Task");
}

static async void DoWorkAsyncVoid()
{
    throw new InvalidOperationException("Oops from async void");
}

// Catches async void exceptions instead of letting them crash the process
