using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CabSpan.Services;

namespace CabSpan;

public partial class App : System.Windows.Application
{
    public const string TEST_ERROR_ALERT_FLAG = "--test-error-alert";
    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
        RegisterGlobalExceptionHandlers();
        SelfUpdateInstaller.CleanupPreviousBackup();

        if (e.Args.Any(arg => string.Equals(arg, TEST_ERROR_ALERT_FLAG, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorLogger.CurrentExecutionMode = "DiagnosticTestCli";
            bool sent = ErrorLogger.SendTestDiagnosticAlert();
            Console.WriteLine(sent
                ? "[CabSpan Diagnostics] Test error embed delivered to Discord and logged locally."
                : "[CabSpan Diagnostics] Failed to deliver Discord alert (check webhook URL).");
            Shutdown(sent ? 0 : 1);
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, AutoLaunchHandler.AUTO_LAUNCH_FLAG, StringComparison.OrdinalIgnoreCase)))
        {
            ErrorLogger.CurrentExecutionMode = "AutoLaunchCli";
            Shutdown(RunAutoLaunchWithLogging(e.Args));
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, "--span-borderless", StringComparison.OrdinalIgnoreCase)))
        {
            ErrorLogger.CurrentExecutionMode = "BorderlessSpannerCli";
            Shutdown(RunBorderlessSpannerWithLogging());
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, "--test-math", StringComparison.OrdinalIgnoreCase)))
        {
            ErrorLogger.CurrentExecutionMode = "MathSelfTestCli";
            int exitCode = MathSelfTestRunner.RunAllTests();
            Shutdown(exitCode);
            return;
        }

        ErrorLogger.CurrentExecutionMode = "DesktopGUI";
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLogger.LogException(e.Exception, "WPF.DispatcherUnhandledException", isFatal: true);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            ErrorLogger.LogException(ex, "AppDomain.UnhandledException", isFatal: true);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorLogger.LogException(e.Exception, "TaskScheduler.UnobservedTaskException", isFatal: false);
        e.SetObserved();
    }

    private static int RunAutoLaunchWithLogging(string[] args)
    {
        try
        {
            var handler = new AutoLaunchHandler();
            return handler.Execute(args);
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, "AutoLaunchHandler.Execute", isFatal: true);
            return 1;
        }
    }

    private static int RunBorderlessSpannerWithLogging()
    {
        try
        {
            var store = new ProfileStore();
            var profile = store.Load();
            var scanner = new MonitorScanner();
            var spanner = new BorderlessSpanner();
            bool spanned = spanner.TrySpanSimulatorWindow(profile, scanner.ScanConnectedMonitors());
            return spanned ? 0 : 1;
        }
        catch (Exception ex)
        {
            ErrorLogger.LogException(ex, "BorderlessSpanner.TrySpanSimulatorWindow", isFatal: true);
            return 1;
        }
    }
}
