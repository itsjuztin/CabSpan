using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CabSpan.Services;

public enum SimulatorGame
{
    AmericanTruckSimulator,
    EuroTruckSimulator2
}

/// <summary>
/// Manages config.cfg, config.single_monitor.bak, and multimon_config.sii for ATS and ETS2.
/// </summary>
public sealed class ConfigManager
{
    public const string ATS_FOLDER_NAME = "American Truck Simulator";
    public const string ETS2_FOLDER_NAME = "Euro Truck Simulator 2";
    public const string CONFIG_FILE_NAME = "config.cfg";
    public const string BACKUP_FILE_NAME = "config.single_monitor.bak";
    public const string MULTIMON_SII_FILE_NAME = "multimon_config.sii";

    private const string KEY_MULTIMON_MODE = "r_multimon_mode";
    private const string KEY_ZERO_PITCH = "g_interior_camera_zero_pitch";
    private const string KEY_MODE_WIDTH = "r_mode_width";
    private const string KEY_MODE_HEIGHT = "r_mode_height";
    private const string KEY_FULLSCREEN = "r_fullscreen";
    private const string KEY_DEVELOPER = "g_developer";
    private const string KEY_CONSOLE = "g_console";

    private readonly IReadOnlyList<string> _targetDirectories;

    public ConfigManager()
        : this(GetDefaultGameDirectories())
    {
    }

    public ConfigManager(IEnumerable<string> targetDirectories)
    {
        ArgumentNullException.ThrowIfNull(targetDirectories);
        _targetDirectories = targetDirectories.ToList();
    }

    public static IReadOnlyList<string> GetDefaultGameDirectories()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new[]
        {
            Path.Combine(documents, ATS_FOLDER_NAME),
            Path.Combine(documents, ETS2_FOLDER_NAME)
        };
    }

    public static string GetGameDirectory(SimulatorGame game, string? documentsRoot = null)
    {
        string root = documentsRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string folder = game == SimulatorGame.AmericanTruckSimulator ? ATS_FOLDER_NAME : ETS2_FOLDER_NAME;
        return Path.Combine(root, folder);
    }

    /// <summary>
    /// Applies r_multimon_mode "4" and spanned width/height settings across configured game directories.
    /// Creates config.single_monitor.bak prior to modifying config.cfg.
    /// </summary>
    public void ApplyMultiMonitorMode(int spannedWidth, int spannedHeight = 0, bool borderlessWindowed = false)
    {
        if (spannedWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spannedWidth), "Spanned width must be positive.");
        }

        var updates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [KEY_MULTIMON_MODE] = "4",
            [KEY_ZERO_PITCH] = "1",
            [KEY_MODE_WIDTH] = spannedWidth.ToString(CultureInfo.InvariantCulture),
            [KEY_FULLSCREEN] = "1",
            [KEY_DEVELOPER] = "1",
            [KEY_CONSOLE] = "1"
        };

        if (spannedHeight > 0)
        {
            updates[KEY_MODE_HEIGHT] = spannedHeight.ToString(CultureInfo.InvariantCulture);
        }

        foreach (string directory in _targetDirectories)
        {
            if (Directory.Exists(directory))
            {
                ApplyConfigUpdatesToDirectory(directory, updates);
            }
        }
    }

    /// <summary>
    /// Resets multimon cvars back to single-monitor mode while preserving the user's original developer/console preference from backup.
    /// </summary>
    public void ApplySingleMonitorFallback(int singleWidth)
    {
        if (singleWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(singleWidth), "Single monitor width must be positive.");
        }

        foreach (string directory in _targetDirectories)
        {
            if (Directory.Exists(directory))
            {
                string backupPath = Path.Combine(directory, BACKUP_FILE_NAME);
                string origDev = ReadCvarFromBackup(backupPath, KEY_DEVELOPER, "0");
                string origConsole = ReadCvarFromBackup(backupPath, KEY_CONSOLE, "0");

                var updates = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [KEY_MULTIMON_MODE] = "0",
                    [KEY_ZERO_PITCH] = "0",
                    [KEY_MODE_WIDTH] = singleWidth.ToString(CultureInfo.InvariantCulture),
                    [KEY_DEVELOPER] = origDev,
                    [KEY_CONSOLE] = origConsole
                };

                ApplyConfigUpdatesToDirectory(directory, updates);
            }
        }
    }

    private static string ReadCvarFromBackup(string backupPath, string key, string defaultValue)
    {
        if (!File.Exists(backupPath))
        {
            return defaultValue;
        }

        try
        {
            string content = File.ReadAllText(backupPath, Encoding.UTF8);
            Match m = Regex.Match(content, $@"^\s*uset\s+{Regex.Escape(key)}\s+""([^""]*)""", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public void WriteMultimonSii(string siiContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siiContent);

        foreach (string directory in _targetDirectories)
        {
            if (Directory.Exists(directory))
            {
                string siiPath = Path.Combine(directory, MULTIMON_SII_FILE_NAME);
                File.WriteAllText(siiPath, siiContent, Encoding.UTF8);
            }
        }
    }

    /// <summary>
    /// Restores config.single_monitor.bak over config.cfg in each target game directory.
    /// </summary>
    public int RestoreOriginalBackup()
    {
        int restoredCount = 0;
        foreach (string directory in _targetDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string configPath = Path.Combine(directory, CONFIG_FILE_NAME);
            string backupPath = Path.Combine(directory, BACKUP_FILE_NAME);
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, configPath, overwrite: true);
                restoredCount++;
            }
        }

        return restoredCount;
    }

    /// <summary>
    /// Checks whether the specified simulator's config.cfg is currently configured for Multi-Monitor Mode 4.
    /// </summary>
    public static bool IsMultiMonitorModeActive(SimulatorGame game, string? documentsRoot = null)
    {
        string gameDir = GetGameDirectory(game, documentsRoot);
        string configPath = Path.Combine(gameDir, CONFIG_FILE_NAME);
        if (!File.Exists(configPath))
        {
            return false;
        }

        string content = File.ReadAllText(configPath, Encoding.UTF8);
        return Regex.IsMatch(content, @"^\s*uset\s+r_multimon_mode\s+""4""\s*$", RegexOptions.Multiline);
    }

    /// <summary>
    /// Returns the human-readable status pill label for the specified game.
    /// </summary>
    public static string GetGameModeStatusText(SimulatorGame game, string? documentsRoot = null)
    {
        if (IsMultiMonitorModeActive(game, documentsRoot))
        {
            return "🟢 Multi-Screen Active";
        }

        return "🖥️ 1-Screen (Normal)";
    }

    private static void ApplyConfigUpdatesToDirectory(string directory, IReadOnlyDictionary<string, string> updates)
    {
        string configPath = Path.Combine(directory, CONFIG_FILE_NAME);
        string backupPath = Path.Combine(directory, BACKUP_FILE_NAME);

        EnsureConfigAndBackupExist(configPath, backupPath);

        string[] lines = File.ReadAllLines(configPath, Encoding.UTF8);
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = TryUpdateConfigLine(lines[i], updates, matchedKeys);
        }

        var outputLines = new List<string>(lines);
        foreach (KeyValuePair<string, string> kvp in updates)
        {
            if (!matchedKeys.Contains(kvp.Key))
            {
                outputLines.Add($"uset {kvp.Key} \"{kvp.Value}\"");
            }
        }

        File.WriteAllLines(configPath, outputLines, Encoding.UTF8);
    }

    private static void EnsureConfigAndBackupExist(string configPath, string backupPath)
    {
        if (!File.Exists(configPath))
        {
            string defaultContent = string.Join(Environment.NewLine, new[]
            {
                "# Prism3D variable configuration",
                "uset r_multimon_mode \"0\"",
                "uset g_interior_camera_zero_pitch \"0\"",
                "uset r_mode_width \"2560\"",
                "uset g_developer \"0\"",
                "uset g_console \"0\""
            }) + Environment.NewLine;

            File.WriteAllText(configPath, defaultContent, Encoding.UTF8);
        }

        if (!File.Exists(backupPath))
        {
            File.Copy(configPath, backupPath, overwrite: false);
        }
    }

    private static string TryUpdateConfigLine(
        string line,
        IReadOnlyDictionary<string, string> updates,
        ISet<string> matchedKeys)
    {
        foreach (KeyValuePair<string, string> kvp in updates)
        {
            string pattern = $@"^(\s*uset\s+{Regex.Escape(kvp.Key)}\s+"")[^""]*(""\s*)$";
            Match match = Regex.Match(line, pattern);
            if (match.Success)
            {
                matchedKeys.Add(kvp.Key);
                return $"{match.Groups[1].Value}{kvp.Value}{match.Groups[2].Value}";
            }
        }

        return line;
    }
}
