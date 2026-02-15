// Slide 11: ExecutionContext — The Invisible Flow
// Demonstrates that AsyncLocal values flow across await boundaries,
// even to different threads. ConfigureAwait(false) does NOT suppress this.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 11: ExecutionContext (AsyncLocal flows across await) ===\n");

var requestId = new AsyncLocal<int>();

requestId.Value = 42;
Console.WriteLine($"Before await  - Thread: {Environment.CurrentManagedThreadId}, RequestId: {requestId.Value}");

await Task.Delay(100);

Console.WriteLine($"After await   - Thread: {Environment.CurrentManagedThreadId}, RequestId: {requestId.Value}");
Console.WriteLine("  ↑ AsyncLocal survived the await, possibly on a different thread!\n");

// Show that ConfigureAwait(false) does NOT affect ExecutionContext
requestId.Value = 99;
Console.WriteLine($"Before ConfigureAwait(false) - RequestId: {requestId.Value}");

await Task.Delay(100).ConfigureAwait(false);

Console.WriteLine($"After ConfigureAwait(false)  - RequestId: {requestId.Value}");
Console.WriteLine("  ↑ Still flows! ConfigureAwait(false) only affects SynchronizationContext.");
