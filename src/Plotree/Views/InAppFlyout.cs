using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Plotree.Services;
using Windows.Foundation;

namespace Plotree.Views;

/// <summary>Applies the same below-item, root-constrained policy to toolbar and native MenuBar flyouts.</summary>
public static class InAppFlyout
{
    private sealed record Configuration(FrameworkElement Owner, Style? OriginalStyle);
    private static readonly ConditionalWeakTable<FlyoutBase, Configuration> Configured = new();

    public static void ConfigureTree(DependencyObject element)
    {
        if (element is Control control) control.ApplyTemplate();
        if (element is Popup popup) popup.ShouldConstrainToRootBounds = true;
        if (element is FrameworkElement owner)
        {
            // Native MenuBarItem exposes its internal menu through ContentButton.ContextFlyout.
            if (owner is Button && owner.ContextFlyout is { } context) Configure(owner, context);
            if (owner is Button { Flyout: { } buttonFlyout }) Configure(owner, buttonFlyout);
            if (owner is SplitButton { Flyout: { } splitFlyout }) Configure(owner, splitFlyout);
        }
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
            ConfigureTree(VisualTreeHelper.GetChild(element, index));
    }

    /// <summary>ComboBox dropdowns use a template Popup rather than FlyoutBase.</summary>
    public static void ConfigureDropDown(ComboBox owner)
    {
        var popup = FindPopup(owner);
        if (popup is null || owner.XamlRoot.Content is not FrameworkElement root) return;
        var position = owner.TransformToVisual(root).TransformPoint(new Point(0, 0));
        var bounds = MenuPlacementCalculator.BelowItem(root.ActualWidth, root.ActualHeight,
            position.X, position.Y, owner.ActualHeight, double.MaxValue, double.MaxValue);
        popup.ShouldConstrainToRootBounds = true;
        popup.PlacementTarget = owner;
        popup.DesiredPlacement = PopupPlacementMode.BottomEdgeAlignedLeft;
        popup.HorizontalOffset = 0;
        popup.VerticalOffset = 0;
        if (popup.Child is FrameworkElement content)
        {
            content.MinWidth = 0;
            content.MinHeight = 0;
            content.MaxWidth = bounds.Width;
            content.MaxHeight = bounds.Height;
        }
    }

    private static Popup? FindPopup(DependencyObject element)
    {
        if (element is Popup popup) return popup;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var match = FindPopup(VisualTreeHelper.GetChild(element, index));
            if (match is not null) return match;
        }
        return null;
    }

    public static void Configure(FrameworkElement owner, FlyoutBase flyout)
    {
        if (Configured.TryGetValue(flyout, out _)) return;
        var originalStyle = flyout switch
        {
            MenuFlyout menu => menu.MenuFlyoutPresenterStyle,
            Flyout content => content.FlyoutPresenterStyle,
            _ => null,
        };
        var configuration = new Configuration(owner, originalStyle);
        Configured.Add(flyout, configuration);
        flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft;
        flyout.ShouldConstrainToRootBounds = true;
        flyout.Opening += (_, _) => ApplyBounds(flyout, configuration);
    }

    private static MenuPlacement ApplyBounds(FlyoutBase flyout, Configuration configuration)
    {
        var owner = configuration.Owner;
        var root = (FrameworkElement)owner.XamlRoot.Content;
        var position = owner.TransformToVisual(root).TransformPoint(new Point(0, 0));
        var requestedWidth = flyout is Flyout { Content: FrameworkElement content } && double.IsFinite(content.Width)
            ? content.Width + 32 : 320;
        var bounds = MenuPlacementCalculator.BelowItem(root.ActualWidth, root.ActualHeight,
            position.X, position.Y, owner.ActualHeight, requestedWidth, double.MaxValue);
        var style = new Style(flyout is MenuFlyout ? typeof(MenuFlyoutPresenter) : typeof(FlyoutPresenter))
        {
            BasedOn = configuration.OriginalStyle,
        };
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, bounds.Width));
        style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, bounds.Height));
        if (flyout is MenuFlyout menu) menu.MenuFlyoutPresenterStyle = style;
        else if (flyout is Flyout contentFlyout)
        {
            contentFlyout.FlyoutPresenterStyle = style;
            if (contentFlyout.Content is FrameworkElement contentElement)
                contentElement.MaxWidth = Math.Max(0, bounds.Width - 32);
        }
        return bounds;
    }

    public static void ShowBelowItem(FrameworkElement owner, MenuFlyout flyout)
    {
        Configure(owner, flyout);
        var bounds = ApplyBounds(flyout, Configured.GetValue(flyout, _ => throw new InvalidOperationException()));
        flyout.ShowAt((FrameworkElement)owner.XamlRoot.Content, new FlyoutShowOptions
        {
            Position = new Point(bounds.X, bounds.Y),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        });
    }
}
