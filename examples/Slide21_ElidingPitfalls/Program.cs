// Slide 21: Eliding async/await — Exception Semantics Change
// With async: exceptions are placed on the Task.
// Without async: exceptions are thrown synchronously before a Task exists.

using System;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 21: Eliding Pitfalls — Exception Semantics ===\n");

// With async/await — exception stored on the Task
Console.WriteLine("1) With async/await:");
Task<int> task1 = DivideAsync(10, 0);
try
{
    await task1;
}
catch (DivideByZeroException)
{
    Console.WriteLine("   Exception caught via await. Task was faulted. ✅\n");
}

// Without async (elided) — exception thrown SYNCHRONOUSLY
Console.WriteLine("2) Without async (elided):");
try
{
    Task<int> task2 = DivideElided(10, 0); // 💥 Throws HERE
    await task2;
}
catch (DivideByZeroException)
{
    Console.WriteLine("   Exception thrown synchronously BEFORE a Task was created. 💥");
    Console.WriteLine("   Callers can't use try { await ... } catch to handle it cleanly.");
}

// --- Methods ---

static async Task<int> DivideAsync(int a, int b)
{
    return a / b; // Exception is captured on the Task
}

static Task<int> DivideElided(int a, int b)
{
    return Task.FromResult(a / b); // 💥 Throws before Task.FromResult runs
}
