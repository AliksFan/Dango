using System.Windows.Controls;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private void EnterBeltPorch()
    {
        if (beltRuntime is null || beltPresenter is null) return;
        ClearBeltInput();
        var nativeX = beltSnapshot?.Frame?.CoreX ?? 0;
        var boundary = beltSnapshot?.Frame?.Curve;
        var left = boundary is null ? -80 : Enumerable.Range(0, boundary.Length / 2)
            .Min(i => (boundary[2 * i] - nativeX) * BeltBodyAppearancePresenter.WorldToCanvas);
        var right = boundary is null ? 80 : Enumerable.Range(0, boundary.Length / 2)
            .Max(i => (boundary[2 * i] - nativeX) * BeltBodyAppearancePresenter.WorldToCanvas);
        porchResumeX = Math.Clamp(porchResumeX, 24 - left, PorchSceneView.WorldWidth - 24 - right);
        beltPorchOriginX = porchResumeX - nativeX * BeltBodyAppearancePresenter.WorldToCanvas;
        LiveEntityLayer.Children.Remove(beltPresenter.View);
        PorchScene.AttachBeltBody(beltPresenter.View, porchResumeX);
        ObserveBeltOperation(beltRuntime.SetBoundsAsync((24 - beltPorchOriginX) / BeltBodyAppearancePresenter.WorldToCanvas,
            (PorchSceneView.WorldWidth - 24 - beltPorchOriginX) / BeltBodyAppearancePresenter.WorldToCanvas));
        beltPendingDisplay = null;
        beltLastPresentedSequence = -1;
        RefreshBeltVisibility();
    }

    private void LeaveBeltPorch()
    {
        if (beltRuntime is null || beltPresenter is null) return;
        ClearBeltInput();
        PorchScene.DetachBeltBody();
        if (!LiveEntityLayer.Children.Contains(beltPresenter.View)) LiveEntityLayer.Children.Add(beltPresenter.View);
        Canvas.SetLeft(beltPresenter.View, 0);
        ObserveBeltOperation(beltRuntime.SetBoundsAsync(-256, 256));
        beltPendingDisplay = null;
        beltLastPresentedSequence = -1;
        RefreshBeltVisibility();
    }
}
