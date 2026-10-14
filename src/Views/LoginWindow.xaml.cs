using System;
using System.Windows;
using System.Windows.Controls;
using USBBlockManager.ViewModels;

namespace USBBlockManager.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void LoginPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.Password = pb.Password;
    }

    private void SetupPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.Password = pb.Password;
    }

    private void SetupConfirmBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.PasswordConfirm = pb.Password;
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }
}