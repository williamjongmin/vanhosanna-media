namespace FreeShow.Native.Core;

/// <summary>
/// FIFO admission on the source event loop. Each command starts on that
/// loop only after the preceding asynchronous command has settled. The
/// caller queues INTENT, not stale precomputed output plans. No coalescing,
/// implicit retries or latest-wins replacement of rapid key presses.
/// Clear/restore and timer commands should share this same queue.
/// </summary>
public sealed class FreeShowNativeCommandQueue : IDisposable
{
    private readonly IFreeShowTimerEventLoop eventLoop;
    private readonly object sync = new();
    private readonly Queue<Request> pending = new();
    private Request? active;
    private bool posted;
    private bool disposed;
    private Exception? loopFailure;

    public FreeShowNativeCommandQueue(IFreeShowTimerEventLoop sourceEventLoop)
    {
        eventLoop = sourceEventLoop ?? throw new ArgumentNullException(nameof(sourceEventLoop));
        RequireAccess();
    }

    public int OutstandingCount
    {
        get { lock (sync) return pending.Count + (active == null ? 0 : 1); }
    }

    public Task<T> Enqueue<T>(Func<CancellationToken, Task<T>> command,
        CancellationToken cancellationToken = default)
    {
        RequireAccess();
        ArgumentNullException.ThrowIfNull(command);
        var request = new Request<T>(command, cancellationToken);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (loopFailure != null) return Task.FromException<T>(loopFailure);
            pending.Enqueue(request);
        }
        PostNext();
        return request.Result;
    }

    private void PostNext()
    {
        lock (sync)
        {
            if (disposed || loopFailure != null || posted || active != null || pending.Count == 0)
                return;
            posted = true;
        }
        try { eventLoop.Post(StartNext); }
        catch (Exception error) { FailLoop(error); }
    }

    private void StartNext()
    {
        if (!eventLoop.CheckAccess())
        {
            FailLoop(new InvalidOperationException("Source event loop invoked a command on the wrong thread."));
            return;
        }
        Request request;
        lock (sync)
        {
            posted = false;
            if (disposed || loopFailure != null || active != null || pending.Count == 0) return;
            request = pending.Dequeue();
            active = request;
        }

        Task operation = request.Start(); // Captures synchronous throws too.
        if (operation.IsCompleted) Finish(request);
        else _ = ObserveAsync(request, operation);
    }

    private async Task ObserveAsync(Request request, Task operation)
    {
        // Observe faults even if shutdown makes the subsequent Post fail.
        // Only Finish publishes the typed outcome and starts another cue.
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { }
        try { eventLoop.Post(() => Finish(request)); }
        catch (Exception error) { FailLoop(error); }
    }

    private void Finish(Request request)
    {
        if (!eventLoop.CheckAccess())
        {
            FailLoop(new InvalidOperationException("Source event loop completed a command on the wrong thread."));
            return;
        }
        lock (sync)
        {
            if (!ReferenceEquals(active, request)) return;
            active = null;
        }
        request.Complete();
        PostNext();
    }

    private void FailLoop(Exception error)
    {
        Request[] abandoned;
        Exception failure;
        lock (sync)
        {
            if (loopFailure != null) return;
            failure = loopFailure = new InvalidOperationException(
                "Source command event loop failed. An in-flight source action may still require reconciliation.", error);
            abandoned = active == null ? pending.ToArray() : pending.Prepend(active).ToArray();
            active = null;
            pending.Clear();
            posted = false;
        }
        foreach (var request in abandoned) request.Fail(failure);
    }

    /// <summary>
    /// Cancel queued, unstarted intents only. An active physical command
    /// must finish or reconcile through its own cancellation/result contract.
    /// Dispose timers first; keep the owner loop alive until active work ends.
    /// </summary>
    public void Dispose()
    {
        RequireAccess();
        Request[] unstarted;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            unstarted = pending.ToArray();
            pending.Clear();
        }
        foreach (var request in unstarted) request.CancelUnstarted();
    }

    internal void RequireAccess()
    {
        if (!eventLoop.CheckAccess())
            throw new InvalidOperationException("Native commands must be admitted on the source event loop.");
    }

    private abstract class Request
    {
        public abstract Task Start();
        public abstract void Complete();
        public abstract void Fail(Exception error);
        public abstract void CancelUnstarted();
    }

    private sealed class Request<T>(Func<CancellationToken, Task<T>> execute, CancellationToken token) : Request
    {
        private readonly TaskCompletionSource<T> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<T>? operation;
        public Task<T> Result => completion.Task;

        public override Task Start()
        {
            try
            {
                token.ThrowIfCancellationRequested();
                operation = execute(token) ?? throw new InvalidOperationException("Native command returned no task.");
            }
            catch (OperationCanceledException error)
            {
                // A synchronous cancellation need not carry a cancelled token.
                var cancelled = new TaskCompletionSource<T>();
                cancelled.SetCanceled(error.CancellationToken);
                operation = cancelled.Task;
            }
            catch (Exception error) { operation = Task.FromException<T>(error); }
            return operation;
        }

        public override void Complete()
        {
            try { completion.TrySetResult(operation!.GetAwaiter().GetResult()); }
            catch (OperationCanceledException error) { completion.TrySetCanceled(error.CancellationToken); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        public override void Fail(Exception error) => completion.TrySetException(error);
        public override void CancelUnstarted() => completion.TrySetCanceled();
    }
}
