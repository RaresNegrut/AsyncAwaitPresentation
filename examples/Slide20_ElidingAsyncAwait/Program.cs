// Slide 20: Eliding async/await — When to Omit the Keywords
// Shows safe eliding vs. the dangerous 'using' scenario.

using System;
using System.IO;
using System.Threading.Tasks;

Console.WriteLine("=== Slide 20: Eliding async/await ===\n");

// ✅ Safe to elide: trivial passthrough
Console.WriteLine("1) Safe passthrough (elided):");
var name = await GetNameAsync(1);
Console.WriteLine($"   Result: {name}\n");

// ❌ Dangerous: eliding with using — resource disposed too early
Console.WriteLine("2) DANGEROUS: eliding with 'using' (resource disposed too early):");
try
{
    var data = await GetDataElided_BROKEN();
    Console.WriteLine($"   Result: {data}");
}
catch (ObjectDisposedException ex)
{
    Console.WriteLine($"   💥 {ex.GetType().Name}: {ex.Message}\n");
}

// ✅ Fixed: keep async/await with using
Console.WriteLine("3) FIXED: keep async/await with 'using':");
var data2 = await GetDataAsync_FIXED();
Console.WriteLine($"   Result: {data2}");

// --- Methods ---

// ✅ Safe to elide  
static Task<string> GetNameAsync(int id)
    => Task.FromResult($"User-{id}");

// ❌ BUG: StreamReader is disposed before ReadToEndAsync completes
static Task<string> GetDataElided_BROKEN()
{
    using var reader = new StreamReader(
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world")));
    return reader.ReadToEndAsync();
    // reader is disposed HERE — before ReadToEndAsync finishes!
}

// ✅ Fixed: async/await keeps the using scope alive
static async Task<string> GetDataAsync_FIXED()
{
    using var reader = new StreamReader(
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes("hello world")));
    return await reader.ReadToEndAsync();
    // reader is disposed AFTER await completes ✅
}
