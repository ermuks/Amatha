using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Amaranth10API.Views;

public partial class ConfirmDialog : UserControl
{
    private Action<bool>? _completed;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static bool Ask(Window owner, string title, string message)
    {
        if (owner is MainWindow mainWindow)
        {
            return mainWindow.ShowConfirm(title, message);
        }

        return AskFallback(owner, title, message);
    }

    public void Prepare(string title, string message, Action<bool> completed)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _completed = completed;
    }

    public void Complete(bool confirmed)
    {
        Action<bool>? completed = _completed;
        _completed = null;
        completed?.Invoke(confirmed);
    }

    private void Yes_Click(object sender, RoutedEventArgs e)
    {
        Complete(true);
    }

    private void No_Click(object sender, RoutedEventArgs e)
    {
        Complete(false);
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Complete(false);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Complete(true);
        }
    }

    private static bool AskFallback(Window? owner, string title, string message)
    {
        bool confirmed = false;
        Window window = new()
        {
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            Width = 420,
            Height = 240,
            WindowStartupLocation = owner == null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner
        };

        ConfirmDialog dialog = new();
        dialog.Prepare(title, message, result =>
        {
            confirmed = result;
            window.Close();
        });
        window.Content = dialog;
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                confirmed = false;
                window.Close();
            }
        };
        window.ShowDialog();
        return confirmed;
    }
}
