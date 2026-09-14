using System.Windows;
using Amaranth10API.Helpers;

namespace Amaranth10API.Views;

public partial class TrayMenuWindow : Window
{
    public event Action? ShowGuiRequested;
    public event Action? ExitRequested;

    private DateTime _openedUtc = DateTime.UtcNow;

    public TrayMenuWindow()
    {
        InitializeComponent();
        ContentRendered += (_, _) => _openedUtc = DateTime.UtcNow;
    }

    public void PositionNearTray()
    {
        UpdateLayout();
        Point cursor = ScreenPlacement.CursorDip();
        Rect work = ScreenPlacement.WorkingAreaDip();
        double width = ActualWidth;
        double height = ActualHeight;
        double left = cursor.X - width + 16;
        double top = cursor.Y - height + 8;

        if (left < work.Left + 8)
        {
            left = work.Left + 8;
        }

        if (left + width > work.Right - 8)
        {
            left = work.Right - width - 8;
        }

        if (top + height > work.Bottom - 8)
        {
            top = work.Bottom - height - 8;
        }

        if (top < work.Top + 8)
        {
            top = cursor.Y + 8;
        }

        Left = left;
        Top = top;
        _openedUtc = DateTime.UtcNow;
    }

    private void ShowGui_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ShowGuiRequested?.Invoke();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ExitRequested?.Invoke();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if ((DateTime.UtcNow - _openedUtc).TotalMilliseconds < 300)
        {
            ScreenPlacement.StealFocus(this);
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsVisible)
            {
                Close();
            }
        }));
    }
}
