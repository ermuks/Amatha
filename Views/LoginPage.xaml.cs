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

    public LoginPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Loaded += (_, _) => IdBox.Focus();
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
        if (ViewModel.IsBusy)
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
