using System;
using System.ComponentModel;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using USBBlockManager.ViewModels;

namespace USBBlockManager;

public partial class MainWindow : Window
{
    // ============================================================
    // Секретный токен. Должен совпадать с токеном в installer\setup.iss
    // (константа UninstallToken). Именно его мы передаём установщику
    // в командной строке, чтобы он разрешил удаление.
    //
    // ВАЖНО: это защита от "случайного" удаления обычным пользователем,
    // а не от админа с Revo Uninstaller. Revo удаляет файлы напрямую,
    // игнорируя unins000.exe.
    // ============================================================
    private const string UninstallToken = "{7E2A-91F4-BD03-USB-BLOCK-2026}";

    private bool _isShuttingDown;
    private bool _isUninstalling;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Closed += (_, _) =>
        {
            if (viewModel is IDisposable d)
                d.Dispose();
        };
    }

    /// <summary>
    /// Позволяет App корректно завершить приложение, не блокируясь в OnClosing.
    /// </summary>
    public void PrepareForShutdown()
    {
        _isShuttingDown = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void ExitMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
            app.TryExitWithPasswordFromMenu();
    }

    private void ChangePasswordMenu_OnClick(object sender, RoutedEventArgs e)
    {
        var authService = new USBBlockManager.Security.AuthService(
            USBBlockManager.Services.AppPaths.AdminHashFile);

        var vm = new USBBlockManager.ViewModels.ChangePasswordViewModel(authService);
        var window = new USBBlockManager.Views.ChangePasswordWindow(vm)
        {
            Owner = this,
        };

        if (window.ShowDialog() == true)
        {
            MessageBox.Show(
                "Пароль успешно изменён.",
                "USB Block Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void AboutMenu_OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "USB Block Manager\n\nБлокировка USB-накопителей с белым списком.\nВерсия 0.1 (dev).",
            "О программе",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Пункт меню «Файл → Удалить программу...».
    /// 1. Запрашивает пароль администратора.
    /// 2. Запускает unins000.exe с секретным токеном.
    /// Без токена установщик откажется удалять (см. setup.iss).
    /// </summary>
    private void UninstallMenu_OnClick(object sender, RoutedEventArgs e)
    {
        // Защита от повторного запуска удаления
        if (_isUninstalling)
            return;

        // --- 1. Запрос пароля администратора ---
        var authService = new USBBlockManager.Security.AuthService(
            USBBlockManager.Services.AppPaths.AdminHashFile);

        var prompt = new USBBlockManager.Views.AdminPasswordPrompt(authService)
        {
            Owner = this
        };

        if (prompt.ShowDialog() != true)
            return; // пароль не введён или неверный

        // --- 2. Подтверждение ---
        var confirm = MessageBox.Show(
            "Удалить USB Block Manager?\n\n" +
            "Служба и задача планировщика будут остановлены и удалены,\n" +
            "все настройки и белый список будут стёрты.",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        // --- 3. Поиск unins000.exe рядом с GUI ---
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var uninstaller = System.IO.Path.Combine(exeDir, "unins000.exe");

        if (!System.IO.File.Exists(uninstaller))
        {
            MessageBox.Show(
                "Не найден файл удаления (unins000.exe).\n" +
                "Возможно, программа запущена не из установленной версии.",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // --- 4. Подготовка к закрытию GUI ---
        // Делаем это ДО запуска установщика, чтобы OnClosing не заблокировал
        // закрытие окна, пока установщик уже работает.
        _isUninstalling = true;
        PrepareForShutdown();

        // --- 5. Запуск установщика с токеном ---
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = uninstaller,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PASSWORD={UninstallToken}",
                UseShellExecute = false
            };

            System.Diagnostics.Process.Start(psi);

            // --- 6. Корректно закрываем GUI, чтобы он не мешал удалению ---
            if (Application.Current is App app)
            {
                app.ShutdownSafely();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            // Откатываем флаги, чтобы пользователь мог попробовать снова
            _isUninstalling = false;
            _isShuttingDown = false;

            MessageBox.Show(
                "Не удалось запустить удаление:\n\n" + ex.ToString(),
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}