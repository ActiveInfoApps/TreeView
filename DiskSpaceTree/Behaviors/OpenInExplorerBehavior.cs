using System.Diagnostics;
using Microsoft.Maui.Controls;

#if WINDOWS
using Microsoft.Maui.Controls.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
#endif

namespace DiskSpaceTree.Behaviors;

#if WINDOWS
public class OpenInExplorerBehavior : PlatformBehavior<Microsoft.Maui.Controls.Grid, UIElement>
{
    private Microsoft.Maui.Controls.Grid? _bindable;

    protected override void OnAttachedTo(Microsoft.Maui.Controls.Grid bindable, UIElement platformView)
    {
        base.OnAttachedTo(bindable, platformView);
        _bindable = bindable;

        if (platformView is FrameworkElement frameworkElement)
        {
            frameworkElement.RightTapped += OnRightTapped;
        }
    }

    protected override void OnDetachedFrom(Microsoft.Maui.Controls.Grid bindable, UIElement platformView)
    {
        base.OnDetachedFrom(bindable, platformView);
        _bindable = null;

        if (platformView is FrameworkElement frameworkElement)
        {
            frameworkElement.RightTapped -= OnRightTapped;
        }
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_bindable?.BindingContext is not ViewModels.TreeViewItem item)
        {
            return;
        }

        e.Handled = true;

        var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();
        var menuItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Open in Explorer" };
        menuItem.Click += (s, args) =>
        {
            OpenInExplorer(item.Node.Path);
        };
        menu.Items.Add(menuItem);

        if (sender is FrameworkElement element)
        {
            menu.ShowAt(element, e.GetPosition(element));
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open explorer: {ex.Message}");
        }
    }
}
#else
public class OpenInExplorerBehavior : Behavior<Microsoft.Maui.Controls.Grid>
{
    // No-op on non-Windows platforms.
}
#endif
