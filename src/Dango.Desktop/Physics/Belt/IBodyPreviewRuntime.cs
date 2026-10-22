using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Physics.Belt;

/// <summary>
/// The editor's asynchronous body owner. Pause invalidates old input/poses immediately;
/// its task acknowledges the in-flight step. Only dequeued successful snapshots may
/// be acknowledged as displayed. Close releases native resources on their owner thread.
/// Snapshot/input data retain the accepted body-frame contract; a different frame
/// format requires an explicit adapter rather than reinterpretation by the page.
/// </summary>
internal interface IBodyPreviewRuntime : IAsyncDisposable
{
    long Epoch { get; }
    BeltBodyMetrics Metrics { get; }
    Task ConfigureAsync(DangoBodyControls body, long revision, long bodyEpoch);
    Task<long> PauseAsync();
    Task<bool> ResumeAsync();
    Task SetBoundsAsync(double left, double right);
    void PublishInput(DangoBeltInput input);
    void ClearInput();
    bool TryDequeue(out BeltBodySnapshot? snapshot);
    bool MarkDisplayed(BeltBodySnapshot snapshot);
    Task CloseAsync();
}
