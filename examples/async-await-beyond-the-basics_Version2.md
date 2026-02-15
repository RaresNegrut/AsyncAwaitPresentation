# Async & Await - Beyond the Basics
### A Deep Dive for .NET Developers

---

## Slide 1: Title Slide

**Async & Await - Beyond the Basics**

*Going deeper than "just put await in front of it"*

Topics: The evolution of async in .NET, Tasks, Awaitables & Awaiters, the State Machine, SynchronizationContext, ExecutionContext, ConfigureAwait, CancellationTokens, TaskScheduler, AsyncLocal, Eliding async/await

Resources: Stephen Toub (Microsoft DevBlogs), Stephen Cleary (blog.stephencleary.com), Sergey Tepliakov (Dissecting the Code), Vasil Kosturski (vkontech.com), Microsoft Learn, Jon Skeet (eduasync)

---

## Slide 2: Motivation - Why This Talk?

**We all `await` async methods... but do we really understand them?**

The reality in most codebases:
- We `await` because "we must" or "the analyzer told us to"
- We sprinkle `ConfigureAwait(false)` like a superstition
- We copy-paste CancellationToken parameters without understanding propagation
- We have no idea what happens when our continuation resumes on a different thread

**But here's the thing:** `async/await` is not magic - it's a well-designed collaboration between your code, the compiler, and the runtime. Every piece exists for a reason.

This talk aims to build that understanding from the ground up:
1. First, we'll see **why** async/await was needed - by looking at how we used Tasks before the keywords existed
2. Then, we'll learn what a **Task** actually is and what makes it tick
3. Then, we'll discover the **awaitable/awaiter pattern** - the contract that makes `await` work
4. Then, we'll peek inside the **state machine** the compiler generates
5. Finally, we'll explore the advanced topics: **SynchronizationContext**, **ExecutionContext**, **ConfigureAwait**, **CancellationTokens**, and more

---

## Slide 3: The World Before async/await - Writing Async Code with Tasks

**Why does async/await exist? Because writing async code without it was painful.**

When the Task Parallel Library (TPL) was introduced in .NET 4.0(~2010), we got `Task` and `Task<T>` - objects that represent an ongoing operation. To chain work after a task completed, we used `ContinueWith`. Let's see what that looked like with a concrete example: **copying data from one stream to another** (adapted from Stephen Toub's "How Async/Await Really Works in C#").

**The synchronous version** - simple, readable, blocking:

```csharp
// Synchronous - blocks the calling thread during every Read and Write
void CopyStreamToStream(Stream source, Stream destination)
{
    var buffer = new byte[0x1000];
    int numRead;
    while ((numRead = source.Read(buffer, 0, buffer.Length)) != 0)
    {
        destination.Write(buffer, 0, numRead);
    }
}
```

This is clean, but it blocks the thread. On a UI app, the screen freezes. On a server, you're wasting a thread that could serve other requests.

### The TPL way: Task + ContinueWith (.NET 4.0)

The Task Parallel Library gave us a **single object representing an ongoing operation**. You could chain work using `ContinueWith`:

```csharp
// Task + ContinueWith - non-blocking, but verbose and error-prone
Task CopyStreamToStream(Stream source, Stream destination)
{
    var buffer = new byte[0x1000];

    Task ReadThenWrite()
    {
        return source.ReadAsync(buffer, 0, buffer.Length)
            .ContinueWith(readTask =>
            {
                if (readTask.IsFaulted)
                    throw readTask.Exception.InnerException;

                int numRead = readTask.Result;
                if (numRead == 0)
                    return Task.CompletedTask; // Done - nothing left to read

                return destination.WriteAsync(buffer, 0, numRead)
                    .ContinueWith(writeTask =>
                    {
                        if (writeTask.IsFaulted)
                            throw writeTask.Exception.InnerException;

                        // Recurse to read the next chunk
                        return ReadThenWrite();
                    }).Unwrap();
            }).Unwrap();
    }

    return ReadThenWrite();
}
```

Look at this code - it works, but it's hard to follow:
- **Nested lambdas** that grow deeper with each step
- **Manual error checking** with `IsFaulted` and `Exception.InnerException`
- **`.Unwrap()`** needed because `ContinueWith` on an async lambda returns `Task<Task>`
- **No natural loops** - we had to use recursion to simulate `while`
- **Exception stack traces** are mangled across continuations
- **`using` and `try/catch`** don't work naturally across continuations

### The async/await way - C# 5.0 (.NET 4.5 - cca 2012)

Now look at the same logic with `async/await`:

```csharp
// async/await - reads like the synchronous version, but doesn't block
async Task CopyStreamToStream(Stream source, Stream destination)
{
    var buffer = new byte[0x1000];
    int numRead;
    while ((numRead = await source.ReadAsync(buffer, 0, buffer.Length)) != 0)
    {
        await destination.WriteAsync(buffer, 0, numRead);
    }
}
```

**This is almost identical to the synchronous version.** You can use `while`, `using`, `try/catch`, `foreach` - all the normal control flow. The compiler does the hard work of turning this into efficient continuation-based code. That's the entire point of `async/await`.

*(Ref: Stephen Toub - "How Async/Await Really Works in C#", devblogs.microsoft.com/dotnet/how-async-await-really-works)*

---

## Slide 4: What Is a Task?

**Before we can understand `await`, we need to understand what we're awaiting.**

A `Task` (or `Task<T>`) represents **a promise that some work will complete in the future** - and it gives you ways to observe when it does.

Think of a `Task` as a receipt you get when you order food. The food isn't ready yet, but the receipt lets you:
- **Check** if it's ready (`IsCompleted`, `Status`)
- **Wait** for it (`Wait()`, `.Result` - these block!)
- **Attach a callback/continuation** for when it's ready (`ContinueWith`)
- **Get the result** when it's done (`Result` for `Task<T>`)
- **See if something went wrong** (`IsFaulted`, `Exception`)

```csharp
// Key properties of Task / Task<T>:
Task<string> task = httpClient.GetStringAsync("https://example.com");

task.IsCompleted      // false initially, true when done
task.IsCompletedSuccessfully // true only if no error and not canceled
task.IsFaulted        // true if an exception occurred
task.IsCanceled       // true if the operation was canceled
task.Status           // enum: Created, Running, RanToCompletion, Faulted, Canceled...
task.Exception        // AggregateException wrapping any errors
task.Result           // the TResult value (⚠️ blocks if not completed!)
```

**A Task can be in one of three terminal states:**
1. **RanToCompletion** - success (and `Result` is available for `Task<T>`)
2. **Faulted** - an exception occurred (stored in `task.Exception`)
3. **Canceled** - the operation was canceled via a `CancellationToken`

**Why does this matter for `await`?** When you `await` a task:
- If the task completed successfully → you get the result
- If the task faulted → the exception is **re-thrown** at the `await` point, not wrapped in AggregateException
- If the task was canceled → an `OperationCanceledException` is thrown

This is why `await` is so much better than `.Result`
- it integrates naturally with `try/catch`.
- await does not block, and cannot cause deadlocks

*(Ref: Stephen Cleary - "A Tour of Task", Microsoft Learn - "Task-based asynchronous pattern")*

---

## Slide 5: The Awaitable & Awaiter Pattern - What Makes `await` Work

**You can `await` anything - as long as it follows a pattern.**

A common misconception is that `await` only works with `Task`. In reality, you can `await` **any type** that follows the **awaitable pattern**. This is a duck-typed contract - no interface required.

**To be awaitable**, a type needs one thing:
- A `GetAwaiter()` method (instance or extension) that returns an **awaiter**

**To be an awaiter**, the returned object must have:
1. `bool IsCompleted { get; }` - is the operation already done?
2. `void OnCompleted(Action continuation)` - call me back when you're done
3. `T GetResult()` - give me the result (or throw if faulted)

(Technically, the awaiter must also implement `INotifyCompletion`. High-performance awaiters implement `ICriticalNotifyCompletion` which has `UnsafeOnCompleted` - this skips `ExecutionContext` capture because the async infrastructure handles that separately.)

```csharp
// Here's what the pattern looks like concretely for Task<T>:

// Task<T> is the "awaitable" - it has GetAwaiter()
public TaskAwaiter<T> GetAwaiter();

// TaskAwaiter<T> is the "awaiter" - it has the three members
public struct TaskAwaiter<T> : ICriticalNotifyCompletion
{
    public bool IsCompleted { get; }              // 1. Are we done?
    public void OnCompleted(Action continuation); // 2. Call me when done
    public T GetResult();                         // 3. Give me the result
}
```

**This is the contract that makes the entire async ecosystem work.** The compiler doesn't care about `Task` specifically - it just needs these three members.

This is why you can `await`:
- `Task` and `Task<T>` (most common)
- `ValueTask` and `ValueTask<T>` (struct-based, less allocation)
- `ConfiguredTaskAwaitable` (returned by `ConfigureAwait`)
- Any custom type you create with a `GetAwaiter()` method

*(Ref: Stephen Toub - "Await Anything", devblogs.microsoft.com/pfxteam/await-anything)*

---

## Slide 6: What `await` Actually Does - Step by Step

**Now that we know the pattern, let's see how the compiler uses it.**

When you write:
```csharp
string result = await httpClient.GetStringAsync("https://example.com");
Console.WriteLine(result);
```

The compiler essentially transforms it into something like this:

```csharp
// Step 1: Get the awaiter from the awaitable
TaskAwaiter<string> awaiter = httpClient.GetStringAsync("https://example.com")
                                        .GetAwaiter();

// Step 2: Check if it's already complete (fast path)
if (awaiter.IsCompleted)
{
    // Already done! No need to suspend - just get the result
    goto GetResult;
}

// Step 3: Not complete - register a callback and SUSPEND
// "When you're done, call MoveNext() on my state machine to resume me"
awaiter.OnCompleted(stateMachine.MoveNext);
return; // ← Return control to the caller. The method is "paused."

// Step 4: When the task completes, we resume here
GetResult:
string result = awaiter.GetResult(); // Gets value or throws exception
Console.WriteLine(result);
```

**The key insights:**

1. **`IsCompleted` is checked first** - if the task already finished (common for cached results, small reads, etc.), we skip suspension entirely and continue synchronously. This is the "fast path."

2. **If not complete, we suspend** - `OnCompleted` registers our state machine's `MoveNext` as the callback. Then we **return** to the caller. The thread is free to do other work.

3. **When the task completes**, the callback fires, `MoveNext` is called, and we pick up right where we left off - at the `GetResult()` line.

4. **`GetResult()` does double duty** - it returns the value on success, or **re-throws the exception** on failure. This is why `await` integrates with `try/catch`.

**Everything after `await` is a continuation** - it's the code that runs after the awaited operation completes. The compiler packages it up so the awaiter can invoke it at the right time.

*(Ref: Stephen Toub - "How Async/Await Really Works in C#")*

---

## Slide 7: The State Machine - What the Compiler Generates

**The compiler transforms your `async` method into a state machine struct.**

Let's trace through a simple example:

```csharp
async Task<string> GetDataAsync()
{
    var result = await httpClient.GetStringAsync("https://api.example.com");
    return result.ToUpper();
}
```

The compiler generates (simplified - real output has more plumbing):

```csharp
// The method you wrote becomes a stub that creates and starts the state machine
Task<string> GetDataAsync()
{
    var stateMachine = new GetDataAsync_StateMachine();
    stateMachine._builder = AsyncTaskMethodBuilder<string>.Create();
    stateMachine._state = -1; // "not started"
    stateMachine._builder.Start(ref stateMachine); // calls MoveNext()
    return stateMachine._builder.Task; // return the Task to the caller
}

struct GetDataAsync_StateMachine : IAsyncStateMachine
{
    public int _state;
    public AsyncTaskMethodBuilder<string> _builder;
    private TaskAwaiter<string> _awaiter;

    // Local variables from your method become fields
    private string _result;

    public void MoveNext()
    {
        try
        {
            if (_state == -1) // Initial entry
            {
                // Your code BEFORE the first await
                _awaiter = httpClient.GetStringAsync("https://api.example.com")
                                     .GetAwaiter();

                if (_awaiter.IsCompleted)
                {
                    goto State0; // Fast path - skip suspension
                }

                _state = 0;
                // Register MoveNext as the continuation callback
                _builder.AwaitUnsafeOnCompleted(ref _awaiter, ref this);
                return; // SUSPEND - return to caller
            }

            State0: // Resuming after the await
            _result = _awaiter.GetResult(); // Get value or throw

            // Your code AFTER the await
            string upper = _result.ToUpper();

            _state = -2; // "finished"
            _builder.SetResult(upper); // Complete the returned Task
        }
        catch (Exception ex)
        {
            _state = -2;
            _builder.SetException(ex); // Fault the returned Task
        }
    }
}
```

**What's happening here, in plain English:**

1. **Your method becomes a struct** - all your local variables become fields so they survive across suspensions (the stack frame is gone when we return)

2. **`_state` tracks where we are** - each `await` is a "checkpoint." When we resume, `MoveNext` uses `_state` to jump to the right place

3. **`MoveNext()` is called repeatedly** - once on start, and once for each `await` that actually suspends. Each call advances the state machine by one step

4. **`AsyncTaskMethodBuilder`** is the glue - it creates the `Task` returned to the caller, wires up the continuation, and eventually sets the result or exception on that Task

5. **The fast path** - if `IsCompleted` is true, we never suspend, never allocate a continuation, never leave `MoveNext`. The whole method can complete in a single call

**Note about heap allocation**: The state machine starts as a struct on the stack. If the method actually needs to suspend (the task isn't complete), the struct gets boxed to the heap so it can survive after the method returns. If the method completes synchronously (fast path), no heap allocation occurs.

*(Ref: Stephen Toub - "How Async/Await Really Works in C#", Sergey Tepliakov - "Dissecting the async methods in C#")*

---

## Slide 8: SynchronizationContext - The Problem It Solves

**We now know that `await` registers a continuation. But *where* does that continuation run?**

Imagine you're writing a WPF app. You click a button, start downloading data from the internet, and when it's done, you want to update a label. But .NET UI frameworks have a rule: **only the UI thread can touch UI elements.** If your download finishes on a background thread and you try to update the label, you get a cross-thread exception.

Before `async/await`, you had to manually write `Dispatcher.BeginInvoke(...)` or `Control.BeginInvoke(...)` to bounce work back to the UI thread. `SynchronizationContext` abstracts this - it's a way for any environment to say *"here's how you send work to the right place."*

```csharp
// The base implementation - just queues to the ThreadPool
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
- **Console apps / ASP.NET Core**: **null** - no context at all

*(Ref: Stephen Toub - "ConfigureAwait FAQ", Vasil Kosturski - "Exploring the async/await State Machine – Synchronization Context")*

---

## Slide 9: SynchronizationContext - How await Uses It

**When you `await` an incomplete task, the runtime captures `SynchronizationContext.Current` so it knows where to Post the continuation.**

The runtime's capture logic (from `dotnet/runtime` source) looks like this:

```csharp
// Simplified from the actual runtime code:
SynchronizationContext? syncCtx = SynchronizationContext.Current;

if (syncCtx != null && syncCtx.GetType() != typeof(SynchronizationContext))
{
    // A real, derived SynchronizationContext exists - capture it.
    // The continuation will be Posted to this context.
}
else
{
    // No meaningful SynchronizationContext.
    // Fall back to TaskScheduler (see later slide).
    // If no custom TaskScheduler either → schedule on ThreadPool.
}
```

Notice the `GetType() != typeof(SynchronizationContext)` check - the runtime ignores the *base* `SynchronizationContext` because it would just queue to the ThreadPool anyway (same as having no context). Only *derived* implementations are meaningful.

**This is why your WPF button handler "just works":**

```csharp
private async void Button_Click(object sender, RoutedEventArgs e)
{
    // Running on UI thread. SynchronizationContext.Current
    // is DispatcherSynchronizationContext
    StatusLabel.Text = "Loading...";

    var data = await httpClient.GetStringAsync("https://api.example.com");
    // ↑ Task is incomplete → runtime captures DispatcherSynchronizationContext
    // When the download finishes, the continuation is Posted to the UI thread

    StatusLabel.Text = data; // ✅ Safe - we're back on the UI thread
}
```

Without SynchronizationContext, updating `StatusLabel.Text` after `await` would throw a cross-thread exception. The whole point of this machinery is to make async code feel natural in environments with thread affinity.

*(Ref: Vasil Kosturski - "Exploring the async/await State Machine – Concrete Implementation", Stephen Toub - "How Async/Await Really Works", dotnet/runtime AsyncHelpers.CoreCLR.cs)*

---

## Slide 10: No SynchronizationContext in ASP.NET Core

**Why does ASP.NET Core not have one?**

Legacy ASP.NET had `AspNetSynchronizationContext` which:
- Ensured one-at-a-time execution of continuations per request
- Re-entered the request context (setting `HttpContext.Current`, thread identity, culture)
- Created overhead: queueing, context switching, housekeeping

But unlike a UI app, there's no "special thread" in a web server. Any thread pool thread can handle any request. So the concept of "posting back to the right thread" doesn't apply.

ASP.NET Core dropped it for performance and simplicity:
- Continuations run on **any thread pool thread**
- No "re-entering" overhead
- `HttpContext` is accessed via DI (`IHttpContextAccessor`), not thread-static

**Practical consequences:**
- **No deadlocks** from blocking on async code (`.Result`, `.Wait()`) - though you still shouldn't do it
- **No need** for `ConfigureAwait(false)` in ASP.NET Core application code
- Code that mutates shared state within a request needs explicit synchronization

```csharp
// ASP.NET Core controller - no SynchronizationContext
[HttpGet]
public async Task<IActionResult> Get()
{
    // SynchronizationContext.Current is null here
    var data = await _service.GetDataAsync();
    // Continuation runs on any ThreadPool thread - totally fine
    return Ok(data);
}
```

*(Ref: Stephen Cleary - "ASP.NET Core SynchronizationContext")*

---

## Slide 11: ConfigureAwait(false) - What It Does

**Why does this exist?**

If you're writing library code - say, an HTTP helper or a JSON parser - you don't know who's calling you. Maybe it's a WPF app, maybe it's a console app. If a WPF app calls your library and you `await` something, the runtime will try to post your continuation back to the UI thread. But your library doesn't need the UI thread! It's just processing data. By forcing the continuation onto the UI thread, you're wasting time and potentially causing deadlocks.

`ConfigureAwait(false)` says: *"I don't need to resume where I started. Just use the ThreadPool."*

```csharp
// ConfigureAwait returns a ConfiguredTaskAwaitable - a different awaitable type
// whose awaiter passes continueOnCapturedContext: false to the scheduling logic,
// so context capture is skipped.
await task.ConfigureAwait(false);
```

What happens internally:
- The awaiter receives `continueOnCapturedContext: false`
- It tells the scheduling infrastructure **not** to capture `SynchronizationContext.Current`
- Continuation is scheduled on the **ThreadPool** instead of the original context
- If the task is already completed, `ConfigureAwait(false)` has **no effect** - execution continues synchronously on the same thread

```csharp
// Library code - use ConfigureAwait(false) everywhere
public async Task<string> GetDataAsync()
{
    var raw = await _httpClient.GetStringAsync(url)
        .ConfigureAwait(false);
    // Now running on ThreadPool thread, NOT on caller's context

    var processed = await ProcessAsync(raw)
        .ConfigureAwait(false);
    // Still on ThreadPool - good for library code

    return processed;
}
```

*(Ref: Stephen Toub - "ConfigureAwait FAQ")*

---

## Slide 12: ConfigureAwait - When and Where to Use It

**The decision matrix:**

**Application-level code (WPF, WinForms):**
→ Do NOT use `ConfigureAwait(false)` - you need the UI context to update controls

**Application-level code (ASP.NET Core):**
→ Not needed - there's no SynchronizationContext to capture anyway. Harmless but unnecessary.

**Library code (NuGet packages, shared DLLs):**
→ USE `ConfigureAwait(false)` on **every** `await` - your library doesn't know if callers have a context

```csharp
// ❌ WPF - DON'T use ConfigureAwait(false) if you touch UI after
private async void Button_Click(object sender, RoutedEventArgs e)
{
    var data = await GetDataAsync().ConfigureAwait(false);
    StatusLabel.Text = data; // 💥 Cross-thread exception!
}

// ✅ Library code - always use ConfigureAwait(false)
public async Task<byte[]> DownloadAsync(string url)
{
    var response = await _client.GetAsync(url).ConfigureAwait(false);
    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
}
```

**Key pitfall**: Using `ConfigureAwait(false)` only on the *first* `await` is NOT sufficient. If that task completes synchronously, the context is still present for subsequent awaits. You must use it on **every** `await`.

**Modern alternative (.NET 8+)**: `ConfigureAwait(ConfigureAwaitOptions.None)` is equivalent but more self-documenting. The enum also provides `ForceYielding` (always yield, even if already complete) and `SuppressThrowing` (don't throw on cancellation/fault).

*(Ref: Stephen Toub - "ConfigureAwait FAQ", Stephen Cleary - "Async and Await")*

---

## Slide 13: ConfigureAwait - The Classic Deadlock

**Mixing sync and async code with a SynchronizationContext causes deadlocks.**

This is the single most common async bug:

```csharp
// Library method
public static async Task<JObject> GetJsonAsync(Uri uri)
{
    using var client = new HttpClient();
    var json = await client.GetStringAsync(uri); // captures UI context
    return JObject.Parse(json);
}

// WinForms - DEADLOCK! 💀
public void Button1_Click(object sender, EventArgs e)
{
    var jsonTask = GetJsonAsync(uri);
    textBox1.Text = jsonTask.Result; // ← blocks the UI thread
}
```

**What's happening step by step:**
1. `GetJsonAsync` starts `GetStringAsync` and captures the UI `SynchronizationContext`
2. `.Result` blocks the UI thread, waiting for the task to complete
3. `GetStringAsync` finishes on a background thread and tries to **Post** the continuation to the UI thread
4. The UI thread is blocked by `.Result` - it can't process the Posted continuation
5. **Deadlock**: the task can't complete because it needs the UI thread, and the UI thread can't unblock because it's waiting for the task

**Two solutions:**
1. **Don't block**: use `async` all the way down (`await` instead of `.Result`)
2. **ConfigureAwait(false)** in the library: continuation doesn't need the UI thread

**In ASP.NET Core**, this deadlock doesn't happen because there's no SynchronizationContext. But **don't rely on this** - use `async` all the way.

*(Ref: Stephen Cleary - "Don't Block on Async Code")*

---

## Slide 14: ExecutionContext - The Invisible Flow

**Why does this exist?**

Imagine you set a security principal on your thread, or you store a correlation ID for logging. Then you `await` something and your continuation resumes on a totally different thread. If those ambient values didn't travel with you, you'd lose your security identity or your logging context mid-request. That would be a disaster.

`ExecutionContext` solves this: it carries ambient data across async boundaries - automatically.

Unlike SynchronizationContext (which determines *where* continuations run), ExecutionContext carries *what data* flows with them. It includes security context, `AsyncLocal<T>` values, and more.

```csharp
// ExecutionContext ALWAYS flows across awaits - this is non-negotiable
static AsyncLocal<int> _requestId = new AsyncLocal<int>();

static async Task ProcessAsync()
{
    _requestId.Value = 42;

    await Task.Delay(100);
    // ExecutionContext was captured and restored here
    // _requestId.Value is still 42 ✅ - even if we're on a different thread

    Console.WriteLine(_requestId.Value); // 42
}
```

Key facts:
- `ExecutionContext` flows via `Task.Run`, `ThreadPool.QueueUserWorkItem`, and across `await`
- `ConfigureAwait(false)` does **NOT** suppress ExecutionContext flow - it only affects SynchronizationContext
- The `AsyncMethodBuilderCore.Start` method explicitly saves and restores both `ExecutionContext` and `SynchronizationContext` around the initial `MoveNext()` call, preventing context changes in the synchronous part of your method from leaking to the caller

*(Ref: Sergey Tepliakov - "Dissecting the async methods in C#", Stephen Toub - "ExecutionContext vs SynchronizationContext")*

---

## Slide 15: AsyncLocal\<T\> - Thread-Local Storage for Async

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

**How does the isolation work?** `ExecutionContext` is immutable - every time you set an `AsyncLocal` value, a **new** `ExecutionContext` instance is created (copy-on-write). The `AsyncMethodBuilderCore.Start` method saves the current context before `MoveNext()` and restores it afterwards. So when the child modifies an `AsyncLocal`, it gets its own copy of the context, leaving the parent's reference unchanged.

**Real-world uses**: distributed tracing (Activity/OpenTelemetry), scoped DI containers, logging correlation IDs, culture propagation.

*(Ref: Stephen Cleary - "Implicit Async Context ('AsyncLocal')")*

---

## Slide 16: AsyncLocal\<T\> - The Synchronous Trap

**Synchronous methods do NOT get the save/restore treatment.**

The copy-on-write isolation only kicks in for methods that go through the `AsyncMethodBuilder` infrastructure (i.e., `async` methods). Plain synchronous methods run on the caller's `ExecutionContext` directly.

```csharp
static AsyncLocal<int> _ctx = new AsyncLocal<int>();

static async Task Main()
{
    _ctx.Value = 1;
    SyncMethod();  // ⚠️ This modifies the SAME context!
    Console.WriteLine(_ctx.Value); // 999 - NOT 1!

    await AsyncMethod();
    Console.WriteLine(_ctx.Value); // 1 - async restored properly
}

static void SyncMethod()
{
    _ctx.Value = 999; // 💥 Leaks to caller! No save/restore wrapper!
}

static async Task AsyncMethod()
{
    _ctx.Value = 999; // ✅ Isolated - caller won't see this
    await Task.Yield();
}
```

**Rule of thumb**: If a method modifies `AsyncLocal` values, make sure it's `async` - or callers may see unexpected mutations.

*(Ref: Stephen Cleary - "Eliding Async and Await")*

---

## Slide 17: ExecutionContext vs SynchronizationContext - Summary

| Aspect                    | ExecutionContext                       | SynchronizationContext                 |
|---------------------------|----------------------------------------|----------------------------------------|
| **Purpose**               | Carries ambient data (security, AsyncLocal) | Determines *where* continuations run  |
| **Flows across `await`?** | Always                                 | Only if `ConfigureAwait(true)` (default) |
| **Affected by `ConfigureAwait(false)`?** | No            | Yes - context is not captured          |
| **Who manages it?**       | `AsyncMethodBuilderCore.Start` (save/restore) + `AsyncStateMachineBox` (capture/flow) | Awaiter infrastructure (`CaptureContinuationContext`) |
| **Can suppress flow?**    | Only via `ExecutionContext.SuppressFlow()` (rare, dangerous) | Yes - `ConfigureAwait(false)` |

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

Think of it this way: **ExecutionContext** is your luggage - it always travels with you. **SynchronizationContext** is your return ticket - `ConfigureAwait(false)` throws it away.

*(Ref: Sergey Tepliakov - "Dissecting the async methods in C#", Stephen Toub - "ConfigureAwait FAQ")*

---

## Slide 18: TaskScheduler - The Other Context

**Why does this exist?**

Sometimes you need more control than "run it on the ThreadPool." Maybe you want to limit concurrency to N tasks at a time, or you need to run tasks on a specific thread. `TaskScheduler` lets you plug in custom scheduling strategies.

Most of the time, you'll never need to think about it. But `await` *does* check for it.

**When `SynchronizationContext.Current` is null (or is the base type), the async infrastructure falls back to `TaskScheduler.Current`.**

Built-in schedulers:
- `TaskScheduler.Default` - schedules to ThreadPool (most common)
- `ConcurrentExclusiveSchedulerPair` - provides reader/writer-like scheduling without locks

```csharp
// Custom scheduler for concurrency control
var pair = new ConcurrentExclusiveSchedulerPair();
var exclusive = pair.ExclusiveScheduler; // Only 1 task at a time

await Task.Factory.StartNew(async () =>
{
    // TaskScheduler.Current is now 'exclusive'
    // AND it's sticky - async continuations also use it!
    await SomeAsync(); // continuation also runs on 'exclusive'
}, CancellationToken.None, TaskCreationOptions.None, exclusive).Unwrap();
```

⚠️ **The sticky deadlock trap:** Custom TaskSchedulers with limited threads can deadlock if continuations are also queued to them. Use `ConfigureAwait(false)` to escape, or prefer `SemaphoreSlim`, `Channel<T>`, or `Parallel.ForEachAsync` for concurrency throttling.

*(Ref: Stephen Cleary - "Async and Scheduled Concurrency", Sergey Tepliakov - "You probably should stop using a custom TaskScheduler")*

---

## Slide 19: CancellationToken - Fundamentals

**Why does this exist?**

Imagine a user clicks "Search" and then immediately clicks "Cancel." Or a web request takes too long and the client disconnects. Without cancellation, the server keeps doing work that nobody cares about anymore - wasting CPU, memory, and database connections.

.NET uses **cooperative cancellation**: the caller signals *"I'd like you to stop,"* and the callee decides *when and how* to stop. Nobody yanks the rug out from under a running operation.

```csharp
// The pattern: CancellationTokenSource creates the signal,
// CancellationToken observes it
public async Task ProcessItemsAsync(CancellationToken cancellationToken)
{
    foreach (var item in items)
    {
        // Option 1: Poll - check if cancellation was requested
        cancellationToken.ThrowIfCancellationRequested();

        // Option 2: Pass to framework methods (they check internally)
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Option 3: Register a callback for cleanup
        cancellationToken.Register(() => _connection.Close());
    }
}
```

```csharp
// ASP.NET Core - the framework provides the token automatically
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

*(Ref: Microsoft DevBlogs - "Recommended patterns for CancellationToken")*

---

## Slide 20: CancellationToken - Linked Tokens & Timeouts

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

**Key insight**: After side effects occur (file uploaded, email sent), switch to `CancellationToken.None` to avoid leaving inconsistent state. You don't want a notification to be skipped just because the client disconnected after the upload finished.

*(Ref: Stephen Cleary - "Cancellation, Part 6: Linking")*

---

## Slide 21: CancellationToken - Exception Handling

**Always catch `OperationCanceledException`, not `TaskCanceledException`.**

`TaskCanceledException` *derives from* `OperationCanceledException`. If you only catch `TaskCanceledException`, you'll miss cancellations from `CancellationToken.ThrowIfCancellationRequested()` which throws `OperationCanceledException` directly.

```csharp
public async Task ExecuteWithRetryAsync(CancellationToken ct)
{
    try
    {
        await _httpClient.SendAsync(request, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // ✅ Clean cancellation - the caller asked us to stop
        _logger.LogInformation("Operation cancelled by caller");
        throw; // re-throw to propagate cancellation
    }
    catch (OperationCanceledException)
    {
        // ⚠️ Not our token - could be HttpClient's internal timeout
        _logger.LogWarning("Operation cancelled unexpectedly (timeout?)");
        throw;
    }
}
```

**Rules:**
1. Catch `OperationCanceledException`, not `TaskCanceledException`
2. Check `cancellationToken.IsCancellationRequested` to distinguish *your* cancellation from internal ones
3. When re-throwing, use `throw;` - never `throw ex;` (preserves stack trace)
4. Don't throw `OperationCanceledException` after work is complete - return success instead

*(Ref: Microsoft DevBlogs - "Recommended patterns for CancellationToken")*

---

## Slide 22: Eliding async/await - When to Omit the Keywords

**Why would you ever omit them?**

Each `async` method creates a state machine. For trivial one-line passthroughs, you're paying for that setup just to forward a call. By "eliding" (omitting) `async/await`, you return the inner task directly.

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
}

// ✅ Fixed - keep async/await
async Task<string> GetDataAsync()
{
    using var client = new HttpClient();
    return await client.GetStringAsync("https://api.example.com");
    // HttpClient is disposed AFTER the request completes ✅
}
```

*(Ref: Stephen Cleary - "Eliding Async and Await")*

---

## Slide 23: Eliding async/await - The Hidden Pitfalls

**Three major dangers when eliding async/await:**

**1. Exception semantics change:**
```csharp
// With async/await - exception is placed on the Task
async Task<int> DivideAsync(int a, int b)
{
    return a / b; // DivideByZeroException is stored on the Task
}

// Without async - exception is thrown synchronously to caller
Task<int> DivideElided(int a, int b)
{
    return Task.FromResult(a / b);
    // 💥 DivideByZeroException is thrown HERE, before a Task is even created
}
```

**2. AsyncLocal isolation is lost** (see Slide 16):
Synchronous methods don't get the `AsyncMethodBuilder`'s save/restore wrapper.

**3. Stack traces become harder to read:**
Elided methods don't appear in async stack traces, making debugging harder.

**Recommendation**: Keep `async`/`await` by default. Only elide for trivial single-line passthroughs with no `try/catch`, no `using`, and no `AsyncLocal` mutations.

*(Ref: Stephen Cleary - "Eliding Async and Await", Sergey Tepliakov - "The performance characteristics of async methods in C#")*

---

## Slide 24: async void - The Danger Zone

**Why does this exist?**

Event handlers in UI frameworks (like `Button.Click`) have a fixed signature: `void MethodName(object sender, EventArgs e)`. They can't return `Task`. So `async void` exists as a special case to allow `await` inside event handlers. **That's the only legitimate use.**

**`async void` methods are fire-and-forget with no way to observe errors from outside.**

```csharp
// ❌ async void - exception can crash the process!
async void DoWorkAsync()
{
    await Task.Delay(100);
    throw new Exception("Oops");
    // 💥 Where does this exception go?
    // → If SynchronizationContext exists: Posted to that context (crash dialog in WPF)
    // → If no context (Console, ASP.NET Core): thrown on ThreadPool → process crash
}

// ✅ async Task - exception is captured on the Task
async Task DoWorkAsync()
{
    await Task.Delay(100);
    throw new Exception("Oops"); // Stored on the Task, caller can observe
}
```

```csharp
// ✅ The ONE valid use of async void - UI event handlers
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

*(Ref: Sergey Tepliakov - "The Dangers of Async Void", Stephen Cleary - "Async and Await")*

---

## Slide 25: Task.Run vs Task.Factory.StartNew

**Prefer `Task.Run` for async delegates. `Task.Factory.StartNew` has subtle bugs.**

`Task.Factory.StartNew` predates `async/await` and was designed for CPU-bound delegates. When you pass an `async` lambda to it, things break:

```csharp
// ❌ Task.Factory.StartNew with async - returns Task<Task>!
var task = Task.Factory.StartNew(async () =>
{
    await Task.Delay(1000);
    throw new Exception("Error");
});
// task.Status is RanToCompletion even though the inner task failed!
// task is Task<Task> - the outer completed, the inner is lost

// ✅ Fix with Unwrap
var task = Task.Factory.StartNew(async () => { ... }).Unwrap();

// ✅ Better - just use Task.Run (handles unwrapping automatically)
var task = Task.Run(async () =>
{
    await Task.Delay(1000);
    throw new Exception("Error");
});
```

**`Task.Factory.StartNew` pitfall with `LongRunning`**: The `LongRunning` flag creates a dedicated thread, but only for the synchronous portion before the first `await`. After that, the dedicated thread is released and the continuation runs on the ThreadPool anyway.

*(Ref: Sergey Tepliakov - "The Dangers of Task.Factory.StartNew")*

---

## Slide 26: ValueTask - When and Why

**Why does this exist?**

`Task<T>` is a class - every time an async method returns one, it allocates an object on the heap. For methods that *often complete synchronously* (like reading from a cache), you're paying for a heap allocation you don't need. `ValueTask<T>` is a struct that can wrap either a raw `TResult` value (no allocation!) or a `Task<T>` (for the truly async path).

```csharp
// If the cache hits, we return synchronously - no Task allocation
public ValueTask<User> GetUserAsync(int id)
{
    if (_cache.TryGetValue(id, out var user))
        return new ValueTask<User>(user); // No allocation!

    return new ValueTask<User>(GetUserFromDbAsync(id)); // Wraps the Task
}
```

**Rules for consuming `ValueTask<T>` - stricter than `Task<T>`:**
```csharp
// ✅ Await it once
var user = await GetUserAsync(1);

// ❌ NEVER await it twice
var vt = GetUserAsync(1);
var a = await vt;
var b = await vt; // 💥 Undefined behavior!

// ❌ NEVER use WhenAll/WhenAny with ValueTask - convert to Task first
var tasks = ids.Select(id => GetUserAsync(id).AsTask());
await Task.WhenAll(tasks);
```

*(Ref: Sergey Tepliakov - "The performance characteristics of async methods in C#", Stephen Toub - .NET performance blogs)*

---

## Slide 27: Putting It All Together - How the Pieces Connect

**A map of the async machinery:**

```
You write:      async Task<T> MyMethodAsync()
                {
                    var x = await SomeOperationAsync();
                    return Transform(x);
                }

Compiler generates:  State Machine (struct → boxed to heap if it suspends)
                     ├── MoveNext()          - advances through your code
                     ├── _state              - tracks which await we're at
                     ├── _awaiter            - the current awaiter
                     └── _builder            - AsyncTaskMethodBuilder
                          └── .Task          - the Task<T> returned to caller

At each await:   1. awaitable.GetAwaiter()     → get the awaiter
                 2. awaiter.IsCompleted?        → fast path if yes
                 3. awaiter.OnCompleted(MoveNext) → register callback
                 4. Context capture:
                    ├── SynchronizationContext → WHERE to resume
                    │   (suppressed by ConfigureAwait(false))
                    └── ExecutionContext       → WHAT DATA flows
                        (always flows, carries AsyncLocal<T>)
                 5. return to caller           → thread is free

When complete:   1. Callback fires → MoveNext() called
                 2. awaiter.GetResult()         → value or exception
                 3. Continue to next statement
                 4. Eventually: builder.SetResult() or SetException()
                    → completes the returned Task<T>
```

---

## Slide 28: Decision Flowchart - Quick Reference

**"Should I use `ConfigureAwait(false)`?"**
- Writing library code? → Yes, on every `await`
- Writing ASP.NET Core application code? → Not needed
- Writing WPF/WinForms code that touches UI after? → No
- Writing WPF/WinForms code in a non-UI helper method? → Yes

**"Should I elide async/await?"**
- Trivial one-line passthrough? → OK to elide
- Has `using`, `try/catch`, or multiple statements? → Keep async/await
- Modifies `AsyncLocal` values? → Keep async/await
- Unsure? → Keep async/await

**"Should I use CancellationToken?"**
- I/O-bound or long-running? → Yes, always propagate
- Already committed side effects? → Switch to `CancellationToken.None`
- Public API? → Make it optional (default `CancellationToken.None`)
- Internal method? → Make it required

**"Should I use `async void`?"**
- UI event handler? → OK, but wrap body in try/catch
- Anything else? → No. Return `Task`.

**"Should I use `ValueTask<T>`?"**
- Frequently completes synchronously? → Consider `ValueTask<T>`
- Consumers await it exactly once? → Fine
- Need `WhenAll`, `WhenAny`, or multiple awaits? → Stick with `Task<T>`

---

## Slide 29: References & Further Reading

**Stephen Toub (Microsoft .NET Team)**
- "How Async/Await Really Works in C#" - devblogs.microsoft.com/dotnet/how-async-await-really-works
- "ConfigureAwait FAQ" - devblogs.microsoft.com/dotnet/configureawait-faq
- "Await Anything" - devblogs.microsoft.com/pfxteam/await-anything
- "Processing Tasks as They Complete" - devblogs.microsoft.com/dotnet/processing-tasks-as-they-complete

**Stephen Cleary (Microsoft MVP)**
- "Don't Block on Async Code" - blog.stephencleary.com/2012/07/dont-block-on-async-code
- "Eliding Async and Await" - blog.stephencleary.com/2016/12/eliding-async-await
- "ASP.NET Core SynchronizationContext" - blog.stephencleary.com/2017/03/aspnetcore-synchronization-context
- "A Tour of Task" - blog.stephencleary.com/2014/04/a-tour-of-task-part-0-overview
- "Cancellation, Part 6: Linking" - blog.stephencleary.com/2024/10/cancellation-6-linking

**Sergey Tepliakov (Dissecting the Code)**
- "Dissecting the async methods in C#" - devblogs.microsoft.com/premier-developer/dissecting-the-async-methods-in-c
- "You probably should stop using a custom TaskScheduler" - sergeyteplyakov.github.io
- "The Dangers of Async Void" - sergeyteplyakov.github.io

**Vasil Kosturski**
- "Exploring the async/await State Machine" (full series) - vkontech.com

**Microsoft Learn**
- "Task-based Asynchronous Pattern (TAP)" - learn.microsoft.com
- "Recommended patterns for CancellationToken" - devblogs.microsoft.com/premier-developer

**Runtime source code (for the curious)**
- `SynchronizationContext.cs` - github.com/dotnet/runtime
- `AsyncMethodBuilderCore.cs` - github.com/dotnet/runtime
- `AsyncVoidMethodBuilder.cs` - github.com/dotnet/runtime
- `ExecutionContext.cs` - github.com/dotnet/runtime