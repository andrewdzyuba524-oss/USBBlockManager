using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using USBBlockService;

var builder = Host.CreateApplicationBuilder(args);

// Включаем режим Windows Service — служба будет корректно
// обрабатывать Start/Stop от SCM (Service Control Manager).
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "USBBlockService";
});

// Регистрируем наш BackgroundService
builder.Services.AddHostedService<BlockService>();

// Логирование в файл
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

var host = builder.Build();
await host.RunAsync();