using System.Windows;
using System.Windows.Controls;
using USBBlockManager.ViewModels;

namespace USBBlockManager.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly ChangePasswordViewModel _viewModel;

    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.RequestClose += OnRequestClose;
    }

    private void CurrentPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.CurrentPassword = pb.Password;
    }

    private void NewPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.NewPassword = pb.Password;
    }

    private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _viewModel.ConfirmPassword = pb.Password;
    }

    private void OnRequestClose(object? sender, bool success)
    {
        DialogResult = success;
        Close();
    }
}