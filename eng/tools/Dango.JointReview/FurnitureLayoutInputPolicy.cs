using System.Windows;

namespace Dango.JointReview;

internal static class FurnitureLayoutInputPolicy
{
    internal static bool HasSceneFocus(Window owner, FrameworkElement scene, IInputElement? focused) =>
        ReferenceEquals(focused, scene) && ReferenceEquals(Window.GetWindow(scene), owner);
}
