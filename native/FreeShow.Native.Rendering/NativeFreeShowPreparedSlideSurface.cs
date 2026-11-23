using Avalonia.Threading;
using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Physical lyric-surface part of FreeShowPreparedSlideTransitionRuntime.
/// All actual swaps/receipts run on Avalonia's UI thread. This owns no
/// source action engine, schedules no cue, and never touches LibVLC.
/// </summary>
public sealed class NativeFreeShowPreparedSlideSurface : IFreeShowPreparedSlideSurface
{
    private readonly NativeFreeShowOutputSession session;
    public string OutputId => session.OutputId;

    public NativeFreeShowPreparedSlideSurface(NativeFreeShowOutputSession outputSession)
    {
        session = outputSession ?? throw new ArgumentNullException(nameof(outputSession));
    }

    public bool HasPrepared(FreeShowSlideTransitionTarget target,
        FreeShowRevealStyleContext style) =>
        Dispatcher.UIThread.CheckAccess() &&
        NativeSourceSlideVisualPreflight.HasPreparedOriginalTarget(target, session, style);

    public Task PresentAsync(FreeShowSlideTransitionTarget target,
        FreeShowRevealStyleContext style, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        void Present()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasPrepared(target, style) ||
                !session.ShowPrewarmedReveal(target.NextOutSlide, style))
                throw new InvalidOperationException("Exact Native lyric visual is no longer prepared.");
        }

        // The normal operator path already owns the UI thread. Avoid an
        // additional dispatcher turn for the cached cue swap.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Present();
            return Task.CompletedTask;
        }
        return DispatchAsync();
        async Task DispatchAsync() => await Dispatcher.UIThread.InvokeAsync(Present);
    }

    public Task<bool> IsPresentingAsync(FreeShowSlideTransitionTarget target,
        FreeShowRevealStyleContext style, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool Inspect()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return HasPrepared(target, style) &&
                session.IsPresentingPrewarmedReveal(target.NextOutSlide, style);
        }
        if (Dispatcher.UIThread.CheckAccess()) return Task.FromResult(Inspect());
        return DispatchAsync();
        async Task<bool> DispatchAsync() => await Dispatcher.UIThread.InvokeAsync(Inspect);
    }
}
