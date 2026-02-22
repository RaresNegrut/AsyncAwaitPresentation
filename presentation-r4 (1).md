## Slide 1: Beyond the Basics of
Beyond the Basics of
async / await
What really happens when you press F5

### Notes
Welcome to Beyond the Basics of async/await. You've written 'async Task DoSomethingAsync()' hundreds of times. You know the rule: if it's async, you await it.
Your IDE screams at you if you forget. Your code reviews demand it. And so you do it - almost mechanically.
But do you know what actually happens when you press F5? The rest of this talk answers the questions you've probably never asked.

---

## Slide 2: "We all do this, but why?"
"We all do this, but why?"
You’ve written this method a thousand times...
```csharp
async Task DoWorkAsync()
{
    Console.WriteLine("Before");
    await Task.Delay(1000);
    Console.WriteLine("After");
}
```
- What is a Task, really?
- Who actually runs it?
- What does await compile to?
- Why does ConfigureAwait(false) exist?
You use async/await every day. It’s time to understand what’s happening underneath.
1

### Notes
You've written this method a thousand times. Three lines. Simple.
But behind those three lines, the compiler is doing a lot. It's rewriting your method into something you wouldn't recognise. And the runtime is coordinating a whole cast of infrastructure you've never had to think about - until today.
We're going to answer: What is a Task, really? Who actually runs it? What does await compile to? Why does ConfigureAwait(false) exist, and should you care? Let's peel the layers back, one at a time.

---

## Slide 3: The Problem: Why Does Async Exist?
The Problem: Why Does Async Exist?
- ~1 MB
- stack per thread
- 10,000
- concurrent requests
- 20-50
- threads needed with async
- Blocking (Thread.Sleep)
- Thread.Sleep(1000);
- Thread alive, consuming memory, doing nothing. Can’t be reused.
```csharp
Async (await Task.Delay)
await Task.Delay(1000);
OS timer fires. Thread released, free for other work. Resumes on continuation.
```
Async is about not wasting threads while waiting - not parallelism.
2

### Notes
Threads are expensive. Each thread in .NET costs roughly 1 MB of stack space, plus the OS overhead of creating, scheduling, and context-switching it. Now think about a server handling 10,000 concurrent requests. If each request occupies a thread for the duration of its work - including the time it spends waiting for a database query or an HTTP response - that's 10,000 threads. That's roughly 10 GB of stack space alone.
That's exactly what synchronous, blocking code does. Thread.Sleep is a crime scene: the thread is alive, consuming memory, registered with the OS scheduler, and it's accomplishing absolutely nothing. It's just waiting. But because it's blocked, nobody else can use it.
With async, we say: 'Hey runtime, I need to wait for 1000 milliseconds. I don't need this thread during that time. Take it back, give it to someone else, and come find me when the timer fires.' With Task.Delay, no thread is held. A timer is set at the OS level. The thread is released and can do other useful work.
That server handling 10,000 concurrent requests? With async I/O, it might only need 20-50 threads - the ones actually computing at any given instant. Async is not the same as parallelism. Parallelism means doing multiple things at the same time to go faster. Async means not holding resources hostage while doing nothing.

---

## Slide 4: Pre-await: ContinueWith and Callback Hell
Pre-await: ContinueWith and Callback Hell
- Nesting grows with every sequential step
- No try/catch - must check IsFaulted manually on each step
- Unwrap() needed: ContinueWith on Task<T> returns Task<Task<T>>
- Code structure doesn't match the logical flow
```csharp
// Each sequential step = another level of nesting
Task DoWorkTPL() {
    return Task.Delay(1000).ContinueWith(t => {
        if (t.IsFaulted) return Task.CompletedTask;
        Console.WriteLine("Step 1");
        return Task.Delay(500).ContinueWith(_ => {
            Console.WriteLine("Step 2");
        });
    }).Unwrap();
}
```
```csharp
// With async/await: flat, sequential, natural
async Task DoWorkClean() {
    Console.WriteLine("Step 1");
    await Task.Delay(1000);
    Console.WriteLine("Step 2");
    await Task.Delay(500);
}
```
await is syntactic sugar for ContinueWith - same mechanics, dramatically better ergonomics.
3

### Notes
Before async/await came along in C# 5.0, the answer was the Task Parallel Library, specifically the ContinueWith method. The idea was: start an operation, and attach a callback that runs when it finishes. The thread that started the operation is free to leave; the callback will be invoked later.
But real code has sequential steps. Each step needs the result of the previous one. So you end up nesting. Two callbacks deep is the trivial case. Imagine ten sequential asynchronous operations - ten levels of nesting. This is exactly what JavaScript developers called 'callback hell.'
It gets worse with error handling. With ContinueWith, there's no try/catch. Exceptions are wrapped in AggregateException on the Task's .Exception property. You have to check IsFaulted manually in every callback. Miss a check, and the exception vanishes silently. And there's Unwrap() - because ContinueWith on a Task that returns another Task gives you a Task<Task>.
Now look at the same logic with async/await: flat, sequential, try/catch works normally. The compiler handles all the callback plumbing for you. Same underlying mechanism - continuations on Tasks - but the developer experience is night and day.

---

## Slide 5: Task: A Promise + TaskCompletionSource
Task: A Promise + TaskCompletionSource
```csharp
Task = promise, not a thread or unit of execution
Task.Delay: sets an OS timer. Zero threads used.
Task.Run: queues work to a ThreadPool thread
TaskCompletionSource: bridge between event-driven code and async/await
All I/O APIs eventually wrap a TCS completed by an OS callback
Task<T> = same promise, but also carries a typed result
```
```csharp
// Task.Delay: registers a timer - NO thread involved
Task delayTask = Task.Delay(1000);
// Task.Run: queues a delegate to a worker thread
Task runTask = Task.Run(() => Console.WriteLine("On thread"));
// TaskCompletionSource: manual control over completion
var tcs = new TaskCompletionSource<int>();
// No thread running. We hold the Task open.
// Later - maybe from an OS callback or event:
tcs.SetResult(42);  // Now tcs.Task is complete
await tcs.Task;     // Returns 42
```
A Task is a data structure - a promise. TaskCompletionSource lets you create and resolve Tasks manually.
4

### Notes
The most important misconception to kill: a Task is not a thread. A Task does not represent a thread. It does not inherently own a thread, use a thread, or require a thread. A Task is a promise - a data structure that represents an operation that will complete at some point in the future.
Task.Delay does NOT create a thread. It sets a system timer. During the wait, zero threads are involved. Task.Run takes your delegate and queues it to be executed by a worker thread, but the Task is still just the promise object - the notification mechanism.
TaskCompletionSource is the key primitive that proves no thread ever needs to be involved. You create a Task and complete it manually. This is how most truly asynchronous APIs work under the hood. At the bottom of the stack, there's almost always a TaskCompletionSource being completed by an OS callback - network data arrived, a file read finished, a timer fired. No dedicated thread sat around waiting.
TaskCompletionSource has three completion methods: SetResult (success), SetException (fault), and SetCanceled (cancellation). Each transitions the Task to its final state. TrySet variants return false instead of throwing if the Task is already completed. This is the bridge between event-driven OS callbacks and the async/await world.

---

## Slide 6: CancellationToken: Cooperative Cancellation
CancellationToken: Cooperative Cancellation
- CancellationTokenSource: creates the token, controls cancellation
- CancellationToken: read-only handle passed into async methods
- Cooperative: methods must actively observe the token
- ct.ThrowIfCancellationRequested() - manual check in CPU loops
- Most BCL async methods accept a token (HttpClient, Stream, etc.)
- Throws OperationCanceledException - catch at top level
- Always pass tokens downstream - don't ignore them
```csharp
var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));  // auto-cancel in 5s
try {
    await DoLongWorkAsync(cts.Token);
} catch (OperationCanceledException) {
    Console.WriteLine("Work was cancelled");
}
async Task DoLongWorkAsync(CancellationToken ct) {
    await Task.Delay(10_000, ct);     // Delay respects cancellation
    ct.ThrowIfCancellationRequested(); // Manual checkpoint
    await SomethingElseAsync(ct);      // Pass token downstream
}
```
Cancellation is cooperative: pass the token everywhere and check it at every async boundary.
5

### Notes
Cancellation in .NET is cooperative, not preemptive. You cannot forcibly abort an async operation from the outside. Instead, you pass a CancellationToken into the method, and the method must actively check it and decide to stop.
CancellationTokenSource is the controller - it creates the token and has the Cancel() method. CancellationToken is the read-only handle you pass into async methods. This separation of concerns means the code doing the work can observe cancellation but can't accidentally trigger it.
CancellationTokenSource also supports timed cancellation with CancelAfter(). You can link multiple sources together with CreateLinkedTokenSource, which cancels when ANY of the linked sources cancel - useful for combining a user-initiated cancel with a timeout.
Most BCL async methods accept a CancellationToken parameter: HttpClient.GetAsync, Stream.ReadAsync, Task.Delay, and many more. When cancelled, they throw OperationCanceledException (or its subclass TaskCanceledException). For CPU-bound loops, call ct.ThrowIfCancellationRequested() at regular intervals.
Always pass tokens downstream through your entire call chain. If you accept a CancellationToken but don't pass it to the async methods you call, cancellation won't propagate. This is one of the most common mistakes. Also remember to dispose CancellationTokenSource when you're done - it registers with the OS timer if you use CancelAfter.

---

## Slide 7: ThreadPool, SynchronizationContext and the Classic Deadlock
ThreadPool, SynchronizationContext and the Classic Deadlock
ThreadPool
SynchronizationContext
```csharp
Pool of pre-created worker threads - reuse avoids per-thread allocation
Task.Run() and async continuations queue work here by default
Starts ~1 thread/CPU core; slowly grows under load (~1 per 500ms)
DANGER: blocking threads with .Result/.Wait() starves the pool
```
- Decides WHERE continuations run after an await
- WinForms/WPF: posts to the UI thread message loop
- ASP.NET Core / Console: null - continuations go to ThreadPool
- Priority: SynchCtx.Current -> TaskScheduler.Default -> ThreadPool
```csharp
// The classic deadlock - DON'T DO THIS in UI code:
void ButtonClick() {
    var r = SomeAsync().Result;  // 1. Blocks UI thread
}                                // 4. DEADLOCK
async Task SomeAsync() {
    await Task.Delay(1000);      // 2. Captures UI context
    // 3. Tries to post back to UI thread... which is blocked
}
```
SynchronizationContext explains why UI code works after await - and why blocking on async causes deadlocks.
6

### Notes
The .NET ThreadPool is a pool of pre-created worker threads, managed by the runtime, that exist for the lifetime of your application. When you call Task.Run, the delegate is placed into a queue. One of the ThreadPool's worker threads dequeues it and executes it. Thread creation is expensive - allocating ~1 MB of stack space, making OS system calls. The pool solves this by reusing threads.
The ThreadPool starts with a small number of threads, typically one per CPU core. If work items queue up faster than threads can process them, the pool slowly injects new threads - roughly one every 500ms. This conservative growth is intentional. But if you block many ThreadPool threads by calling .Result or .Wait(), you can starve the pool.
SynchronizationContext is an abstraction that answers: where should a continuation run? In WinForms, it posts to the UI thread's Win32 message loop. In WPF, it posts to the Dispatcher. In ASP.NET Core and console apps, there is no SynchronizationContext - it's null.
The classic deadlock: You're on the UI thread. You call SomeAsyncMethod().Result - blocking the UI thread. Inside SomeAsyncMethod, the code hits an await and captures the SynchronizationContext. The awaited operation completes and tries to post the continuation to the UI thread. But the UI thread is blocked waiting for the very Task that needs it. Deadlock. This does not happen in console apps or ASP.NET Core because there's no SynchronizationContext.

---

## Slide 8: ExecutionContext and AsyncLocal
ExecutionContext and AsyncLocal
```csharp
ExecutionContext: captured at every await, restored on resume
Preserves ambient state even when the thread changes
AsyncLocal<T>: value tied to the async flow, not the physical thread
Copy-on-write: child flows inherit parent context; changes don't propagate back
Never use ThreadLocal<T> in async code - breaks after any await
Practical use: per-request correlation IDs, user identity, culture
```
```csharp
static AsyncLocal<string> _user = new AsyncLocal<string>();
async Task DemoAsyncLocal() {
    _user.Value = "Alice";
    await Task.Delay(500);
    // May be on a different thread now, but context was restored:
    Console.WriteLine(_user.Value);  // Still "Alice"!
    await Task.Run(() => {
        Console.WriteLine(_user.Value);  // "Alice" (inherited)
        _user.Value = "Bob";             // Only affects child copy
    });
    Console.WriteLine(_user.Value);  // "Alice" (parent unchanged)
}
```
ExecutionContext flows automatically. Use AsyncLocal<T> for ambient state; never ThreadLocal<T> in async code.
7

### Notes
After an await, your code might resume on a completely different thread. But ambient state - the current culture, security principal, per-request correlation IDs - needs to survive. ExecutionContext is an opaque container maintained by the runtime that holds all ambient data associated with the current flow of execution.
When the async infrastructure suspends your code at an await, it captures the current ExecutionContext. When the continuation resumes on a different thread, the captured ExecutionContext is restored. You almost never interact with it directly - it's automatic.
AsyncLocal<T> is the user-facing API built on top of ExecutionContext. Think of it as a 'logical thread-local' - a value associated with the current async flow, not any particular physical thread. The value persists across await boundaries because ExecutionContext carries it.
Copy-on-write isolation: when you fork execution with Task.Run, the child flow gets a copy of the parent's ExecutionContext. Modifications in the child don't affect the parent. This is the right behaviour for per-request correlation IDs: every spawned task inherits the ID, but can't overwrite the parent's state.
Never use ThreadLocal<T> in async code. ThreadLocal is tied to the physical thread. After an await, you might be on a different thread and your value will be gone - or worse, you'll see someone else's value left over from whatever that thread was doing before. AsyncLocal<T> always works correctly across await boundaries.

---

## Slide 9: State Flows Across Awaits
State Flows Across Awaits
- ExecutionContext is captured at each await and restored when the continuation runs.
- AsyncLocal<T> stores per-async-flow data inside it.
```csharp
static AsyncLocal<string> _user = new AsyncLocal<string>();
async Task DemoAsyncLocal()
{
    _user.Value = "Alice";
    Console.WriteLine($"Before: {_user.Value}");  // Alice
    await Task.Delay(500);
    // Possibly on a different thread now, but:
    Console.WriteLine($"After:  {_user.Value}");  // Alice!
}
```
- ✅  AsyncLocal<T> - follows the async flow
- ❌  ThreadLocal<T> - tied to physical thread
17

### Notes
ExecutionContext is an opaque container maintained by the runtime. It holds all the ambient data associated with the current flow of execution. When the async infrastructure suspends your code at an await, it captures the current ExecutionContext.
When the continuation resumes - possibly on a different thread - the captured ExecutionContext is restored before your code runs. You almost never interact with ExecutionContext directly. It's automatic.
What flows inside it? The security context, the call context, the current culture, and any AsyncLocal<T> values. AsyncLocal<T> is a 'logical thread-local' - a value associated with the current async flow, not any particular physical thread. The value persists across the await because ExecutionContext was captured (carrying the AsyncLocal data) and restored on the other side.
It doesn't matter that the thread changed - the logical context was preserved.

---

## Slide 10: Copy-on-Write Isolation
Copy-on-Write Isolation
Child flows inherit the parent’s ExecutionContext, but changes stay in the child.
```csharp
_user.Value = "Alice";
await Task.Run(() =>
{
    Console.WriteLine(_user.Value);  // Alice (inherited)
    _user.Value = "Bob";             // Only affects child
});
Console.WriteLine(_user.Value);      // Alice (unchanged!)
```
ExecutionContext flows automatically. Use AsyncLocal<T> for ambient state; never ThreadLocal<T>.
18

### Notes
When you fork execution - say, by starting a Task.Run - the child flow gets a COPY of the parent's ExecutionContext. Modifications in the child don't affect the parent. This is copy-on-write semantics.
This is the right behaviour for things like per-request correlation IDs: you want every task spawned during a request to inherit the ID, but you don't want a child task accidentally overwriting the parent's state. Why not ThreadLocal<T>? Because ThreadLocal is tied to the physical thread, not the logical async flow. After an await, the continuation might run on a different thread - and your ThreadLocal value will be gone, or worse, you'll see someone else's value left over from whatever that thread was doing before.
AsyncLocal<T> always works correctly across await boundaries. ThreadLocal<T> is a trap in async code.

---

## Slide 11: 08
08
The Awaitable Pattern
await is pattern-based, not type-based
19

### Notes
So far, every time we've used await, we've awaited a Task. You might think await and Task are inseparable. They're not.
The await keyword doesn't know about Task. It doesn't require Task. Instead, await is pattern-based.
It relies on the awaitable pattern.

---

## Slide 12: What Makes Something Awaitable?
What Makes Something Awaitable?
await doesn’t require Task. It requires the awaitable pattern:
GetAwaiter()
Return an awaiter object
IsCompleted
Bool - has it already finished? (fast path)
OnCompleted(Action)
Register the callback for when it’s done
GetResult()
Return result or rethrow exception
You can await anything with a GetAwaiter() method - Task is just the most common one.
20

### Notes
An object is awaitable if it has a GetAwaiter() method - instance method or extension method, the compiler doesn't care. It just needs to return an awaiter. The awaiter provides three things: IsCompleted (a bool - has it already finished? enables the fast path), OnCompleted(Action) (registers a callback - 'when you're done, call this'), and GetResult() (returns the result or rethrows the exception).
That's it. Any type with GetAwaiter() returning something with these three members can be awaited. Task happens to implement this pattern, but it's not special.
This extensibility is how ValueTask works (a more allocation-efficient alternative), and how ConfigureAwait(false) works - by returning a different wrapper type whose awaiter skips SynchronizationContext capture.

---

## Slide 13: Custom Awaitable Example
Custom Awaitable Example
```csharp
struct MyDelayAwaitable
{
    private readonly int _ms;
    public MyDelayAwaitable(int ms) => _ms = ms;
    public MyDelayAwaiter GetAwaiter() => new MyDelayAwaiter(_ms);
}
struct MyDelayAwaiter : INotifyCompletion
{
    private readonly Task _task;
    public MyDelayAwaiter(int ms) => _task = Task.Delay(ms);
    public bool IsCompleted => _task.IsCompleted;
    public void OnCompleted(Action c) => _task.GetAwaiter().OnCompleted(c);
    public void GetResult() => _task.GetAwaiter().GetResult();
}
```
```csharp
await new MyDelayAwaitable(1000);  // This works!
```
21

### Notes
Here we prove it by building a custom awaitable. MyDelayAwaitable has GetAwaiter() that returns MyDelayAwaiter. MyDelayAwaiter implements INotifyCompletion and provides IsCompleted, OnCompleted, and GetResult.
The compiler sees 'await expr', looks for expr.GetAwaiter(), and generates code against the IsCompleted, OnCompleted, and GetResult members. It doesn't care that MyDelayAwaitable isn't Task. It just needs the pattern.

---

## Slide 14: 09
09
- The State Machine
- Deep Dive
What the compiler really does with your async method
22

### Notes
This is the heart of the talk. Everything we've covered - Tasks, the ThreadPool, SynchronizationContext, ExecutionContext, awaiters - comes together in what the compiler does when it sees the async keyword. The compiler rewrites your entire method into a state machine.

---

## Slide 15: Our Example Method
Our Example Method
```csharp
async Task ExampleAsync()
{
    Console.WriteLine("Step 1");
    await Task.Delay(1000);
    Console.WriteLine("Step 2");
    await Task.Delay(500);
    Console.WriteLine("Step 3");
}
```
The compiler splits this into 3 segments at the 2 await points:
Segment A:  Step 1 + start delay
Segment B:  Step 2 + start delay
Segment C:  Step 3 + finish
23

### Notes
Three Console.WriteLine calls. Two await points. This method has three segments of code - the parts between the suspension points.
Segment A: Step 1 and starting the first delay. Segment B: Step 2 and starting the second delay. Segment C: Step 3.
The state machine's job is to execute one segment at a time, suspend if the awaited operation isn't finished yet, and resume at the right segment when it is.

---

## Slide 16: What the Compiler Generates
What the Compiler Generates
Your method becomes a struct with a MoveNext() method and a state field:
IAsyncStateMachine struct
Holds your local variables as fields
<>1__state field
- -1=running, 0/1=suspended, -2=done
AsyncTaskMethodBuilder
Creates the outer Task, captures context
Awaiter fields
One per await, stores awaiter while paused
MoveNext()
Switch on state → runs the right segment
The compiler rewrites your method into a state machine struct with MoveNext().
24

### Notes
The compiler: 1) Creates a struct implementing IAsyncStateMachine. 2) Moves all your method's local variables into fields on that struct - because they can't live on the stack, since the stack frame is gone when the method returns at a suspension point. 3) Adds a state field (<>1__state) that tracks which segment to execute next.
4) Adds an AsyncTaskMethodBuilder field: the infrastructure piece that creates the caller-visible Task, sets its result or exception, and coordinates with SynchronizationContext and ExecutionContext. 5) Adds awaiter fields (one per await expression) to hold the awaiter while suspended. 6) Puts all your code into a MoveNext() method, rewritten as a “switch” on the state field.
7) Replaces your original method with a thin stub that creates the state machine and starts it.

---

## Slide 17: The Stub Method
The Stub Method
Your original method becomes this thin wrapper:
```csharp
Task ExampleAsync()
{
    var sm = new ExampleAsyncStateMachine();
    sm.<>1__state = -1;                       // Initial state
    sm.<>t__builder = AsyncTaskMethodBuilder.Create();
    sm.<>t__builder.Start(ref sm);            // Calls MoveNext()
    return sm.<>t__builder.Task;              // Returns the promise
}
```
- Start() calls MoveNext() synchronously on the calling thread
- The first segment runs immediately - async methods start synchronously
- Returns the builder’s Task to the caller (may be incomplete)
25

### Notes
Notice: Start calls MoveNext() synchronously on the calling thread. The first segment of your code - up to the first await that isn't already complete - on the calling thread, not on a ThreadPool thread. This is an important detail: async methods begin executing synchronously.
The method only 'becomes asynchronous' at the point where it actually suspends.

---

## Slide 18: Inside MoveNext() : First Await
Inside MoveNext() : First Await
/// show actual state machine
If IsCompleted is false: save state, register MoveNext as callback, return. Thread is free.
26

### Notes
Inside MoveNext(), state is -1 on the first entry, so we hit the default branch. We execute the first segment of your code (Step 1), then call Task.Delay(1000).GetAwaiter(). We check IsCompleted - false, because 1000ms hasn't elapsed.
So we: set state to 0, stash the awaiter, and call AwaitUnsafeOnCompleted on the builder. The builder captures SynchronizationContext.Current and ExecutionContext, wraps MoveNext in a callback, and passes it to the awaiter's UnsafeOnCompleted. Then we RETURN from MoveNext - the method is now 'paused.' The thread is released.
The caller gets back an incomplete Task. No thread is blocked. A system timer is ticking.

---

## Slide 19: Inside MoveNext() : Resuming & Second Await
Inside MoveNext() : Resuming & Second Await
///show actual state machine
27

### Notes
When the timer fires (~1000ms later), the continuation is dispatched. MoveNext() runs again with state=0. We retrieve the stashed awaiter, clear the field, reset state to -1 (running).
Jump to AfterFirstAwait. Call GetResult() - if the Task faulted, this throws and the catch block handles it. Otherwise, execute Step 2, get the next awaiter for Task.Delay(500), check IsCompleted (false again), set state=1, stash awaiter, register continuation, return.
Thread is free again.

---

## Slide 20: Completion & Error Handling
Completion & Error Handling
```csharp
    } // end try
    catch (Exception ex)
    {
        <>1__state = -2;               // Terminal state
        <>t__builder.SetException(ex); // Faults the outer Task
        return;
    }
    <>1__state = -2;                   // Terminal state
    <>t__builder.SetResult();          // Completes the outer Task
}
```
- Any exception in your code or GetResult() is caught and set on the outer Task
- On success, SetResult() completes the promise the caller is awaiting
- State -2 means the machine is finished - MoveNext won’t be called again
28

### Notes
Any exception in your code or in GetResult() is caught by the try/catch and set on the outer Task via SetException(). This is what transitions the outer Task to faulted state. On success, SetResult() completes the promise the caller is awaiting.
State -2 is the terminal state - MoveNext won't be called again. The machine is finished.

---

## Slide 21: Step-by-Step Execution
Step-by-Step Execution
Call 1
state=-1 → prints "Step 1" → gets awaiter → not completed → suspends → returns incomplete Task
- ⏱ Timer
No thread blocked. System timer ticking for 1000ms.
Call 2
state=0 → resumes → GetResult() → prints "Step 2" → next awaiter → suspends again
- ⏱ Timer
System timer for 500ms.
Call 3
state=1 → resumes → GetResult() → prints "Step 3" → SetResult() → Task complete
3 calls to MoveNext(). 3 segments. 2 suspension points. 0 threads blocked during waits.
29

### Notes
Let's trace the full execution. Call 1: state=-1, prints Step 1, gets awaiter, not completed, suspends, returns incomplete Task. The calling thread is free.
Timer is ticking. ~1000ms pass. Timer fires.
Call 2: state=0, resumes, GetResult(), prints Step 2, next awaiter, suspends again. ~500ms pass. Timer fires.
Call 3: state=1, resumes, GetResult(), prints Step 3, SetResult(), Task complete. That's the entire lifecycle. Three calls to MoveNext().
Three segments of code. Two suspension points. Zero threads blocked during the waits.

---

## Slide 22: The Fast Path & Allocation Story
The Fast Path & Allocation Story
The Fast Path:  IsCompleted
- If the awaited Task is already done, the state machine skips suspension entirely.
- No context capture, no callback registration, no thread hop. Falls straight through.
The Struct-to-Heap Boxing Story
All awaits complete synchronously
Zero allocations: struct lives on the stack
At least one true suspension
One allocation: struct is boxed onto the heap
Compare: ContinueWith approach
Multiple allocations per step (closures + delegates + Tasks)
```csharp
async/await: a struct, a switch, some callbacks. No magic - just a very clever compiler rewrite.
```
30

### Notes
The Fast Path: Those IsCompleted checks are a critical performance optimisation. If the awaited Task has already completed (cached HTTP responses, buffered stream reads, Task.Delay(0)), the state machine doesn't bother suspending. It skips the state assignment, the awaiter stash, the context capture, the continuation registration; all of it.
Just falls through and continues synchronously. No thread switch, no callback overhead. The Struct-to-Heap Boxing Story: The state machine is declared as a struct, not a class.
It starts on the stack - no heap allocation. If the method runs to completion without ever truly suspending (all awaits hit the IsCompleted fast path), zero allocations. But if it does need to suspend, the builder boxes the struct onto the heap so it can survive beyond the current stack frame.
In the best case: zero allocations. In the typical case: one allocation (the boxed state machine). Compare with ContinueWith: closures, delegates, and Task objects at each step.
The compiler approach is far more efficient. async/await: a struct, a switch, some callbacks. No magic - just a very clever compiler rewrite.

---

## Slide 23: 10
10
ConfigureAwait(false)
When, why, and how it prevents deadlocks
31

### Notes
Now that you understand the state machine, the awaitable pattern, and SynchronizationContext, we can finally explain the one piece of async advice everyone has heard but few can explain: ConfigureAwait(false).

---

## Slide 24: What ConfigureAwait(false) Does
What ConfigureAwait(false) Does
- Tells the infrastructure: skip SynchronizationContext capture.
- Run my continuation on the ThreadPool, regardless of the original context.
Default (captures context)
ConfigureAwait(false)
```csharp
await Task.Delay(1000);
// UI app: runs on UI thread
// Console:  runs on ThreadPool
```
```csharp
await Task.Delay(1000)
    .ConfigureAwait(false);
// ALWAYS runs on ThreadPool
```
- Mechanically: returns a ConfiguredTaskAwaitable whose awaiter skips
- the SynchronizationContext in OnCompleted
ConfigureAwait(false) opts out of context capture; continuations go straight to the ThreadPool.
32

### Notes
Recall: when you await something, the AsyncTaskMethodBuilder captures SynchronizationContext.Current and uses it to schedule the continuation. In a UI app, that means the continuation runs on the UI thread. ConfigureAwait(false) tells the infrastructure: 'I don't need to come back to the original SynchronizationContext.
Run my continuation wherever is most convenient.' In practice, that means the ThreadPool. Mechanically: Task.ConfigureAwait(false) returns a ConfiguredTaskAwaitable - a different struct. This is the awaitable pattern from Section 8 in action.
Its awaiter's OnCompleted implementation deliberately ignores the current SynchronizationContext and schedules the continuation directly to the ThreadPool.

---

## Slide 25: When to Use ConfigureAwait(false)
When to Use ConfigureAwait(false)
Library code
Prevents deadlocks, avoids unnecessary marshalling
USE IT on every await
UI application code
You need to stay on the UI thread to update controls
DON’T use it
ASP.NET Core
No SynchronizationContext exists, but good habit in libraries
Largely a no-op
ConfigureAwait(false) is a correctness and performance tool for library authors, but it comes with maintainability tradeoffs
33

### Notes
Use ConfigureAwait(false) in library code. If you're writing a NuGet package, a shared utility, or any code that doesn't know whether it'll be called from a UI app - use it on every await. Why? 
1) Deadlock prevention: a UI developer calls your library method with .Result, blocking the UI thread.
Your library's continuation tries to post back to the UI thread. Deadlock. With ConfigureAwait(false), your continuation runs on the ThreadPool, so there's no contention.
2) Performance: posting to a SynchronizationContext has overhead - queueing, waking the target thread, context switching.
If you don't need the original context, why pay for it? Don't use it in application-level UI code - if you're in a WPF button click handler and need to update a text box after an await, you need to be back on the UI thread.
In ASP.NET Core, there is no SynchronizationContext, so ConfigureAwait(false) is largely a no-op.
But it's still good practice in library code, because your library might be consumed in a WPF app someday. You need it on EVERY await in library code, not just the first one.Pitfalls of configureAwait false:legacy asp net: continuation is queued to request context. Request context is responsible for value of HttpContext.Current. Using configureAwait(false) avoids the capture of context -> Any code dependent on HttpContext.Current will now get null as its value.AspNetSynchronizationContext would be responsible for reattaching HttpContext.Current value. Not capturing it means HttpContext.Current is null.

---

## Slide 26: 11
11
Putting It All Together
The complete mental model
34

### Notes
Let's put the whole picture together. This is the recap of everything we've covered.

---

## Slide 27: The Complete Picture
The Complete Picture
02
Why async exists
Free threads during waits, not parallelism
03
Before await
Callback hell with ContinueWith
04
Task
A promise (data structure), not a thread
05
ThreadPool
Default engine for work items and continuations
06
SynchronizationContext
Decides where continuations run
07
ExecutionContext
Ambient state flows across thread hops
08
Awaitable pattern
GetAwaiter() - pattern-based, not type-based
09
State machine
Compiler rewrites to struct + MoveNext() + switch
10
ConfigureAwait(false)
Skip context capture - use in libraries
35

### Notes
When you write 'async Task MyMethod()', the compiler generates a state machine - a struct with MoveNext() containing all your code as a switch statement. Each await is a potential suspension point. When execution reaches an await, the state machine asks the awaiter: 'Are you done?' If yes - fast path - it keeps running.
If not, it records state, registers MoveNext as a callback, and returns - freeing the thread. When the awaited operation completes, the builder restores ExecutionContext, and either posts MoveNext to the captured SynchronizationContext or queues it to the ThreadPool. Your code resumes at the right state.
The Task returned to the caller is just a promise - a handle on the outcome. When MoveNext falls through, the builder calls SetResult, and the caller's own await can resume. Every piece has a job: async exists to free threads during waits.
Tasks are promises, not threads. The ThreadPool is the default engine. SynchronizationContext decides where continuations run.
ExecutionContext flows ambient state. The awaitable pattern makes await extensible. The state machine is the compiler's rewrite.
ConfigureAwait(false) opts out of context capture.

---

## Slide 28: ValueTask: Reducing Allocations on Hot Paths
ValueTask: Reducing Allocations on Hot Paths
```csharp
ValueTask<T> is a struct wrapping either a result or a Task<T>
Synchronous completion with cached result: zero allocations
Needs to suspend: falls back to one Task allocation (same as Task<T>)
Used throughout .NET internals (Streams, Sockets, HttpClient)
Stricter usage rules: await once, don't cache, no concurrent awaits
IValueTaskSource: poolable completion source for near-zero alloc
Profile before switching - only worthwhile on proven hot paths
```
```csharp
// Task<T>: always allocates a heap object
async Task<int> GetValue() => await FetchAsync();
// ValueTask<T>: struct - zero allocation if result is already ready
async ValueTask<int> GetValueFast() {
    if (_cache.TryGetValue(key, out var cached))
        return cached;   // No suspension - zero heap allocation
    return await FetchAsync();  // Suspends - allocates one Task
}
// Rules for ValueTask:
// - Await it directly - don't store and await later
// - Never await from multiple callers concurrently
// - IValueTaskSource: fully allocation-free advanced pattern
```
ValueTask<T> is a struct that avoids allocating a Task when the result is already available.
28

### Notes
ValueTask<T> is a struct that wraps either a bare T result value or a Task<T>. The key insight: if the operation completes synchronously and the result is already available, ValueTask<T> can return it without allocating a Task object on the heap. This is a zero-allocation fast path.
When the operation does need to suspend, ValueTask<T> falls back to wrapping a real Task<T>, so you get one heap allocation - same as if you'd used Task<T> directly. The benefit is purely on the synchronous-completion path: cache hits, buffered reads, already-computed results.
ValueTask has stricter usage rules than Task. You must await it exactly once, directly. Don't store it in a variable and await it later. Don't await it from multiple places concurrently. Don't call .Result or .GetAwaiter().GetResult() after it's already been awaited. These restrictions exist because the underlying IValueTaskSource may be pooled and reused.
IValueTaskSource<T> is the advanced pattern for near-zero allocation async operations. Instead of allocating a new Task for each suspension, you can pool and reuse IValueTaskSource instances. This is used throughout .NET's internal I/O stack - Socket.ReceiveAsync, PipeReader.ReadAsync, and similar hot-path APIs.
Profile before switching from Task<T> to ValueTask<T>. The allocation savings only matter on proven hot paths - methods called thousands of times per second where GC pressure is measurable. For most application code, Task<T> is simpler and has no usage restrictions. ValueTask<T> is a performance tool for library authors and framework internals.

---

## Slide 29: The Future of async/await
The Future of async/await
Where We Are
Where We're Headed
- Compiler-generated state machines transform async methods
- Heap allocation when the state machine suspends (boxing)
- SynchronizationContext and ThreadPool as scheduling layers
- ConfigureAwait(false) is a manual concern in library code
- Sophisticated but complex - context capture, scheduler hops, etc.
- Runtime-aware async: .NET team exploring first-class runtime support for continuations
- Green threads / fibers: lighter weight, managed by the runtime scheduler not the OS
- Eliminates heap boxing - state machines can stay on the stack throughout
- Structured concurrency and async streams (IAsyncEnumerable) already in BCL
- dotnet/runtime: async-generator and green-threads proposals actively discussed
```csharp
// Today: compiler transforms async methods into state machine structs, boxes to heap on suspend
// The vision: runtime manages continuations as first-class lightweight fibers
// Result: cheaper suspension, less GC pressure, simpler ConfigureAwait story
// .NET 9 already improved: I/O completion ThreadPool on Windows, IOUring on Linux
// Watch: dotnet/runtime proposals on GitHub for the next chapter of async in .NET
```
async/await today is a compiler trick. Tomorrow, it may be a runtime primitive.
29

### Notes
Everything we've covered today - state machines, context capture, scheduler hops - is fundamentally a compiler trick. The C# compiler transforms your async methods into state machine structs, and the runtime provides the scheduling infrastructure. But the runtime itself has no native concept of 'this is an async operation that was suspended.'
The .NET team has explored making async a first-class runtime primitive. Instead of the compiler generating state machine structs that get boxed onto the heap when they suspend, the runtime itself could manage lightweight continuation frames. This would eliminate the boxing allocation entirely.
Green threads or fibers are another direction being explored - lightweight threads managed by the runtime scheduler rather than the OS. These are cheaper to create and switch between than OS threads. Languages like Go (goroutines) and Kotlin (coroutines) already use this model.
.NET 9 already brought improvements: the I/O completion ThreadPool on Windows was reworked, and Linux got IOUring support for more efficient async I/O at the OS level. IAsyncEnumerable and async streams are already part of the BCL, enabling async iteration patterns.
The vision: cheaper suspension, less GC pressure, and potentially eliminating the need for ConfigureAwait(false) entirely if the runtime can handle context management natively. Watch the dotnet/runtime GitHub repository for the latest proposals on async-generator and green-threads discussions.

---

## Slide 30: Thank you
Thank you

### Notes
You started this talk knowing that async/await is something you put on methods because you're told you must. Now you know what it does, why it exists, and how the compiler and runtime conspire to make it work. The next time you see a deadlock or a mysterious context bug, you’ll hopefully have the mental model to diagnose it, or at least have a starting point on where you should look.