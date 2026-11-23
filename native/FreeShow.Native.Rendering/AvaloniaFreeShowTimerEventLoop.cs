using Avalonia.Threading;
using FreeShow.Native.Core;

namespace FreeShow.Native.Rendering;

/// <summary>
/// Queues .NET timer notifications onto the same Avalonia UI dispatcher as
/// Native output commands. Post deliberately never runs the callback inline.
/// Constructing this adapter alone does not start any timers or outputs.
/// </summary>
public sealed class AvaloniaFreeShowTimerEventLoop : IFreeShowTimerEventLoop
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
    public void Post(Action callback) => Dispatcher.UIThread.Post(callback);
}
