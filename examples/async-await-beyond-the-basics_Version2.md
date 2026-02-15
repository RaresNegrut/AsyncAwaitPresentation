# Async & Await — Beyond the Basics
### A Deep Dive for .NET Developers

---

## Slide 1: Title Slide

**Async & Await — Beyond the Basics**

*Going deeper than "just put await in front of it"*

Topics: SynchronizationContext, ExecutionContext, ConfigureAwait, CancellationTokens, TaskScheduler, AsyncLocal, Eliding async/await

Resources: Stephen Toub (Microsoft DevBlogs), Stephen Cleary (blog.stephencleary.com), Sergey Tepliakov (Dissecting the Code), Vasil Kosturski (vkontech.com), Microsoft Learn, Jon Skeet (eduasync)

---

## Slide 2: Motivation — Why This Talk?

**We all `await` async methods... but do we really understand them?**

The reality in most codebases:
- We `await` because "we must" or "the analyzer told us to"
- We sprinkle `ConfigureAwait(false)` like a superstition
- We copy-paste CancellationToken parameters without understanding propagation
- We have no idea what happens when our continuation resumes on a different thread

This talk aims to lift the veil on the machinery behind `async/await` and give you practical, actionable knowledge.

---

## Slide 3: Quick Refresher — What await Actually Does

**Why does this exist?**

Writing asynchronous code by hand — with callbacks, `ContinueWith`, and manual error propagation — is painful and error-prone. The `async/await` keywords let you write code that *reads* like synchronous code but *behaves* asynchronously. The compiler does the hard work for you.

**The compiler transforms your async method into a state machine.**

```csharp
// What you write:
async Task<string> GetDataAsync()
{
    var result = await httpClient.GetStringAsync("https://api.example.com");
    return result.ToUpper();
}
```

Under the hood, the compiler generates:
1. A **struct** implementing `IAsyncStateMachine` — but when the method actually suspends at an `await`, this struct gets boxed into a heap-allocated class called `AsyncStateMachineBox<TStateMachine>`, which itself *is* the `Task<TResult>` that gets returned to the caller. So the returned task and the state machine are the same object.
2. An `AsyncTaskMethodBuilder` that manages the returned `Task`
3. State transitions for each `await` point
4. Context capture and restoration logic

The key insight: **everything after `await` is a continuation**, scheduled by the infrastructure. *How* and *where* it's scheduled is the subject of this talk.

*(Ref: Sergey Tepliakov — "Dissecting the async methods in C#", Stephen Toub — "How Async/Await Really Works in C#")*

---

## Slide 4: SynchronizationContext — The Problem It Solves

**Why does this exist?**

Imagine you're writing a WPF app. You click a button, start downloading data from the internet, and when it's done, you want to update a label. But .NET UI frameworks have a rule: **only the UI thread can touch UI elements.** If your download finishes on a background thread and you try to update the label, you get a cross-thread exception.

Before `async/await`, you had to manually write `Dispatcher.BeginInvoke(...)` or `Control.BeginInvoke(...)` to bounce work back to the UI thread. `SynchronizationContext` abstracts this — it's a way for any environment to say *"here's how you send work to the right place."*

```csharp
// The base implementation — just queues to the ThreadPool
// (simplified for clarity — the actual runtime implementation is more memory-optimized
public class SynchronizationContext
{
    public virtual void Post(SendOrPostCallback d, object? state)
        => ThreadPool.QueueUserWorkItem(static s => s.d(s.state), (d, state));

    public virtual void Send(SendOrPostCallback d, object? state)
        => d(state); // synchronous by default
}
```

Each app model provides its own derived implementation:
- **WinForms**: `WindowsFormsSynchronizationContext` → sends work to the UI thread via `Control.BeginInvoke`
- **WPF**: `DispatcherSynchronizationContext` → sends work to the `Dispatcher` thread
- **Legacy ASP.NET**: `AspNetSynchronizationContext` → one-at-a-time per request
- **xUnit**: `MaxConcurrencySyncContext` → limits parallel test execution
- **Console apps / ASP.NET Core**: **null** — no context at all

*(Ref: Stephen Toub — "ConfigureAwait FAQ", Vasil Kosturski — "Exploring the async/await State Machine – Synchronization Context")*

---

## Slide 5: SynchronizationContext — How await Uses It

**When you `await` an incomplete task, the runtime captures `SynchronizationContext.Current` so it knows where to run the rest of your method.**

The runtime's actual capture logic (from `dotnet/runtime` source) looks like this:

```csharp
// Simplified from the actual CaptureContinuationContext method:
SynchronizationContext? syncCtx = Thread.CurrentThread._synchronizationContext;

if (syncCtx != null && syncCtx.GetType() != typeof(SynchronizationContext))
{
    // A real, derived SynchronizationContext exists — capture it.
    // The continuation will be Posted to this context.
}
else
{
    // No meaningful SynchronizationContext.
    // Check if there's a custom TaskScheduler active.
    TaskScheduler? scheduler = TaskScheduler.InternalCurrent; // can be null
    if (scheduler != null && scheduler != TaskScheduler.Default)
    {
        // Use the custom TaskScheduler
    }
    else
    {
        // Default: schedule continuation on the ThreadPool
    }
}
```

Notice the `GetType() != typeof(SynchronizationContext)` check — the runtime ignores the *base* `SynchronizationContext` because it would just queue to the ThreadPool anyway (same as having no context). Only *derived* implementations are meaningful.

This is why your WPF button click handler "just works" — the continuation is posted back to the UI thread via `DispatcherSynchronizationContext.Post`.

*(Ref: Vasil Kosturski — "Exploring the async/await State Machine – Concrete Implementation", Stephen Toub — "How Async/Await Really Works", dotnet/runtime AsyncHelpers.CoreCLR.cs)*

---

## Slide 6: SynchronizationContext in WPF/WinForms — Demo

**UI thread affinity: why you can update controls after `await`**

```csharp
// WPF Example
private async void Button_Click(object sender, RoutedEventArgs e)
{
    // Running on UI thread. SynchronizationContext.Current
    // is DispatcherSynchronizationContext
    StatusLabel.Text = "Loading...";

    var data = await httpClient.GetStringAsync("https://api.example.com");

    // After await, continuation is Posted back to the UI thread
    // via DispatcherSynchronizationContext.Post → Dispatcher.BeginInvoke
    StatusLabel.Text = data; // ✅ Safe — we're on the UI thread
}
```

```csharp
// WinForms — identical behavior
private async void Button1_Click(object sender, EventArgs e)
{
    // SynchronizationContext.Current is WindowsFormsSynchronizationContext
    textBox1.Text = "Loading...";

    var json = await GetJsonAsync(uri);

    textBox1.Text = json; // ✅ Safe — continuation ran on UI thread
}
```

**Without** SynchronizationContext, updating `StatusLabel.Text` after `await` would throw a cross-thread exception. The whole point of this machinery is to make async code feel natural in environments with thread affinity.

---

## Slide 7: No SynchronizationContext in ASP.NET Core

**Why does ASP.NET Core not have one?**

Legacy ASP.NET had `AspNetSynchronizationContext` which did a lot of work:
- Ensured one-at-a-time execution of continuations per request
- Re-entered the request context (setting `HttpContext.Current`, thread identity, culture)
- Created overhead: queueing, context switching, housekeeping

But unlike a UI app, there's no "special thread" in a web server. Any thread pool thread can handle any request. So the concept of "posting back to the right thread" doesn't apply.

ASP.NET Core dropped it for performance and simplicity:
- Continuations run on **any thread pool thread**
- Continuations can run **in parallel** within the same request
- No "re-entering" overhead
- `HttpContext` is accessed via DI (`IHttpContextAccessor`), not thread-static

**Practical consequences:**
- **No deadlocks** from blocking on async code (`.Result`, `.Wait()`) — though you still shouldn't do it
- **No need** for `ConfigureAwait(false)` in ASP.NET Core application code
- Code that mutates shared state within a request needs explicit synchronization

```csharp
// ASP.NET Core controller — no SynchronizationContext
[HttpGet]
public async Task<IActionResult> Get()
{
    // SynchronizationContext.Current is null here
    var data = await _service.GetDataAsync();
    // Continuation runs on any ThreadPool thread — totally fine
    return Ok(data);
}
```

*(Ref: Stephen Cleary — "ASP.NET Core SynchronizationContext")*

---

## Slide 8: ConfigureAwait(false) — What It Does

**Why does this exist?**

If you're writing library code — say, an HTTP helper or a JSON parser — you don't know who's calling you. Maybe it's a WPF app, maybe it's a console app. If a WPF app calls your library and you `await` something, the runtime will try to post your continuation back to the UI thread. But your library doesn't need the UI thread! It's just processing data. By forcing the continuation onto the UI thread, you're wasting time and potentially causing deadlocks.

`ConfigureAwait(false)` says: *"I don't need to resume where I started. Just use the ThreadPool."*

```csharp
// ConfigureAwait returns a ConfiguredTaskAwaitable — a different awaitable type
// whose awaiter passes continueOnCapturedContext: false to the internal scheduling logic,
// so context capture is skipped.
await task.ConfigureAwait(false);
```

What happens internally:
- The awaiter receives `continueOnCapturedContext: false`
- It tells the scheduling infrastructure not to capture `SynchronizationContext.Current`
- Continuation is scheduled on the **ThreadPool** instead of the original context
- If the task is already completed, `ConfigureAwait(false)` has **no effect** — execution continues synchronously on the same thread

```csharp
// Library code — use ConfigureAwait(false) everywhere
public async Task<string> GetDataAsync()
{
    var raw = await _httpClient.GetStringAsync(url)
        .ConfigureAwait(false);
    // Now running on ThreadPool thread, NOT on caller's context

    var processed = await ProcessAsync(raw)
        .ConfigureAwait(false);
    // Still on ThreadPool — good for library code

    return processed;
}
```

*(Ref: Stephen Toub — "ConfigureAwait FAQ")*

---

## Slide 9: ConfigureAwait — When and Where to Use It

**The decision matrix:**

**Application-level code (WPF, WinForms):**
→ Do NOT use `ConfigureAwait(false)` — you need the UI context to update controls

**Application-level code (ASP.NET Core):**
→ Not needed — there's no SynchronizationContext to capture anyway. Using it is harmless but unnecessary.

**Library code (NuGet packages, shared DLLs):**
→ USE `ConfigureAwait(false)` on **every** `await` — your library doesn't know if callers have a context

```csharp
// ❌ WPF — DON'T use ConfigureAwait(false) if you touch UI after
private async void Button_Click(object sender, RoutedEventArgs e)
{
    var data = await GetDataAsync().ConfigureAwait(false);
    StatusLabel.Text = data; // 💥 Cross-thread exception!
}

// ✅ Library code — always use ConfigureAwait(false)
public async Task<byte[]> DownloadAsync(string url)
{
    var response = await _client.GetAsync(url).ConfigureAwait(false);
    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
}
```

**Key pitfall**: Using `ConfigureAwait(false)` only on the *first* `await` is NOT sufficient. If that task completes synchronously, the context is still present for subsequent awaits. You must use it on **every** `await`.

**Modern alternative (.NET 8+)**: `ConfigureAwait(ConfigureAwaitOptions.None)` is equivalent to `ConfigureAwait(false)` but is more self-documenting. The `ConfigureAwaitOptions` enum also provides `ForceYielding` (always yield, even if the task is already complete) and `SuppressThrowing` (don't throw on cancellation/fault).

*(Ref: Stephen Toub — "ConfigureAwait FAQ", Stephen Cleary — "Async and Await")*

---

## Slide 10: ConfigureAwait — The Classic Deadlock

**Mixing sync and async code with a SynchronizationContext causes deadlocks.**

This is the single most common async bug. Here's how it happens:

```csharp
// Library method
public static async Task<JObject> GetJsonAsync(Uri uri)
{
    using var client = new HttpClient();
    var json = await client.GetStringAsync(uri); // captures UI context
    return JObject.Parse(json);
}

// WinForms — DEADLOCK! 💀
public void Button1_Click(object sender, EventArgs e)
{
    var jsonTask = GetJsonAsync(uri);
    textBox1.Text = jsonTask.Result; // ← blocks the UI thread
    // GetJsonAsync's continuation needs the UI thread to resume
    // but the UI thread is blocked waiting for the task ⇒ DEADLOCK
}
```

**What's happening step by step:**
1. `GetJsonAsync` starts `GetStringAsync` and captures the UI `SynchronizationContext`
2. `.Result` blocks the UI thread, waiting for the task to complete
3. `GetStringAsync` finishes on a background thread and tries to Post the continuation to the UI thread
4. The UI thread is blocked by `.Result` — it can't process the Posted continuation
5. **Deadlock**: the task can't complete because it needs the UI thread, and the UI thread can't unblock because it's waiting for the task

**Two solutions:**
1. **Don't block**: use `async` all the way down (`await` instead of `.Result`)
2. **ConfigureAwait(false)** in the library: continuation doesn't need the UI thread

**In ASP.NET Core**, this deadlock doesn't happen because there's no SynchronizationContext. But **don't rely on this** — use `async` all the way.

*(Ref: Stephen Cleary — "Don't Block on Async Code")*

---

## Slide 11: ExecutionContext — The Invisible Flow

**Why does this exist?**

Imagine you set a security principal on your thread, or you store a correlation ID for logging. Then you `await` something and your continuation resumes on a totally different thread. If those ambient values didn't travel with you, you'd lose your security identity or your logging context mid-request. That would be a disaster.

`ExecutionContext` solves this: it carries ambient data across async boundaries — automatically.

Unlike SynchronizationContext (which determines *where* continuations run), ExecutionContext carries *what data* flows with them. It includes security context, `AsyncLocal<T>` values, and more.

```csharp
// ExecutionContext ALWAYS flows across awaits — this is non-negotiable
static AsyncLocal<int> _requestId = new AsyncLocal<int>();

static async Task ProcessAsync()
{
    _requestId.Value = 42;

    await Task.Delay(100);
    // ExecutionContext was captured before suspension and restored here
    // _requestId.Value is still 42 ✅ — even if we're on a different thread

    Console.WriteLine(_requestId.Value); // 42
}
```

Key facts:
- `ExecutionContext` flows via `Task.Run`, `ThreadPool.QueueUserWorkItem`, and across `await`
- `ConfigureAwait(false)` does **NOT** suppress ExecutionContext flow — it only affects SynchronizationContext
- The `AsyncMethodBuilderCore.Start` method explicitly saves and restores both `ExecutionContext` and `SynchronizationContext` around the initial `MoveNext()` call, preventing context changes in the synchronous part of your method from leaking to the caller

*(Ref: Sergey Tepliakov — "Dissecting the async methods in C#", Stephen Toub — "ExecutionContext vs SynchronizationContext")*

---

## Slide 12: AsyncLocal\<T\> — Thread-Local Storage for Async

**Why does this exist?**

`ThreadLocal<T>` gives you per-thread storage. But in async code, your method can hop between threads at every `await`. A value stored in `ThreadLocal` would be lost. `AsyncLocal<T>` gives you storage that flows with the *logical call chain*, not the physical thread.

**`AsyncLocal<T>` stores data that flows "down" through async calls, but NOT "up."**

```csharp
static AsyncLocal<string> _traceId = new AsyncLocal<string>();

static async Task Main()
{
    _traceId.Value = "REQ-001";
    Console.WriteLine($"Main before: {_traceId.Value}");   // REQ-001

    await ChildAsync();

    Console.WriteLine($"Main after: {_traceId.Value}");    // REQ-001 ✅ (not REQ-002!)
}

static async Task ChildAsync()
{
    Console.WriteLine($"Child before: {_traceId.Value}");  // REQ-001 (flowed down)
    _traceId.Value = "REQ-002";  // only affects this scope and below
    Console.WriteLine($"Child after: {_traceId.Value}");   // REQ-002

    await Task.Yield();
    Console.WriteLine($"Child resumed: {_traceId.Value}"); // REQ-002
}
```

**How does the isolation work?** The `AsyncMethodBuilderCore.Start` method saves the current `ExecutionContext` before calling `MoveNext()` and restores it afterwards. Meanwhile, setting an `AsyncLocal` value always creates a **new** `ExecutionContext` instance (because `ExecutionContext` is immutable — every mutation produces a new copy). So when the child method modifies an `AsyncLocal`, it gets its own copy of the context, leaving the parent's reference unchanged.

**Real-world uses**: distributed tracing (Activity/OpenTelemetry), scoped DI containers, logging correlation IDs, culture propagation.

*(Ref: Stephen Cleary — "Implicit Async Context ('AsyncLocal')")*

---

## Slide 13: AsyncLocal\<T\> — The Synchronous Trap

**Synchronous methods do NOT get the save/restore treatment.**

The copy-on-write isolation only kicks in for methods that go through the `AsyncMethodBuilder` infrastructure (i.e., `async` methods). Plain synchronous methods run on the caller's `ExecutionContext` directly.

```csharp
static AsyncLocal<int> _ctx = new AsyncLocal<int>();

static async Task Main()
{
    _ctx.Value = 1;
    SyncMethod();  // ⚠️ This modifies the SAME context!
    Console.WriteLine(_ctx.Value); // 999 — NOT 1!

    await AsyncMethod();
    Console.WriteLine(_ctx.Value); // 1 — async restored properly
}

static void SyncMethod()
{
    _ctx.Value = 999; // 💥 Leaks to caller! No save/restore wrapper!
}

static async Task AsyncMethod()
{
    _ctx.Value = 999; // ✅ Isolated — caller won't see this
    await Task.Yield();
}
```

This is because `AsyncMethodBuilderCore.Start` wraps the `MoveNext()` call in a try/finally that restores the previous `ExecutionContext`. A synchronous method has no such wrapper — it directly mutates the same `ExecutionContext` instance.

**Rule of thumb**: If a method modifies `AsyncLocal` values, make sure it's `async` — or callers may see unexpected mutations.

*(Ref: Stephen Cleary — "Eliding Async and Await")*

---

## Slide 14: ExecutionContext vs SynchronizationContext — Summary

| Aspect                    | ExecutionContext                       | SynchronizationContext                 |
|---------------------------|----------------------------------------|----------------------------------------|
| **Purpose**               | Carries ambient data (security, AsyncLocal) | Determines *where* continuations run  |
| **Flows across `await`?** | Always                                 | Only if `ConfigureAwait(true)` (default) |
| **Affected by `ConfigureAwait(false)`?** | No            | Yes — context is not captured          |
| **Who manages it?**       | `AsyncMethodBuilderCore.Start` (save/restore) + `AsyncStateMachineBox` (capture/flow) | Awaiter infrastructure (`CaptureContinuationContext`) |
| **Can suppress flow?**    | Only via `ExecutionContext.SuppressFlow()` (rare, dangerous) | Yes — `ConfigureAwait(false)` |

```csharp
static AsyncLocal<string> _userId = new AsyncLocal<string>();

static async Task DemoAsync()
{
    _userId.Value = "alice";
    SynchronizationContext.SetSynchronizationContext(new MyContext());

    await Task.Delay(100).ConfigureAwait(false);

    // SynchronizationContext.Current is now null (not captured)
    // But _userId.Value is still "alice" (ExecutionContext always flows)
}
```

Think of it this way: **ExecutionContext** is your luggage — it always travels with you. **SynchronizationContext** is your return ticket — `ConfigureAwait(false)` throws it away.

*(Ref: Sergey Tepliakov — "Dissecting the async methods in C#", Stephen Toub — "ConfigureAwait FAQ")*

---

## Slide 15: TaskScheduler — The Other Context

**Why does this exist?**

Sometimes you need more control than "run it on the ThreadPool." Maybe you want to limit concurrency to N tasks at a time, or you need to run tasks on a specific thread. `TaskScheduler` lets you plug in custom scheduling strategies.

Most of the time, you'll never need to think about it. But `await` *does* check for it.

**When `SynchronizationContext.Current` is null (or is the base type), the async infrastructure falls back to `TaskScheduler.Current`.**

```csharp
// Pseudocode of the capture logic (matches actual runtime behavior):
SynchronizationContext syncCtx = SynchronizationContext.Current;
if (syncCtx != null && syncCtx.GetType() != typeof(SynchronizationContext))
{
    // Use SynchronizationContext
}
else
{
    TaskScheduler scheduler = TaskScheduler.InternalCurrent; // can be null
    if (scheduler != null && scheduler != TaskScheduler.Default)
    {
        // Use custom TaskScheduler
    }
    else
    {
        // Default: schedule on ThreadPool
    }
}
```

Built-in schedulers:
- `TaskScheduler.Default` — schedules to ThreadPool (most common)
- `ConcurrentExclusiveSchedulerPair` — provides reader/writer-like scheduling without locks

```csharp
// Custom scheduler for concurrency control
var pair = new ConcurrentExclusiveSchedulerPair();
var exclusive = pair.ExclusiveScheduler; // Only 1 task at a time
var concurrent = pair.ConcurrentScheduler; // Many tasks, but not while exclusive runs

await Task.Factory.StartNew(async () =>
{
    // TaskScheduler.Current is now 'exclusive'
    // AND it's sticky — async continuations also use it!
    await SomeAsync(); // continuation also runs on 'exclusive'
}, CancellationToken.None, TaskCreationOptions.None, exclusive).Unwrap();
```

*(Ref: Stephen Cleary — "Async and Scheduled Concurrency")*

---

## Slide 16: TaskScheduler — The Sticky Deadlock Trap

**Custom TaskSchedulers are "sticky" — they flow through `await` continuations.**

This can cause subtle deadlocks if the scheduler has limited threads:

```csharp
// A scheduler with only 2 dedicated threads
var scheduler = new DedicatedThreadsTaskScheduler(threadCount: 2);

// Schedule 3 async tasks on a 2-thread scheduler
var tasks = Enumerable.Range(0, 3).Select(_ =>
    Task.Factory.StartNew(async () =>
    {
        Console.WriteLine("Starting work...");
        await Task.Delay(1000);
        // ⚠️ This continuation also runs on the custom scheduler!
        // With only 2 threads and 3 tasks, the continuations
        // are waiting for a thread, but threads are occupied ⇒ DEADLOCK
        Console.WriteLine("Finished work.");
    }, CancellationToken.None, TaskCreationOptions.None, scheduler).Unwrap()
);

await Task.WhenAll(tasks); // 💀 Deadlock with 3+ tasks!
```

**Fix**: Use `ConfigureAwait(false)` to escape the custom scheduler:
```csharp
await Task.Delay(1000).ConfigureAwait(false);
// Now the continuation runs on the default ThreadPool, not the custom scheduler
```

**Better approach**: Prefer `SemaphoreSlim`, `Channel<T>`, or `Parallel.ForEachAsync` for concurrency throttling instead of custom TaskSchedulers — they're simpler and don't have the "sticky" problem.

*(Ref: Sergey Tepliakov — "You probably should stop using a custom TaskScheduler")*

---

## Slide 17: CancellationToken — Fundamentals

**Why does this exist?**

Imagine a user clicks "Search" and then immediately clicks "Cancel." Or a web request takes too long and the client disconnects. Without cancellation, the server keeps doing work that nobody cares about anymore — wasting CPU, memory, and database connections.

.NET uses **cooperative cancellation**: the caller signals *"I'd like you to stop,"* and the callee decides *when and how* to stop. Nobody yanks the rug out from under a running operation.

```csharp
// The pattern: CancellationTokenSource creates the signal,
// CancellationToken observes it
public async Task ProcessItemsAsync(CancellationToken cancellationToken)
{
    foreach (var item in items)
    {
        // Option 1: Poll — check if cancellation was requested
        cancellationToken.ThrowIfCancellationRequested();

        // Option 2: Pass to framework methods (they check internally)
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Option 3: Register a callback for cleanup
        cancellationToken.Register(() => _connection.Close());
    }
}
```

```csharp
// ASP.NET Core — the framework provides the token automatically
// It fires when the client disconnects (browser closed, timeout, etc.)
[HttpGet]
public async Task<IActionResult> Search(
    string query, CancellationToken cancellationToken) // ← injected by framework
{
    var results = await _searchService.SearchAsync(query, cancellationToken);
    return Ok(results);
}
```

**Best practice**: `CancellationToken` should be the **last parameter**, and **optional on public APIs** (with a default of `CancellationToken.None`), **required on internal methods**.

*(Ref: Microsoft DevBlogs — "Recommended patterns for CancellationToken")*

---

## Slide 18: CancellationToken — Linked Tokens & Timeouts

**What if you need to cancel for multiple reasons?**

Sometimes you want to cancel if the *caller* asks, OR if a *timeout* expires, OR if some other condition is met. `CreateLinkedTokenSource` combines multiple cancellation concerns into one.

```csharp
// Pattern: user cancellation + timeout
public async Task<ReportData> GenerateReportAsync(
    CancellationToken cancellationToken)
{
    // Linked source: cancels if EITHER the caller cancels OR timeout expires
    using var cts = CancellationTokenSource
        .CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(30));

    try
    {
        return await _generator.GenerateAsync(cts.Token);
    }
    catch (OperationCanceledException) when
        (!cancellationToken.IsCancellationRequested)
    {
        // It was OUR timeout, not the caller's cancellation
        throw new TimeoutException("Report generation timed out.");
    }
}
```

```csharp
// ASP.NET Core Minimal API — same pattern
app.MapPost("/upload", async (
    IFormFile file, CancellationToken cancellationToken) =>
{
    await _storage.UploadAsync(file, cancellationToken);

    // After side-effect completed: stop honoring cancellation
    await _notifier.NotifyAsync(CancellationToken.None);
    return Results.Ok();
});
```

**Key insight**: After side effects occur (file uploaded, email sent), switch to `CancellationToken.None` to avoid leaving inconsistent state. You don't want a "success" notification to be skipped just because the client disconnected after the upload finished.

*(Ref: Stephen Cleary — "Cancellation, Part 6: Linking")*

---

## Slide 19: CancellationToken — Exception Handling

**Always catch `OperationCanceledException`, not `TaskCanceledException`.**

This is a common gotcha: `TaskCanceledException` *derives from* `OperationCanceledException`. If you only catch `TaskCanceledException`, you'll miss cancellations from other sources (like `CancellationToken.ThrowIfCancellationRequested()` which throws `OperationCanceledException` directly).

```csharp
public async Task ExecuteWithRetryAsync(CancellationToken ct)
{
    try
    {
        await _httpClient.SendAsync(request, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // ✅ Clean cancellation — the caller asked us to stop
        _logger.LogInformation("Operation cancelled by caller");
        throw; // re-throw to propagate cancellation
    }
    catch (OperationCanceledException)
    {
        // ⚠️ Not our token — could be HttpClient's internal timeout
        _logger.LogWarning("Operation cancelled unexpectedly (timeout?)");
        throw;
    }
}
```

**Rules:**
1. Catch `OperationCanceledException`, not `TaskCanceledException`
2. Check `cancellationToken.IsCancellationRequested` to distinguish *your* cancellation from internal ones
3. When re-throwing in a catch block, use `throw;` — never `throw ex;` (preserves stack trace)
4. Don't throw `OperationCanceledException` after work is complete — return success instead

*(Ref: Microsoft DevBlogs — "Recommended patterns for CancellationToken")*

---

## Slide 20: Eliding async/await — When to Omit the Keywords

**Why would you ever omit them?**

Each `async` method creates a state machine. For trivial one-line passthroughs, you're paying for an allocation and state machine setup just to forward a call. By "eliding" (omitting) `async/await`, you return the inner task directly.

```csharp
// ✅ Safe to elide: trivial passthrough with no other logic
Task<string> GetNameAsync(int id)
    => _repository.GetNameAsync(id);

// ✅ Safe to elide: simple overload delegation
Task<string> GetNameAsync()
    => GetNameAsync(CancellationToken.None);
```

```csharp
// ❌ DANGEROUS to elide: using statement
Task<string> GetDataAsync()
{
    using var client = new HttpClient();
    return client.GetStringAsync("https://api.example.com");
    // 💥 HttpClient is disposed BEFORE the request completes!
    // The method returns immediately, the using block runs Dispose(),
    // but the HTTP request is still in flight!
}

// ✅ Fixed — keep async/await
async Task<string> GetDataAsync()
{
    using var client = new HttpClient();
    return await client.GetStringAsync("https://api.example.com");
    // HttpClient is disposed AFTER the request completes ✅
}
```

*(Ref: Stephen Cleary — "Eliding Async and Await")*

---

## Slide 21: Eliding async/await — The Hidden Pitfalls

**Three major dangers when eliding async/await:**

**1. Exception semantics change:**
```csharp
// With async/await — exception is placed on the Task
async Task<int> DivideAsync(int a, int b)
{
    return a / b; // DivideByZeroException is stored on the Task
}

// Without async — exception is thrown synchronously to caller
Task<int> DivideElided(int a, int b)
{
    return Task.FromResult(a / b);
    // 💥 DivideByZeroException is thrown HERE, before a Task is even created
    // The caller can't catch it with try/await — it blows up immediately
}
```

**2. AsyncLocal isolation is lost** (see Slide 13):
```csharp
// Synchronous methods don't get the AsyncMethodBuilder's
// save/restore wrapper. Modifying AsyncLocal values leaks to the caller.
```

**3. Stack traces become harder to read:**
Elided methods don't appear in async stack traces, making debugging harder.

**Recommendation**: Keep `async`/`await` by default. Only elide for trivial single-line passthroughs with no try/catch, no using, and no AsyncLocal mutations. The performance gain (one fewer state machine) is almost never meaningful for I/O-bound code.

*(Ref: Stephen Cleary — "Eliding Async and Await", Sergey Tepliakov — "The performance characteristics of async methods in C#")*

---

## Slide 22: async void — The Danger Zone

**Why does this exist?**

Event handlers in UI frameworks (like `Button.Click`) have a fixed signature: `void MethodName(object sender, EventArgs e)`. They can't return `Task`. So `async void` exists as a special case to allow `await` inside event handlers. **That's the only legitimate use.**

**`async void` methods are fire-and-forget with no way to observe errors from outside.**

```csharp
// ❌ async void — exception can crash the process!
async void DoWorkAsync()
{
    await Task.Delay(100);
    throw new Exception("Oops");
    // 💥 Where does this exception go?
}

// ✅ async Task — exception is captured on the Task
async Task DoWorkAsync()
{
    await Task.Delay(100);
    throw new Exception("Oops"); // Stored on the Task, caller can observe
}
```

**What actually happens with `async void` exceptions** (from the runtime source):

The `AsyncVoidMethodBuilder` captures the current `SynchronizationContext` when the method starts (via `Create()`). When an exception occurs:
- **If a `SynchronizationContext` was captured**: the exception is **Posted** to that context (e.g., the UI thread in WPF), where it becomes an unhandled exception and typically shows an error dialog.
- **If no `SynchronizationContext` exists** (Console apps, ASP.NET Core): the exception is thrown on the **ThreadPool**, which crashes the process.

Additionally, `async void` calls `SynchronizationContext.OperationStarted()` when the method begins and `OperationCompleted()` when it finishes — this is how UI frameworks track outstanding async operations.

```csharp
// ✅ This is the ONE valid use of async void — UI event handlers
private async void Button_Click(object sender, RoutedEventArgs e)
{
    try
    {
        await DoWorkAsync(); // delegate to async Task method
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.Message); // handle errors here
    }
}
```

*(Ref: Sergey Tepliakov — "The Dangers of Async Void", Stephen Cleary — "Async and Await", dotnet/runtime AsyncVoidMethodBuilder.cs)*

---

## Slide 23: Task.Run vs Task.Factory.StartNew

**Why does this matter?**

`Task.Factory.StartNew` predates `async/await` and was designed for CPU-bound delegates. When you pass an `async` lambda to it, things break in confusing ways.

**Prefer `Task.Run` for async delegates. `Task.Factory.StartNew` has subtle bugs.**

```csharp
// ❌ Task.Factory.StartNew with async — returns Task<Task>!
var task = Task.Factory.StartNew(async () =>
{
    await Task.Delay(1000);
    throw new Exception("Error");
});
// task.Status is RanToCompletion even though the inner task failed!
// task is Task<Task> — the outer completed (it started the async lambda),
// but the inner task (the actual work) is lost

// ✅ Fix with Unwrap — flattens Task<Task> into Task
var task = Task.Factory.StartNew(async () =>
{
    await Task.Delay(1000);
    throw new Exception("Error");
}).Unwrap();

// ✅ Better — just use Task.Run (handles unwrapping automatically)
var task = Task.Run(async () =>
{
    await Task.Delay(1000);
    throw new Exception("Error");
});
```

**`Task.Factory.StartNew` pitfall with `LongRunning`**: The `LongRunning` flag creates a dedicated thread, but only for the synchronous portion before the first `await`. After that, the dedicated thread is released and the continuation runs on the ThreadPool anyway — making `LongRunning` pointless for async delegates.

*(Ref: Sergey Tepliakov — "The Dangers of Task.Factory.StartNew")*

---

## Slide 24: ValueTask — When and Why

**Why does this exist?**

`Task<T>` is a class — every time an async method returns one, it allocates an object on the heap. For methods that *often complete synchronously* (like reading from a cache), you're paying for a heap allocation you don't need. `ValueTask<T>` is a struct that can wrap either a raw `TResult` value (no allocation!) or a `Task<T>` (for the truly async path).

```csharp
// If the cache hits, we return synchronously — no Task allocation
public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
        return new ValueTask<User>(user); // No allocation!

    return new ValueTask<User>(GetUserFromDbAsync(id)); // Wraps the Task
}
```

`ValueTask<T>` can also be backed by an `IValueTaskSource<T>`, which enables **pooling** of the underlying async operation objects. This is the real performance win used internally by high-throughput types like `Socket` and `PipeReader`.

**Rules for consuming `ValueTask<T>` — stricter than `Task<T>`:**
```csharp
// ✅ Await it once
var user = await GetUserAsync(1);

// ❌ NEVER await it twice
var vt = GetUserAsync(1);
var a = await vt;
var b = await vt; // 💥 Undefined behavior!

// ❌ NEVER use .Result or .GetAwaiter().GetResult() unless IsCompleted is true
// ❌ NEVER use WhenAll/WhenAny with ValueTask — convert to Task first
var tasks = ids.Select(id => GetUserAsync(id).AsTask());
await Task.WhenAll(tasks);
```

*(Ref: Sergey Tepliakov — "The performance characteristics of async methods in C#", Stephen Toub — .NET performance blogs)*

---

## Slide 25: Putting It All Together — Decision Flowchart

**Quick reference for your daily async work:**

**"Should I use `ConfigureAwait(false)`?"**
- Writing library code? → Yes, on every `await`
- Writing ASP.NET Core application code? → Not needed (no SynchronizationContext)
- Writing WPF/WinForms code that touches UI after? → No
- Writing WPF/WinForms code in a non-UI helper method? → Yes

**"Should I elide async/await?"**
- Is it a trivial one-line passthrough? → OK to elide
- Does the method have `using`, `try/catch`, or multiple statements? → Keep async/await
- Does the method modify `AsyncLocal` values? → Keep async/await
- Are you unsure? → Keep async/await

**"Should I use CancellationToken?"**
- Is the operation I/O-bound or long-running? → Yes, always propagate
- Have I already committed side effects? → Switch to `CancellationToken.None`
- Am I writing a public API? → Make it optional (default `CancellationToken.None`)
- Am I writing an internal method? → Make it required

**"Should I use `async void`?"**
- Is this a UI event handler? → OK, but wrap body in try/catch
- Anything else? → No. Return `Task`.

**"Should I use `ValueTask<T>` instead of `Task<T>`?"**
- Does the method frequently complete synchronously (caching, buffering)? → Consider `ValueTask<T>`
- Are consumers going to await it exactly once? → `ValueTask<T>` is fine
- Do consumers need `WhenAll`, `WhenAny`, or multiple awaits? → Stick with `Task<T>`

---

## Slide 26: References & Further Reading

**Stephen Toub (Microsoft .NET Team)**
- "ConfigureAwait FAQ" — devblogs.microsoft.com/dotnet/configureawait-faq
- "How Async/Await Really Works in C#" — devblogs.microsoft.com/dotnet/how-async-await-really-works

**Stephen Cleary (Microsoft MVP)**
- "Don't Block on Async Code" — blog.stephencleary.com/2012/07/dont-block-on-async-code
- "Eliding Async and Await" — blog.stephencleary.com/2016/12/eliding-async-await
- "ASP.NET Core SynchronizationContext" — blog.stephencleary.com/2017/03/aspnetcore-synchronization-context
- "Cancellation, Part 6: Linking" — blog.stephencleary.com/2024/10/cancellation-6-linking

**Sergey Tepliakov (Dissecting the Code)**
- "Dissecting the async methods in C#" — devblogs.microsoft.com/premier-developer/dissecting-the-async-methods-in-c
- "You probably should stop using a custom TaskScheduler" — sergeyteplyakov.github.io
- "The Dangers of Async Void" — sergeyteplyakov.github.io

**Vasil Kosturski**
- "Exploring the async/await State Machine" (full series) — vkontech.com

**Microsoft Learn**
- "Async/Await Best Practices" — learn.microsoft.com
- "Recommended patterns for CancellationToken" — devblogs.microsoft.com/premier-developer

**Runtime source code (for the curious)**
- `SynchronizationContext.cs` — github.com/dotnet/runtime
- `AsyncMethodBuilderCore.cs` — github.com/dotnet/runtime
- `AsyncVoidMethodBuilder.cs` — github.com/dotnet/runtime
- `AsyncHelpers.CoreCLR.cs` — github.com/dotnet/runtime
- `ExecutionContext.cs` — github.com/dotnet/runtime