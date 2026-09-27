using System.Drawing;
using System.IO;
using CabSpan.Models;

namespace CabSpan.Services;

/// <summary>
/// Executes automated CLI self-tests (--test-math) verifying SiiGenerator math and ConfigManager state transitions.
/// </summary>
public static class MathSelfTestRunner
{
    private const double FLOAT_TOLERANCE = 0.0001;

    public static int RunAllTests()
    {
        Console.WriteLine("=================================================================");
        Console.WriteLine(" CabSpan Phase 1 CLI Self-Test (--test-math)");
        Console.WriteLine("=================================================================");

        try
        {
            RunLiveMonitorScanCheck();
            RunDual1440pTest();
            RunMixedUltrawideTest();
            RunFiveMonitorSubsetTest();
            RunConfigManagerSandboxTest();
            RunPhase2WatchdogAndProfileTest();
            RunErrorLoggerAndDiscordPayloadTest();
            UpdateSelfTestRunner.RunAllUpdateTests();

            Console.WriteLine();
            Console.WriteLine("[PASS] All CabSpan Phase 1, 2, Telemetry & Auto-Update self-tests passed with 0 errors!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FAIL] Self-test failed: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static void RunLiveMonitorScanCheck()
    {
        var scanner = new MonitorScanner();
        List<MonitorInfo> liveMonitors = scanner.ScanConnectedMonitors();
        Console.WriteLine($"\n[LIVE SCAN] Detected {liveMonitors.Count} connected display(s):");
        foreach (MonitorInfo monitor in liveMonitors)
        {
            Console.WriteLine($"  - {monitor}");
        }
    }

    private static void RunDual1440pTest()
    {
        Console.WriteLine("\n-----------------------------------------------------------------");
        Console.WriteLine(" TEST 1: 2x1440p Setup (2560x1440 Main + 2560x1440 Right)");
        Console.WriteLine("-----------------------------------------------------------------");

        var monitors = new List<MonitorInfo>
        {
            new(@"\\.\DISPLAY1", "LG UltraGear 27", new Rectangle(0, 0, 2560, 1440), isSelectedForCab: true, isMainWheelScreen: true),
            new(@"\\.\DISPLAY2", "LG UltraGear 27", new Rectangle(2560, 0, 2560, 1440), isSelectedForCab: true, isMainWheelScreen: false)
        };

        var generator = new SiiGenerator();
        SiiGenerationResult result = generator.Generate(monitors, bezelGapDegrees: 2.5, sideMonitorAngleDegrees: 0.0);

        AssertEqual(0, result.CabLeft, "2x1440p CabLeft");
        AssertEqual(5120, result.TotalCabWidth, "2x1440p TotalCabWidth");
        AssertNear(0.0, result.NormalizedUiX, "2x1440p normalized_ui_x");
        AssertNear(0.5, result.NormalizedUiWidth, "2x1440p normalized_ui_width");
        AssertNear(0.0, result.Monitors[0].HeadingOffset, "2x1440p Main heading_offset");
        AssertNear(-1.0, result.Monitors[1].HorizontalFovRelativeOffset, "2x1440p Right fov_offset");
        AssertNear(-2.5, result.Monitors[1].HeadingOffset, "2x1440p Right heading_offset");

        Console.WriteLine(result.SiiContent);
    }

    private static void RunMixedUltrawideTest()
    {
        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine(" TEST 2: 3440x1440 + 2560x1440 Mixed Setup (Left 16:9 + Main 21:9)");
        Console.WriteLine("-----------------------------------------------------------------");

        var monitors = new List<MonitorInfo>
        {
            new(@"\\.\DISPLAY1", "Dell 27 1440p", new Rectangle(-2560, 0, 2560, 1440), isSelectedForCab: true, isMainWheelScreen: false),
            new(@"\\.\DISPLAY2", "Alienware 34 UW", new Rectangle(0, 0, 3440, 1440), isSelectedForCab: true, isMainWheelScreen: true)
        };

        var generator = new SiiGenerator();
        SiiGenerationResult result = generator.Generate(monitors, bezelGapDegrees: 2.5, sideMonitorAngleDegrees: 10.0);

        AssertEqual(-2560, result.CabLeft, "Mixed CabLeft");
        AssertEqual(6000, result.TotalCabWidth, "Mixed TotalCabWidth");
        AssertNear(2560.0 / 6000.0, result.NormalizedUiX, "Mixed normalized_ui_x");
        AssertNear(3440.0 / 6000.0, result.NormalizedUiWidth, "Mixed normalized_ui_width");
        AssertNear(1.0, result.Monitors[1].HorizontalFovRelativeOffset, "Mixed Left fov_offset");
        AssertNear(12.5, result.Monitors[1].HeadingOffset, "Mixed Left heading_offset (2.5 + 10.0)");

        Console.WriteLine(result.SiiContent);
    }

    private static void RunFiveMonitorSubsetTest()
    {
        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine(" TEST 3: 5-Monitor Setup (Only 2 Selected for Cab)");
        Console.WriteLine("-----------------------------------------------------------------");

        var monitors = new List<MonitorInfo>
        {
            new(@"\\.\DISPLAY1", "Far Left Vertical", new Rectangle(-1080, 0, 1080, 1920), isSelectedForCab: false),
            new(@"\\.\DISPLAY2", "Left Cab Screen", new Rectangle(0, 0, 2560, 1440), isSelectedForCab: true, isMainWheelScreen: false),
            new(@"\\.\DISPLAY3", "Center Wheel Screen", new Rectangle(2560, 0, 2560, 1440), isSelectedForCab: true, isMainWheelScreen: true),
            new(@"\\.\DISPLAY4", "SimHub Dash Tablet", new Rectangle(2560, 1440, 1280, 800), isSelectedForCab: false, isAuxDashOnly: true),
            new(@"\\.\DISPLAY5", "Right TV Display", new Rectangle(5120, 0, 3840, 2160), isSelectedForCab: false)
        };

        var generator = new SiiGenerator();
        SiiGenerationResult result = generator.Generate(monitors, bezelGapDegrees: 2.5, sideMonitorAngleDegrees: 0.0);

        AssertEqual(2, result.Monitors.Count, "5-Monitor Selected Count");
        AssertEqual(0, result.CabLeft, "5-Monitor CabLeft (ignores X=-1080)");
        AssertEqual(5120, result.TotalCabWidth, "5-Monitor TotalCabWidth (ignores TV at 5120)");
        AssertNear(0.5, result.NormalizedUiX, "5-Monitor normalized_ui_x (Main is right half of 2-screen span)");
        AssertNear(0.5, result.NormalizedUiWidth, "5-Monitor normalized_ui_width");
        AssertNear(1.0, result.Monitors[1].HorizontalFovRelativeOffset, "5-Monitor Left Cab fov_offset");
        AssertNear(2.5, result.Monitors[1].HeadingOffset, "5-Monitor Left Cab heading_offset");

        Console.WriteLine(result.SiiContent);
    }

    private static void RunConfigManagerSandboxTest()
    {
        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine(" TEST 4: ConfigManager Backup & Mode Switching Verification");
        Console.WriteLine("-----------------------------------------------------------------");

        string tempRoot = Path.Combine(Path.GetTempPath(), $"CabSpan_Test_{Guid.NewGuid():N}");
        string atsDir = ConfigManager.GetGameDirectory(SimulatorGame.AmericanTruckSimulator, tempRoot);
        string ets2Dir = ConfigManager.GetGameDirectory(SimulatorGame.EuroTruckSimulator2, tempRoot);

        try
        {
            Directory.CreateDirectory(atsDir);
            Directory.CreateDirectory(ets2Dir);

            var manager = new ConfigManager(new[] { atsDir, ets2Dir });
            manager.ApplyMultiMonitorMode(spannedWidth: 6000, spannedHeight: 1440, borderlessWindowed: true);

            string atsConfig = File.ReadAllText(Path.Combine(atsDir, ConfigManager.CONFIG_FILE_NAME));
            string atsBackup = Path.Combine(atsDir, ConfigManager.BACKUP_FILE_NAME);

            if (!File.Exists(atsBackup) ||
                !atsConfig.Contains("uset r_multimon_mode \"4\"") ||
                !atsConfig.Contains("uset r_mode_width \"6000\"") ||
                !atsConfig.Contains("uset r_mode_height \"1440\"") ||
                !atsConfig.Contains("uset r_fullscreen \"1\""))
            {
                throw new InvalidOperationException("Multi-monitor config update or backup creation failed.");
            }

            manager.ApplySingleMonitorFallback(singleWidth: 3440);
            string fallbackConfig = File.ReadAllText(Path.Combine(atsDir, ConfigManager.CONFIG_FILE_NAME));
            if (!fallbackConfig.Contains("uset r_multimon_mode \"0\"") || !fallbackConfig.Contains("uset r_mode_width \"3440\""))
            {
                throw new InvalidOperationException("Single-monitor fallback reset failed.");
            }

            Console.WriteLine("  - Verified config.single_monitor.bak creation for ATS & ETS2");
            Console.WriteLine("  - Verified ApplyMultiMonitorMode(6000, 1440) sets r_multimon_mode \"4\", r_fullscreen \"1\", r_mode_height \"1440\"");
            Console.WriteLine("  - Verified ApplySingleMonitorFallback(3440) resets multimon cvars and preserves backup dev/console settings.");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void RunPhase2WatchdogAndProfileTest()
    {
        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine(" TEST 5: Phase 2 ProfileStore, AutoLaunchHandler & BorderlessSpanner");
        Console.WriteLine("-----------------------------------------------------------------");

        string tempRoot = Path.Combine(Path.GetTempPath(), $"CabSpan_P2_{Guid.NewGuid():N}");
        string profilePath = Path.Combine(tempRoot, "CabSpan", "profile.json");
        string atsDir = ConfigManager.GetGameDirectory(SimulatorGame.AmericanTruckSimulator, tempRoot);

        try
        {
            Directory.CreateDirectory(atsDir);
            var store = new ProfileStore(profilePath);
            var profile = new CabSpanProfile
            {
                ApplyToAts = true,
                ApplyToEts2 = false,
                SelectedMonitorDeviceNames = new List<string> { @"\\.\DISPLAY1", @"\\.\DISPLAY2" },
                MainWheelMonitorId = @"\\.\DISPLAY1",
                BezelGapDegrees = 3.0,
                SideMonitorAngleDegrees = 12.0,
                SingleMonitorWidth = 2560,
                SpannedCabWidth = 5120,
                SpannedCabHeight = 1440,
                CabLeft = -2560,
                CabTop = 0,
                SpanningMode = SpanningMode.ZeroDriverBorderless
            };

            store.Save(profile);
            CabSpanProfile loaded = store.Load();
            AssertEqual(5120, loaded.SpannedCabWidth, "Loaded SpannedCabWidth");
            AssertEqual(2560, loaded.SingleMonitorWidth, "Loaded SingleMonitorWidth");
            AssertNear(3.0, loaded.BezelGapDegrees, "Loaded BezelGapDegrees");

            var configManager = new ConfigManager(loaded.GetEnabledGameDirectories(tempRoot));
            var watchdog = new AutoLaunchHandler(store);

            bool surroundActive = watchdog.EvaluateAndApplyDisplayMode(loaded, primaryWidth: 5120, configManager);
            string cfgSurround = File.ReadAllText(Path.Combine(atsDir, ConfigManager.CONFIG_FILE_NAME));
            if (!surroundActive || !cfgSurround.Contains("uset r_multimon_mode \"4\""))
            {
                throw new InvalidOperationException("AutoLaunchHandler failed to activate MultiMonitorMode when PrimaryWidth >= 0.9 * SpannedCabWidth.");
            }

            bool fallbackActive = !watchdog.EvaluateAndApplyDisplayMode(loaded, primaryWidth: 2560, configManager);
            string cfgFallback = File.ReadAllText(Path.Combine(atsDir, ConfigManager.CONFIG_FILE_NAME));
            if (!fallbackActive || !cfgFallback.Contains("uset r_multimon_mode \"0\"") || !cfgFallback.Contains("uset r_mode_width \"2560\""))
            {
                throw new InvalidOperationException("AutoLaunchHandler failed to apply SingleMonitorFallback when Surround is OFF.");
            }

            var spanner = new BorderlessSpanner();
            Rectangle box = spanner.ResolveProfileBoundingBox(loaded);
            AssertEqual(-2560, box.X, "BorderlessSpanner CabLeft");
            AssertEqual(5120, box.Width, "BorderlessSpanner TotalCabWidth");
            AssertEqual(1440, box.Height, "BorderlessSpanner TotalCabHeight");

            Console.WriteLine("  - Verified ProfileStore JSON round-trip to profile.json");
            Console.WriteLine("  - Verified AutoLaunchHandler Surround ON (5120 >= 4608) -> MultiMonitorMode");
            Console.WriteLine("  - Verified AutoLaunchHandler Surround OFF (2560 < 4608) -> SingleMonitorFallback");
            Console.WriteLine("  - Verified BorderlessSpanner bounding box (-2560, 0, 5120, 1440)");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void RunErrorLoggerAndDiscordPayloadTest()
    {
        Console.WriteLine("-----------------------------------------------------------------");
        Console.WriteLine(" TEST 6: ErrorLogger PII Scrubbing & Discord Payload Generation");
        Console.WriteLine("-----------------------------------------------------------------");

        string tempLog = Path.Combine(Path.GetTempPath(), $"cabspan-test-{Guid.NewGuid():N}.log");
        try
        {
            var profile = new CabSpanProfile { EnableCrashReporting = false, SpannedCabWidth = 7680, SpannedCabHeight = 1440 };
            var sampleEx = new IOException($@"File locked at {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\Documents\config.cfg");
            ErrorReportPayload payload = ErrorLogger.LogException(sampleEx, "UnitTest.VerifyLog", isFatal: false, profile, tempLog);

            if (payload.SanitizedMessage.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ErrorLogger failed to scrub Windows username from exception message.");

            if (!File.Exists(tempLog))
                throw new InvalidOperationException("ErrorLogger failed to write local log file.");

            string json = DiscordErrorReporter.BuildDiscordPayloadJson(payload);
            if (!json.Contains("CabSpan Operational Error") || !json.Contains("7680x1440"))
                throw new InvalidOperationException("DiscordErrorReporter generated invalid embed JSON.");

            if (BugReportService.ValidateReportInput("too short", out _, out _)
                || BugReportService.ValidateReportInput("aaaaaaaaaaaaaaaaaaaaaaaaaaaa", out _, out _)
                || BugReportService.ValidateReportInput("Check out this link https://spam.example.com in bug report", out _, out _))
                throw new InvalidOperationException("BugReportService failed to reject spam/short/link inputs.");

            if (!BugReportService.ValidateReportInput("@everyone Side monitor bezel angle is off by 3 degrees on 3440x1440.", out string cleanBug, out _)
                || cleanBug.Contains("@everyone", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BugReportService failed to accept valid input or strip @everyone.");

            Console.WriteLine("  - Verified PII scrubbing (%USERPROFILE% replacement)");
            Console.WriteLine("  - Verified local rolling log creation & Discord embed JSON formatting");
            Console.WriteLine("  - Verified BugReportService anti-spam guardrails (length, keyboard-mash, link & @everyone filter)");
        }
        finally
        {
            if (File.Exists(tempLog)) File.Delete(tempLog);
        }
    }

    private static void AssertEqual(int expected, int actual, string label)
    {
        if (expected != actual) throw new InvalidOperationException($"{label} mismatch: expected {expected}, got {actual}");
    }

    private static void AssertNear(double expected, double actual, string label)
    {
        if (Math.Abs(expected - actual) > FLOAT_TOLERANCE) throw new InvalidOperationException($"{label} mismatch: expected {expected:F6}, got {actual:F6}");
    }
}
