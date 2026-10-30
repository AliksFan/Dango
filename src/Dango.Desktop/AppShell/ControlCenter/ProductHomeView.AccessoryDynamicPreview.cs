using System.Windows;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.AppShell.ControlCenter;

public partial class ProductHomeView
{
    private HomeAccessoryDynamicPreview? accessoryDynamicPreview;
    private bool IsAccessoryDynamicPreviewPaused => accessoryDynamicPreview is { Visible: true, Paused: true };

    internal IAccessoryDynamicPreview CreateAccessoryDynamicPreview()
    {
        accessoryDynamicPreview?.Dispose();
        return accessoryDynamicPreview = new HomeAccessoryDynamicPreview(this);
    }

    /// <summary>Uses the home's sole owner and sole rendering callback, including while its modal child has focus.</summary>
    private sealed class HomeAccessoryDynamicPreview : IAccessoryDynamicPreview
    {
        private readonly ProductHomeView home;
        private readonly AccessoryDynamicPreviewInput input;
        private bool disposed;
        private double cameraX;
        internal bool Visible { get; private set; }
        internal bool Paused { get; private set; }

        internal HomeAccessoryDynamicPreview(ProductHomeView home)
        {
            this.home = home;
            input = new(value => home.beltRuntime?.PublishInput(value));
            cameraX = home.beltSnapshot?.Frame?.CoreX ?? 0;
        }

        public byte[]? Capture()
        {
            home.Dispatcher.VerifyAccess();
            if (disposed || !Visible || home.beltFatalError || home.beltPresenter is null || home.beltSnapshot?.Frame is not { } frame)
                return null;
            // Follow only when the body approaches an edge; ordinary movement stays visible.
            var maximumOffset = 80 / (BeltBodyAppearancePresenter.WorldToCanvas * AccessoryDynamicPreviewImage.Scale);
            cameraX = Math.Clamp(cameraX, frame.CoreX - maximumOffset, frame.CoreX + maximumOffset);
            var ground = DangoBodyMaterial.Configure(home.beltSnapshot.Body).GroundHeight;
            var left = AccessoryDynamicPreviewImage.Width / 2d - 80 * AccessoryDynamicPreviewImage.Scale +
                (frame.CoreX - cameraX) * BeltBodyAppearancePresenter.WorldToCanvas * AccessoryDynamicPreviewImage.Scale;
            var top = AccessoryDynamicPreviewImage.Floor - 66 * AccessoryDynamicPreviewImage.Scale +
                (ground - frame.CoreY) * BeltBodyAppearancePresenter.WorldToCanvas * AccessoryDynamicPreviewImage.Scale;
            return AccessoryDynamicPreviewImage.Capture(home.beltPresenter.View, left, top);
        }

        public void SetVisible(bool visible)
        {
            home.Dispatcher.VerifyAccess();
            if (disposed || visible == Visible) return;
            input.Clear(); home.ClearBeltInput();
            Visible = visible;
            if (!visible) Paused = false;
            home.RefreshBeltVisibility();
        }

        public void Command(string command, double x = 0, double y = 0)
        {
            home.Dispatcher.VerifyAccess();
            if (disposed || !Visible || home.beltRuntime is null || home.beltFatalError || !double.IsFinite(x) || !double.IsFinite(y)) return;
            switch (command)
            {
                case "pause":
                    input.Clear(); Paused = true; home.RefreshBeltVisibility(); return;
                case "resume":
                    input.Clear(); Paused = false; home.RefreshBeltVisibility(); return;
                case "reset":
                    input.Clear(); home.ClearBeltInput();
                    home.ObserveBeltOperation(ResetAndPresentAsync());
                    return;
            }
            if (Paused || !home.beltVisible) return;
            var ground = DangoBodyMaterial.Configure(home.beltSnapshot?.Body ?? home.DraftAppearance.Body).GroundHeight;
            var factor = BeltBodyAppearancePresenter.WorldToCanvas * AccessoryDynamicPreviewImage.Scale;
            var point = new Point(cameraX + (Math.Clamp(x, -2, 3) * AccessoryDynamicPreviewImage.Width - AccessoryDynamicPreviewImage.Width / 2d) / factor,
                ground + (AccessoryDynamicPreviewImage.Floor - Math.Clamp(y, -2, 3) * AccessoryDynamicPreviewImage.Height) / factor);
            input.Command(command, point);
        }

        private async Task ResetAndPresentAsync()
        {
            var runtime = home.beltRuntime!;
            await runtime.ResetPoseAsync();
            await home.Dispatcher.InvokeAsync(() =>
            {
                if (disposed || !Visible || !ReferenceEquals(home.beltRuntime, runtime) ||
                    home.beltFatalError || home.beltPresenter is null) return;
                cameraX = 0;
                home.beltAppearanceFrame = null;
                home.beltPresenter.Reset();
                home.beltLastPresentedSequence = -1;
                home.beltPendingDisplay = null;
                home.beltPrimed = false;
                // The regular rendering callback consumes running frames. Only an explicitly
                // paused preview uses this one-shot presentation, with no UI-side physics step.
                if (!Paused || home.beltVisible || !runtime.TryDequeue(out var reset) ||
                    reset?.Frame is null || reset.Error is not null || reset.Epoch != runtime.Epoch) return;
                home.beltPresenter.Paused = false;
                try { home.PresentBeltBodyBatch([reset], reset.FixedDt); }
                finally { home.beltPresenter.Paused = true; }
            });
        }

        public void Dispose()
        {
            if (disposed) return;
            home.Dispatcher.VerifyAccess();
            SetVisible(false);
            disposed = true;
            if (ReferenceEquals(home.accessoryDynamicPreview, this)) home.accessoryDynamicPreview = null;
        }
    }
}
