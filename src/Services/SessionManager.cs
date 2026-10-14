using System.Windows.Threading;

namespace USBBlockManager.Services;

/// <summary>
/// Управляет временем жизни административной сессии.
/// По истечении таймаута бездействия сессия автоматически завершается.
/// </summary>
public class SessionManager : IDisposable
{
    private readonly DispatcherTimer _timer;
    private DateTime _lastActivity = DateTime.UtcNow;
    private bool _isActive;

    public SessionManager(TimeSpan timeout)
    {
        Timeout = timeout;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30), // проверяем раз в 30 секунд
        };
        _timer.Tick += OnTick;
    }

    public TimeSpan Timeout { get; set; }

    public bool IsActive => _isActive;

    /// <summary>Событие: сессия истекла, нужно вернуть пользователя на экран логина.</summary>
    public event EventHandler? SessionExpired;

    public void Start()
    {
        _isActive = true;
        _lastActivity = DateTime.UtcNow;
        _timer.Start();
    }

    public void Stop()
    {
        _isActive = false;
        _timer.Stop();
    }

    /// <summary>Отметить активность пользователя (вызывается из UI).</summary>
    public void Touch()
    {
        if (_isActive)
            _lastActivity = DateTime.UtcNow;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_isActive)
            return;

        if (DateTime.UtcNow - _lastActivity >= Timeout)
        {
            Stop();
            SessionExpired?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}