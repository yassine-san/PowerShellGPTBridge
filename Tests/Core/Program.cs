using System.Management.Automation.Runspaces;
using PersistentBridge;
using System.Text.Json;
using System.IO.Pipes;
using var space = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2()); space.Open();
var results = new List<object>();
async Task Check(string name, string script, Func<Execution,bool> test) {
    var result = await Worker.Execute(space, script);
    bool pass = test(result); results.Add(new { name, status = pass ? "PASS" : "FAIL", result });
    if (!pass) Environment.ExitCode = 1;
}
await Check("set variable", "$Test = 123", r => r.Status == "completed");
await Check("read persistent variable", "$Test", r => r.Output.Trim() == "123");
await Check("define function", "function BridgeTest { 'function-preserved' }", r => r.Status == "completed");
await Check("call persistent function", "BridgeTest", r => r.Output.Trim() == "function-preserved");
await Check("change working directory", "Set-Location ([System.IO.Path]::GetTempPath())", r => r.Status == "completed");
await Check("read persistent working directory", "(Get-Location).Path", r => r.Output.Trim().TrimEnd(Path.DirectorySeparatorChar) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
await Check("bounded success stream", "1..20000 | ForEach-Object { 'abcdefghij' }", r => r.Truncated && r.Output.Length <= 65536);
await Check("bounded error stream", "1..10000 | ForEach-Object { Write-Error 'intentional-error' }", r => r.Status == "failed" && r.Truncated && r.Error.Length <= 65536);
await Check("native exit not invented", "'done'", r => r.ExitCode == null);
await Check("output and error together", "'before'; Write-Error 'expected'; 'after'", r => r.Output.Contains("before") && r.Output.Contains("after") && r.Error.Contains("expected"));
await Check("shadow Get-Date", "function Get-Date { throw 'must not execute' }", r => r.Status == "completed");
await Check("green date pins cmdlet", "Get-Date", r => r.Status == "completed" && DateTime.TryParse(r.Output, out _));
await Check("managed console streams", "[Console]::Write('stdout'); [Console]::Error.Write('stderr')", r => r.Output.Contains("stdout") && r.Error.Contains("stderr"));
await Check("warning is not failure", "Write-Warning 'warning'", r => r.Status == "completed" && r.Error.Contains("warning"));
using (var wire = new MemoryStream()) {
    await Wire.Send(wire, new WorkerRequest("$Test = 123", 10), CancellationToken.None); wire.Position = 0;
    var request = await Wire.Read<WorkerRequest>(wire, CancellationToken.None);
    bool ok = request.Command == "$Test = 123" && request.Seconds == 10;
    results.Add(new { name = "bounded IPC round trip", status = ok ? "PASS" : "FAIL" }); if(!ok) Environment.ExitCode=1;
}
using (var wire = new MemoryStream(BitConverter.GetBytes(1_000_001))) {
    bool refused = false; try { await Wire.Read<WorkerRequest>(wire, CancellationToken.None); } catch(IOException) { refused=true; }
    results.Add(new { name = "oversize IPC rejected before allocation", status = refused ? "PASS" : "FAIL" }); if(!refused) Environment.ExitCode=1;
}
Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented = true }));
