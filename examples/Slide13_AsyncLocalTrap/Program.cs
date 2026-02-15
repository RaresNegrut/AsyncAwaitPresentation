// Slide 13: AsyncLocal<T> — The Synchronous Trap
// Synchronous methods do NOT get copy-on-write isolation.
// Their AsyncLocal changes leak to the caller!

using System;
using System.Threading;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 13: AsyncLocal — Synchronous Trap ===\n");

var ctx = new AsyncLocal<int>();

// --- Sync method LEAKS ---
ctx.Value = 1;
Console.WriteLine($"Before SyncMethod:  {ctx.Value}");
SyncMethod(ctx);
Console.WriteLine($"After SyncMethod:   {ctx.Value}");
Console.WriteLine("  ↑ 999! Sync method leaked its change to the caller.\n");

// --- Async method is ISOLATED ---
ctx.Value = 1;
Console.WriteLine($"Before AsyncMethod: {ctx.Value}");
await AsyncMethod(ctx);
Console.WriteLine($"After AsyncMethod:  {ctx.Value}");
Console.WriteLine("  ↑ Still 1! Async method's change was isolated.");

static void SyncMethod(AsyncLocal<int> ctx)
{
    ctx.Value = 999; // 💥 Leaks to caller! No save/restore wrapper!
}

static async Task AsyncMethod(AsyncLocal<int> ctx)
{
    ctx.Value = 999; // ✅ Isolated — caller won't see this
    await Task.Yield();
}
