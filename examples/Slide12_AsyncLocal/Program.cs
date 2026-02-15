// Slide 12: AsyncLocal<T> — Flows Down, Not Up
// Demonstrates that child async methods get a COPY of the ExecutionContext.
// Modifications in the child do NOT leak back to the parent.

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 12: AsyncLocal<T> — Flows Down, Not Up ===\n");

var traceId = new AsyncLocal<string>();

traceId.Value = "REQ-001";
Console.WriteLine($"Main before child: {traceId.Value}");

await ChildAsync(traceId);

Console.WriteLine($"Main after child:  {traceId.Value}");
Console.WriteLine("  ↑ Still REQ-001! Child's change did NOT leak up.\n");

static async Task ChildAsync(AsyncLocal<string> traceId)
{
    Console.WriteLine($"  Child before set: {traceId.Value}");

    traceId.Value = "REQ-002"; // only affects this scope and below
    Console.WriteLine($"  Child after set:  {traceId.Value}");

    await Task.Yield();
    Console.WriteLine($"  Child after yield: {traceId.Value}");
}
