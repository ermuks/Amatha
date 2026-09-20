using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Amaranth10API.Helpers;
using Amaranth10API.Models;
using Amaranth10API.ViewModels;

namespace Amaranth10API.Views;

public partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; } = new();

    private Task<bool>? _networkWait;
    private CancellationTokenSource? _networkWaitCts;
    private bool _networkReady;

    public LoginPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += LoginPage_Loaded;
        Unloaded += LoginPage_Unloaded;
    }

    public Task<bool> EnsureNetworkAsync()
    {
        return _networkWait ??= WaitForNetworkAsync();
    }

    public void CancelNetworkWait()
    {
        try
        {
            _networkWaitCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async void LoginPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= LoginPage_Loaded;
        bool ready = await EnsureNetworkAsync();
        if (ready && ViewModel.CanEditLogin)
        {
            IdBox.Focus();
        }
    }

    private void LoginPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Unloaded -= LoginPage_Unloaded;
        NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
        CancelNetworkWait();
    }

    private async Task<bool> WaitForNetworkAsync()
    {
        if (NetworkProbe.HasAdapter && await TryReachAsync(CancellationToken.None))
        {
            return true;
        }

        CancellationTokenSource cts = new();
        _networkWaitCts = cts;
        _networkReady = false;
        ViewModel.BeginNetworkWait();
        NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;

        try
        {
            while (!cts.IsCancellationRequested && !_networkReady)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_networkReady)
                {
                    break;
                }

                if (await TryReachAsync(cts.Token))
                {
                    _networkReady = true;
                    break;
                }
            }
        }
        finally
        {
            NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
            if (ReferenceEquals(_networkWaitCts, cts))
            {
                _networkWaitCts = null;
            }

            cts.Dispose();
        }

        ViewModel.EndNetworkWait();
        if (_networkReady)
        {
            return true;
        }

        IdBox.Focus();
        return false;
    }

    private async Task<bool> TryReachAsync(CancellationToken token)
    {
        try
        {
            return await NetworkProbe.CanReachAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void NetworkAvailabilityChanged(object sender, NetworkAvailabilityEventArgs e)
    {
        CancellationTokenSource? cts = _networkWaitCts;
        if (!e.IsAvailable || cts == null || cts.IsCancellationRequested)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (!ReferenceEquals(_networkWaitCts, cts) || cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                if (await TryReachAsync(cts.Token))
                {
                    _networkReady = true;
                    if (ReferenceEquals(_networkWaitCts, cts) && !cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void CancelWait_Click(object sender, RoutedEventArgs e)
    {
        CancelNetworkWait();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
    }

    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await TryLoginAsync();
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        await TryLoginAsync();
    }

    private async Task TryLoginAsync()
    {
        if (!ViewModel.CanEditLogin)
        {
            return;
        }

        ViewModel.Password = PasswordBox.Password;
        ViewModel.ErrorMessage = string.Empty;
        ViewModel.IsBusy = true;
        ViewModel.StatusMessage = "로그인하는 중...";

        try
        {
            AmaranthSession session = await App.Client.LoginAsync(ViewModel.LoginId.Trim(), ViewModel.Password);
            ViewModel.StatusMessage = "데이터를 준비하는 중...";
            AppRuntime.Current.RememberLogin(ViewModel.LoginId.Trim(), ViewModel.Password, session);
            if (Window.GetWindow(this) is MainWindow window)
            {
                window.NavigateToDashboard(session);
            }
        }
        catch (Exception exception)
        {
            ViewModel.ErrorMessage = exception.Message;
            ViewModel.StatusMessage = string.Empty;
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }
}
