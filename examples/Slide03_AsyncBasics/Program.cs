// Slide 3: Quick Refresher — What await Actually Does
// Shows that the compiler transforms async methods into state machines.
// Notice how the thread can change across await boundaries.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 3: Async Basics ===\n");

Console.WriteLine($"Before await - Thread: {Environment.CurrentManagedThreadId}");

var result = await GetDataAsync();

Console.WriteLine($"After await  - Thread: {Environment.CurrentManagedThreadId}");
Console.WriteLine($"Result: {result}");

static async Task<string> GetDataAsync()
{
    Console.WriteLine($"  GetDataAsync before await - Thread: {Environment.CurrentManagedThreadId}");

    // This await point is where the state machine suspends.
    // Everything after this is a "continuation".
    await Task.Delay(500);

    Console.WriteLine($"  GetDataAsync after await  - Thread: {Environment.CurrentManagedThreadId}");
    return "Hello from async!";
}
