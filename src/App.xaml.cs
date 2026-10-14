using System.IO;
using System.Threading;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using USBBlockManager.Licensing;
using USBBlockManager.Security;
using USBBlockManager.Services;
using USBBlockManager.ViewModels;
using USBBlockManager.Views;

namespace USBBlockManager;

public partial class App : Application
{
    private static readonly string CrashLogPath =
        Path.Combine(Path.GetTempPath(), "usbblock-crash.log");

    private static readonly string TraceLogPath =
        Path.Combine(Path.GetTempPath(), "usbblock-trace.log");

    private Mutex? _singleInstanceMutex;
    private TrayIcon? _trayIcon;
    private AuthService? _authService;
    private ServiceClient? _serviceClient;
    private MainWindow? _mainWindow;
    private bool _isWindowVisible;

    /// <summary>Текущий статус лицензии (после проверки при старте).</summary>
    public LicenseStatus? LicenseStatus { get; private set; }

    public App()
    {
        Trace("App constructor");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            WriteCrash(args.ExceptionObject as Exception, "AppDomain.UnhandledException");

        DispatcherUnhandledException += (s, args) =>
        {
            WriteCrash(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Trace("OnStartup started");

        // 1. Одноэкземплярность
        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: "USBBlockManager_GUI_SingleInstance",
                createdNew: out bool createdNew);

            Trace($"Mutex created, createdNew={createdNew}");

            if (!createdNew)
            {
                Trace("Mutex already taken, showing message");

                MessageBox.Show(
                    "USB Block Manager уже запущен.\n\nПроверьте иконку в системном трее.",
                    "USB Block Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                Shutdown();
                return;
            }
        }
        catch (Exception ex)
        {
            Trace($"Mutex exception: {ex}");
            WriteCrash(ex, "Mutex");
        }

        Trace("Setting ShutdownMode");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            Trace("Checking AppPaths.CanWrite");
            if (!AppPaths.CanWrite())
            {
                Trace("CanWrite returned false");

                MessageBox.Show(
                    "Для работы USB Block Manager требуются права администратора.\n\n" +
                    "Закройте приложение и запустите его от имени администратора.",
                    "USB Block Manager — недостаточно прав",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Shutdown();
                return;
            }

            Trace("AppPaths.EnsureAll");
            AppPaths.EnsureAll();

            // ============ ПРОВЕРКА ЛИЦЕНЗИИ ============
            Trace("Checking license");
            LicenseStatus = LicenseChecker.Check(AppPaths.LicenseFile, AppPaths.TrialFile);
            Trace($"License status: {LicenseStatus.Kind} — {LicenseStatus.Message}");

            // Показываем окно активации, если лицензия не валидна
// Показываем окно активации, если лицензия не валидна.
// Включая режим Trial — пользователь сам решает: активировать
// или продолжить пробный период (7 дней).
            if (LicenseStatus.Kind != LicenseStatusKind.Licensed)
            {
                Trace($"Showing activation window (status={LicenseStatus.Kind})");

                var activationVm = new ActivationViewModel(LicenseStatus);
                var activationWindow = new ActivationWindow(activationVm);

                var result = activationWindow.ShowDialog();

                if (result != true)
                {
                    Trace("Activation cancelled or failed, shutting down");
                    MessageBox.Show(
                        "Без действующей лицензии работа программы невозможна.\n\n" +
                        "Получите лицензию у поставщика и запустите программу снова.",
                        "USB Block Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    Shutdown();
                    return;
                }

                // Пользователь активировал лицензию или нажал «Продолжить пробный период»
                Trace("Activation succeeded, re-checking license");
                LicenseStatus = LicenseChecker.Check(AppPaths.LicenseFile, AppPaths.TrialFile);
                Trace($"New license status: {LicenseStatus.Kind}");
            }

            if (LicenseStatus.Kind == LicenseStatusKind.Trial)
            {
                Trace($"Trial mode: {LicenseStatus.Trial?.DaysRemaining} days left");
            }
            // ============ КОНЕЦ ПРОВЕРКИ ЛИЦЕНЗИИ ============

            Trace("Creating AuthService");
            _authService = new AuthService(AppPaths.AdminHashFile);

            Trace("Creating ServiceClient");
            _serviceClient = new ServiceClient();

            bool startMinimized = e.Args.Any(a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            bool uninstallRequested = e.Args.Any(a =>
                string.Equals(a, "--uninstall", StringComparison.OrdinalIgnoreCase));

            Trace($"startMinimized={startMinimized}, uninstallRequested={uninstallRequested}");

            // Если запустили с флагом --uninstall — сразу переходим к удалению
            if (uninstallRequested)
            {
                Trace("Uninstall mode requested");

                var authService = new AuthService(AppPaths.AdminHashFile);
                var prompt = new AdminPasswordPrompt(authService);
                var confirm = prompt.ShowDialog();

                if (confirm == true)
                {
                    TryUninstall();
                }
                else
                {
                    MessageBox.Show(
                        "Удаление отменено.",
                        "USB Block Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            Trace("Creating TrayIcon");
            _trayIcon = new TrayIcon();
            _trayIcon.OpenRequested += (_, _) => ShowMainWindow();
            _trayIcon.DoubleClicked += (_, _) => ShowMainWindow();
            _trayIcon.ExitRequested += (_, _) => TryExitWithPassword();

            if (startMinimized)
            {
                Trace("Showing balloon");
                _trayIcon.ShowBalloon(
                    "USB Block Manager",
                    "Программа запущена и работает в фоне. Двойной клик — открыть окно.");
            }
            else
            {
                Trace("Calling ShowMainWindow");
                ShowMainWindow();
            }

            Trace("OnStartup completed");
        }
        catch (Exception ex)
        {
            Trace($"OnStartup catch: {ex}");
            WriteCrash(ex, "OnStartup catch");

            MessageBox.Show(
                $"Ошибка при запуске:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "USB Block Manager — ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    private void ShowMainWindow()
    {
        Trace("ShowMainWindow started");

        if (_isWindowVisible)
        {
            Trace("Window already visible");
            if (_mainWindow is not null)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
            return;
        }

        if (_authService is null || _serviceClient is null)
        {
            Trace("authService or serviceClient is null");
            return;
        }

        Trace("Creating LoginWindow");
        var loginVm = new LoginViewModel(_authService);
        var loginWindow = new LoginWindow(loginVm);

        _isWindowVisible = true;
        Trace("Calling loginWindow.ShowDialog");
        var result = loginWindow.ShowDialog();
        _isWindowVisible = false;

        Trace($"ShowDialog result={result}");

        if (result != true)
            return;

        Trace("Checking service availability");
        if (!_serviceClient.IsServiceAvailable())
        {
            MessageBox.Show(
                "Служба USBBlockService не запущена.\n\n" +
                "Блокировка USB работать не будет.\n\n" +
                "Запустите службу командой (от имени администратора):\n" +
                "    sc start USBBlockService",
                "USB Block Manager — служба не запущена",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Trace("Creating MainViewModel");
        var mainVm = new MainViewModel(_serviceClient, _authService);
        mainVm.LicenseStatus = LicenseStatus;

        Trace("Creating MainWindow");
        _mainWindow = new MainWindow(mainVm);

        _mainWindow.Closed += (_, _) =>
        {
            _mainWindow = null;
            _isWindowVisible = false;
        };

        _isWindowVisible = true;
        Trace("Calling mainWindow.Show");
        _mainWindow.Show();
        Trace("ShowMainWindow completed");
    }

    private void TryExitWithPassword()
    {
        if (_authService is null)
        {
            ShutdownSafely();
            return;
        }

        var prompt = new AdminPasswordPrompt(_authService);
        if (_mainWindow is not null && _mainWindow.IsVisible)
            prompt.Owner = _mainWindow;

        var result = prompt.ShowDialog();
        if (result == true)
            ShutdownSafely();
    }

    public void TryExitWithPasswordFromMenu()
    {
        TryExitWithPassword();
    }
    private void TryUninstall()
    {
        // Секретный токен — должен совпадать с токеном в installer\setup.iss
        const string UninstallToken = "{7E2A-91F4-BD03-USB-BLOCK-2026}";

        // Ищем unins000.exe рядом с GUI
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

        // Спрашиваем подтверждение
        var confirm = MessageBox.Show(
            "Удалить USB Block Manager?\n\n" +
            "Служба и задача планировщика будут остановлены и удалены,\n" +
            "все настройки и белый список будут стёрты.",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        // Запускаем установщик с токеном
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = uninstaller,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PASSWORD={UninstallToken}",
                UseShellExecute = false
            };

            System.Diagnostics.Process.Start(psi);

            // Даём установщику время запуститься, потом выходим
            System.Threading.Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось запустить удаление:\n" + ex.Message,
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    /// <summary>
    /// Корректное завершение приложения: закрывает окно, убирает трей,
    /// освобождает Mutex в текущем потоке, останавливает WPF.
    /// </summary>
    public void ShutdownSafely()
    {
        try { _mainWindow?.PrepareForShutdown(); } catch { }
        try { _trayIcon?.Dispose(); } catch { }
        try { _mainWindow?.Close(); } catch { }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _trayIcon?.Dispose(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }

        base.OnExit(e);
    }

    private static void Trace(string message)
    {
        try
        {
            File.AppendAllText(TraceLogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    private static void WriteCrash(Exception? ex, string context)
    {
        try
        {
            var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n" +
                       (ex is null ? "(no exception)" : ex.ToString()) +
                       "\n\n";
            File.AppendAllText(CrashLogPath, text);
        }
        catch { }
    }
}