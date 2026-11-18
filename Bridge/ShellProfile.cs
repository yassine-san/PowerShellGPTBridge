namespace PersistentBridge;

internal enum ShellKind { PowerShell, Bash, Zsh }

/// <summary>
/// Describes the local shell this bridge exposes to the model. The shell follows the operating system:
/// PowerShell on Windows, the user's bash or zsh on Linux. PSBRIDGE_SHELL (powershell, bash or zsh)
/// overrides detection and an unknown value is refused rather than silently ignored.
/// </summary>
internal sealed record ShellProfile(ShellKind Kind, string DisplayName, string ToolName, string PinnedRead, string ToolDescription)
{
    public const string OverrideVariable = "PSBRIDGE_SHELL";
    private static readonly string[] SearchDirectories = ["/usr/bin", "/bin", "/usr/local/bin"];

    public bool IsPowerShell => Kind == ShellKind.PowerShell;

    private static readonly Lazy<ShellProfile> Detected = new(() => Detect(Environment.GetEnvironmentVariable, FindExecutable, OperatingSystem.IsWindows()));
    public static ShellProfile Current => Detected.Value;

    /// <summary>
    /// Resolves an executable from the fixed system directories only, never from PATH, and skips candidates
    /// that are not executable or that any user could replace (world-writable).
    /// </summary>
    public static string? FindExecutable(string name) =>
        SearchDirectories.Select(dir => Path.Combine(dir, name)).FirstOrDefault(IsTrustedExecutable);

    private static bool IsTrustedExecutable(string path)
    {
        if (!File.Exists(path)) return false;
        if (OperatingSystem.IsWindows()) return true;
        var mode = File.GetUnixFileMode(path);
        return (mode & UnixFileMode.UserExecute) != 0 && (mode & UnixFileMode.OtherWrite) == 0;
    }

    public static ShellProfile Detect(Func<string, string?> environment, Func<string, string?> locate, bool windows)
    {
        var requested = environment(OverrideVariable)?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(requested)) {
            return requested switch {
                "powershell" => Create(ShellKind.PowerShell),
                "bash" when locate("bash") != null => Create(ShellKind.Bash),
                "zsh" when locate("zsh") != null => Create(ShellKind.Zsh),
                "bash" or "zsh" => throw new InvalidOperationException($"{OverrideVariable}={requested}, but {requested} was not found in a system directory."),
                _ => throw new InvalidOperationException($"{OverrideVariable} must be powershell, bash or zsh (got '{requested}')."),
            };
        }
        if (windows) return Create(ShellKind.PowerShell);
        if (Path.GetFileName(environment("SHELL") ?? "") == "zsh" && locate("zsh") != null) return Create(ShellKind.Zsh);
        if (locate("bash") != null) return Create(ShellKind.Bash);
        if (locate("zsh") != null) return Create(ShellKind.Zsh);
        throw new InvalidOperationException("No supported shell found: install bash or zsh, or set PSBRIDGE_SHELL=powershell.");
    }

    public static ShellProfile Create(ShellKind kind) => kind switch {
        // Strings below are the established Windows contract and must not change.
        ShellKind.PowerShell => new(kind, "PowerShell", "execute_powershell", "Get-Date",
            "Run a script in the persistent local PowerShell 7 session with fresh native approval. Exact Get-Date is a fixed read operation. Arbitrary scripts are WRITE. Elevated=true runs ONE isolated admin operation and does not share persistent state. Supply accurate description/effects; do not hide wrappers or future automation."),
        _ => new(kind, kind == ShellKind.Zsh ? "zsh" : "bash", "execute_shell", "date",
            $"Run a script in the persistent local {(kind == ShellKind.Zsh ? "zsh" : "bash")} session with fresh native approval. Variables, functions, exported environment and the working directory persist between calls; stdin is empty. Exact `date` is a fixed read operation. Arbitrary scripts are WRITE. Elevation is unavailable on Linux. Supply accurate description/effects; do not hide wrappers or future automation."),
    };
}
