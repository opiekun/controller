using System.Windows;
using System.Windows.Threading;
using KeyboardAnalogThrottle.App.Composition;
using KeyboardAnalogThrottle.App.Services;
using KeyboardAnalogThrottle.Infrastructure.Windows.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace KeyboardAnalogThrottle.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceGuard? _singleInstance;
    private ApplicationLifetimeService? _lifetime;
    private int _resourcesDisposed;

    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        base.OnStartup(eventArgs);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _singleInstance = SingleInstanceGuard.TryAcquire();
        if (_singleInstance is null)
        {
            MessageBox.Show(
                "Keyboard Analog Throttle is already running.",
                "Keyboard Analog Throttle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _services = new ServiceCollection()
            .AddKeyboardAnalogThrottle()
            .BuildServiceProvider();
        _lifetime = _services.GetRequiredService<ApplicationLifetimeService>();

        var configuration = await _lifetime.InitializeAsync(CancellationToken.None);
        if (!configuration.IsSuccess)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, configuration.Errors.Select(static error => $"{error.PropertyName}: {error.Message}")),
                "Keyboard Analog Throttle configuration is invalid",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs eventArgs)
    {
        RequestEmergencyStop();
        var cleanup = _lifetime?.DisposeAsync().AsTask();
        if (cleanup is null)
        {
            DisposeResources();
        }
        else
        {
            _ = cleanup.ContinueWith(
                _ => DisposeResources(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        base.OnExit(eventArgs);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs eventArgs) => RequestEmergencyStop();

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs) => RequestEmergencyStop();

    private void OnProcessExit(object? sender, EventArgs eventArgs) => RequestEmergencyStop();

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        RequestEmergencyStop();
        eventArgs.SetObserved();
    }

    private void RequestEmergencyStop() => _lifetime?.RequestEmergencyStop();

    private void DisposeResources()
    {
        if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
        {
            return;
        }

        _services?.Dispose();
        _singleInstance?.Dispose();
    }
}
