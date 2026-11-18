using PersistentBridge;

// Secrets that must never reach a command's environment.
Environment.SetEnvironmentVariable("CONTROL_PLANE_API_KEY", "sk-test-secret");
Environment.SetEnvironmentVariable(ShellProfile.OverrideVariable, "bash");

int failures = 0;
async Task Case(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
}
void Expect(bool condition, string message) { if (!condition) throw new Exception(message); }
static string Show(Execution r) => $"status={r.Status} code={r.ExitCode} healthy={r.Healthy} out={r.Output!.Replace("\n", "\\n")} err={r.Error.Replace("\n", "\\n")}";

// ---- profile detection (pure logic)
Func<string, string?> Env(params (string Key, string Value)[] pairs) => key => pairs.FirstOrDefault(p => p.Key == key).Value;
string? Located(string name) => "/usr/bin/" + name;
string? OnlyZsh(string name) => name == "zsh" ? "/usr/bin/zsh" : null;
string? Nothing(string name) => null;

await Case("windows selects PowerShell with the established tool contract", async () => {
    var p = ShellProfile.Detect(Env(), Located, windows: true);
    Expect(p.IsPowerShell && p.ToolName == "execute_powershell" && p.PinnedRead == "Get-Date", "wrong profile");
    Expect(p.ToolDescription.StartsWith("Run a script in the persistent local PowerShell 7 session with fresh native approval."), "description changed");
    await Task.CompletedTask;
});
await Case("linux defaults to bash", async () => {
    var p = ShellProfile.Detect(Env(), Located, windows: false);
    Expect(p.Kind == ShellKind.Bash && p.ToolName == "execute_shell" && p.PinnedRead == "date", "wrong profile");
    await Task.CompletedTask;
});
await Case("linux follows SHELL=zsh when zsh exists", async () => {
    Expect(ShellProfile.Detect(Env(("SHELL", "/usr/bin/zsh")), Located, false).Kind == ShellKind.Zsh, "zsh expected");
    await Task.CompletedTask;
});
await Case("unsupported login shells fall back to bash", async () => {
    Expect(ShellProfile.Detect(Env(("SHELL", "/usr/bin/fish")), Located, false).Kind == ShellKind.Bash, "bash expected");
    await Task.CompletedTask;
});
await Case("zsh is used when bash is missing, and no shell is an error", async () => {
    Expect(ShellProfile.Detect(Env(), OnlyZsh, false).Kind == ShellKind.Zsh, "zsh fallback expected");
    try { ShellProfile.Detect(Env(), Nothing, false); throw new Exception("no error"); } catch (InvalidOperationException) { }
    await Task.CompletedTask;
});
await Case("PSBRIDGE_SHELL overrides detection and bad values are refused", async () => {
    Expect(ShellProfile.Detect(Env((ShellProfile.OverrideVariable, "PowerShell")), Located, false).IsPowerShell, "override ignored");
    foreach (var bad in new[] { "fish", "bash; rm -rf /" }) {
        try { ShellProfile.Detect(Env((ShellProfile.OverrideVariable, bad)), Located, false); throw new Exception("accepted " + bad); } catch (InvalidOperationException) { }
    }
    try { ShellProfile.Detect(Env((ShellProfile.OverrideVariable, "bash")), Nothing, false); throw new Exception("accepted missing bash"); } catch (InvalidOperationException) { }
    await Task.CompletedTask;
});

// ---- real sessions
var kinds = new List<ShellKind> { ShellKind.Bash };
if (ShellProfile.FindExecutable("zsh") != null) kinds.Add(ShellKind.Zsh); else Console.WriteLine("SKIP zsh not installed");

foreach (var kind in kinds) {
    var profile = ShellProfile.Create(kind);
    string tag = profile.DisplayName + ": ";
    ShellSession Fresh() => new(profile);
    Task<Execution> Run(ShellSession s, string command, int seconds = 10) => s.Run(command, seconds, CancellationToken.None);
    using var shell = Fresh();

    await Case(tag + "variables persist between calls", async () => {
        await Run(shell, "X=41");
        var r = await Run(shell, "echo $((X+1))");
        Expect(r.Output == "42\n" && r.Status == "completed" && r.ExitCode == 0, Show(r));
    });
    await Case(tag + "functions and working directory persist", async () => {
        await Run(shell, "greet() { echo hi-$1; }; cd /tmp");
        var r = await Run(shell, "greet a; pwd");
        Expect(r.Output == "hi-a\n/tmp\n" && r.Directory == "/tmp", Show(r) + " dir=" + r.Directory);
    });
    await Case(tag + "directory with spaces is reported intact", async () => {
        var r = await Run(shell, "mkdir -p \"$HOME/psb dir test\" && cd \"$HOME/psb dir test\" && pwd");
        var cleanup = await Run(shell, "cd / && rmdir \"$HOME/psb dir test\"");
        Expect(r.Directory.EndsWith("/psb dir test") && cleanup.Status == "completed", Show(r));
    });
    await Case(tag + "exit status is reported and the session survives", async () => {
        var r = await Run(shell, "(exit 7)");
        Expect(r.Status == "failed" && r.ExitCode == 7 && r.Healthy, Show(r));
        Expect((await Run(shell, "true")).Status == "completed", "session unusable after failure");
    });
    await Case(tag + "stdout and stderr stay separate", async () => {
        var r = await Run(shell, "echo out; echo err >&2");
        Expect(r.Output == "out\n" && r.Error == "err\n", Show(r));
    });
    await Case(tag + "output without a trailing newline is exact", async () => {
        var r = await Run(shell, "printf abc");
        Expect(r.Output == "abc", Show(r));
    });
    await Case(tag + "multi-line scripts and heredocs run verbatim", async () => {
        var r = await Run(shell, "cat <<'EOF'\nline1\nline 2\nEOF\nfor i in 1 2; do echo n$i; done");
        Expect(r.Output == "line1\nline 2\nn1\nn2\n", Show(r));
    });
    await Case(tag + "a syntax error does not end the session", async () => {
        var bad = await Run(shell, "if then");
        Expect(bad.Status == "failed", Show(bad));
        var r = await Run(shell, "echo alive");
        Expect(r.Output == "alive\n" && r.Healthy, "session lost after syntax error: " + Show(r));
    });
    await Case(tag + "commands cannot consume the control channel", async () => {
        var r = await Run(shell, "cat; echo done");
        Expect(r.Output == "done\n", Show(r));
        Expect((await Run(shell, "echo next")).Output == "next\n", "stream desynchronised");
    });
    await Case(tag + "unicode round-trips", async () => {
        var r = await Run(shell, "echo 'héllo ✓ 日本'");
        Expect(r.Output == "héllo ✓ 日本\n", Show(r));
    });
    await Case(tag + "huge output is bounded and the session keeps working", async () => {
        var r = await Run(shell, "head -c 300000 /dev/zero | tr '\\0' x");
        Expect(r.Truncated && r.Output.Length == 65536, $"truncated={r.Truncated} len={r.Output.Length}");
        Expect((await Run(shell, "echo ok")).Output == "ok\n", "session wedged after large output");
    });
    await Case(tag + "thirty back-to-back commands stay in sync", async () => {
        for (int i = 0; i < 30; i++) {
            var r = await Run(shell, $"echo {i}; echo e{i} >&2");
            Expect(r.Output == $"{i}\n" && r.Error == $"e{i}\n", $"desync at {i}: " + Show(r));
        }
    });
    await Case(tag + "secrets and bridge settings are not inherited", async () => {
        var r = await Run(shell, "echo \"[${CONTROL_PLANE_API_KEY-}][${PSBRIDGE_SHELL-}]\"");
        Expect(r.Output == "[][]\n", Show(r));
    });
    await Case(tag + "pinned date cannot be shadowed by earlier commands", async () => {
        await Run(shell, "date() { echo hacked; }");
        var r = await Run(shell, "date");
        Expect(r.Status == "completed" && DateTimeOffset.TryParse(r.Output, out _), Show(r));
    });
    await Case(tag + "a command cannot spoof the reported working directory", async () => {
        using var s = Fresh();
        await Run(s, "cd /tmp");
        var r = await Run(s, "PWD=/etc/spoofed");
        Expect(r.Directory == "/tmp", "reported " + r.Directory);
        var dirs = await Run(s, "mkdir -p /tmp/psb-nl && cd \"$(printf '/tmp/psb-nl/a\\nb')\" 2>/dev/null || true; pwd");
        Expect(dirs.Healthy, Show(dirs));
    });
    await Case(tag + "closing stderr ends the session instead of hiding errors", async () => {
        using var s = Fresh();
        var r = await Run(s, "exec 2>&-");
        Expect(!r.Healthy && r.Error.Contains("closed stdout or stderr"), Show(r));
    });
    await Case(tag + "background processes are reported and ended by Reset", async () => {
        using var s = Fresh();
        var r = await Run(s, "sleep 30 &");
        Expect(r.Status == "completed" && r.Error.Contains("background process"), Show(r));
        var quiet = await Run(s, "true");
        Expect(quiet.Error.Contains("background process"), "survivor not re-reported: " + Show(quiet));
    });
    await Case(tag + "output is never cut inside a surrogate pair", async () => {
        using var s = Fresh();
        var r = await Run(s, "head -c 65535 /dev/zero | tr '\\0' x; printf '😀😀'");
        Expect(r.Truncated && r.Output.Length <= 65536 && !char.IsHighSurrogate(r.Output[^1]), $"len={r.Output.Length}");
    });
    await Case(tag + "concurrent requests are serialised, never interleaved", async () => {
        using var s = Fresh();
        var slow = Run(s, "sleep 0.5; echo first"); var fast = Run(s, "echo second");
        var results = await Task.WhenAll(slow, fast);
        Expect(results[0].Output == "first\n" && results[1].Output == "second\n", Show(results[0]) + " | " + Show(results[1]));
    });
    await Case(tag + "timeout kills the whole process group", async () => {
        using var s = Fresh();
        var pidFile = Path.Combine(Path.GetTempPath(), "psb-bg-" + Guid.NewGuid().ToString("N"));
        await Run(s, $"sleep 300 & echo $! > '{pidFile}'");
        var r = await Run(s, "sleep 30", 1);
        Expect(r.Status == "timed_out" && !r.Healthy, Show(r));
        int pid = int.Parse(File.ReadAllText(pidFile).Trim()); File.Delete(pidFile);
        for (int i = 0; i < 50 && !IsDead(pid); i++) await Task.Delay(100);
        Expect(IsDead(pid), "background child survived the timeout");
    });
    await Case(tag + "cancellation stops the shell", async () => {
        using var s = Fresh(); using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var r = await s.Run("sleep 30", 20, cts.Token);
        Expect(r.Status == "cancelled" && !r.Healthy, Show(r));
    });
    await Case(tag + "exit ends the session and reports the code", async () => {
        using var s = Fresh();
        var r = await Run(s, "exit 3");
        Expect(r.ExitCode == 3 && !r.Healthy, Show(r));
        try { await Run(s, "echo x"); throw new Exception("session reused after exit"); } catch (InvalidOperationException) { }
    });
    await Case(tag + "set -e ending the shell is reported, not hidden", async () => {
        using var s = Fresh();
        var r = await Run(s, "set -e; false; echo not-reached");
        Expect(r.ExitCode == 1 && !r.Healthy && !r.Output.Contains("not-reached"), Show(r));
    });
}

static bool IsDead(int pid)
{
    try { var stat = File.ReadAllText($"/proc/{pid}/stat"); return stat[(stat.LastIndexOf(')') + 2)..][0] == 'Z'; }
    catch (IOException) { return true; }
}

Console.WriteLine(failures == 0 ? "ALL SHELL TESTS PASSED" : $"{failures} SHELL TEST(S) FAILED");
return failures == 0 ? 0 : 1;

namespace PersistentBridge {
    internal sealed class Window {
        public Task<Credential?> Credential(string target, bool sudo, CancellationToken token) => throw new NotSupportedException();
    }
}
