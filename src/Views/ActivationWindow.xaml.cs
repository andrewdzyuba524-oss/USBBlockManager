using System.Windows;
using USBBlockManager.ViewModels;

namespace USBBlockManager.Views;

public partial class ActivationWindow : Window
{
    private readonly ActivationViewModel _viewModel;

    public ActivationWindow(ActivationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.ActivationSucceeded += OnActivationSucceeded;
        _viewModel.ExitRequested += OnExitRequested;
    }

    private void OnActivationSucceeded(object? sender, EventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }
}