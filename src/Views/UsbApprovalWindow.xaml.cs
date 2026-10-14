using System.Windows;
using System.Windows.Controls;
using USBBlockManager.Models;

namespace USBBlockManager.Views;

public partial class UsbApprovalWindow : Window
{
    private string _password = string.Empty;

    public UsbApprovalWindow(UsbDevice device)
    {
        InitializeComponent();

        DeviceLabelText.Text = string.IsNullOrEmpty(device.Label)
            ? "(без модели)"
            : device.Label;
        DeviceVidPidText.Text = $"VID: {device.Vid}    PID: {device.Pid}";
        DeviceSerialText.Text = $"Серийный номер: {device.Serial}";
        DeviceDriveText.Text = string.IsNullOrEmpty(device.DriveLetter)
            ? "Буква диска: (не назначена)"
            : $"Буква диска: {device.DriveLetter}";

        Device = device;
        PasswordBox.Focus();
    }

    /// <summary>Устройство, для которого показывается диалог.</summary>
    public UsbDevice Device { get; }

    /// <summary>Разрешил ли пользователь устройство.</summary>
    public bool IsApproved { get; private set; }

    /// <summary>Добавить ли в whitelist.</summary>
    public bool AddToWhitelist => AddToWhitelistCheck.IsChecked == true;

    /// <summary>Введённый пароль.</summary>
    public string Password => _password;

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
            _password = pb.Password;
    }

    private void AllowButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_password))
        {
            ErrorText.Text = "Введите пароль администратора.";
            return;
        }

        IsApproved = true;
        DialogResult = true;
        Close();
    }

    private void BlockButton_OnClick(object sender, RoutedEventArgs e)
    {
        IsApproved = false;
        DialogResult = false;
        Close();
    }
}