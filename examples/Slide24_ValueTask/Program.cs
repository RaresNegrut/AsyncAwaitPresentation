// Slide 24: ValueTask — When and Why
// ValueTask<T> avoids allocation when the result is synchronously available.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 24: ValueTask — When and Why ===\n");

var cache = new Dictionary<int, string> { [1] = "Alice", [2] = "Bob" };

// Cache hit → synchronous, no Task allocation
Console.WriteLine("Cache hit (synchronous path):");
var user1 = await GetUserAsync(1, cache);
Console.WriteLine($"  Result: {user1}\n");

// Cache miss → goes async, wraps the Task
Console.WriteLine("Cache miss (async path):");
var user3 = await GetUserAsync(3, cache);
Console.WriteLine($"  Result: {user3}\n");

// ❌ WRONG: awaiting a ValueTask twice
Console.WriteLine("⚠️ ValueTask rules — never await twice, never use with WhenAll.");
Console.WriteLine("   Convert to Task first: valueTask.AsTask()");

static ValueTask<string> GetUserAsync(int id, Dictionary<int, string> cache)
{
    if (cache.TryGetValue(id, out var user))
    {
        Console.WriteLine($"  [cache hit] No allocation — returning ValueTask wrapping value.");
        return new ValueTask<string>(user); // No allocation!
    }

    Console.WriteLine($"  [cache miss] Going async — ValueTask wraps a Task.");
    return new ValueTask<string>(GetUserFromDbAsync(id, cache));
}

static async Task<string> GetUserFromDbAsync(int id, Dictionary<int, string> cache)
{
    await Task.Delay(100); // simulate DB call
    var name = $"User-{id}";
    cache[id] = name;
    return name;
}
