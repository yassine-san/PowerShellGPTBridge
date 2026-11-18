using PersistentBridge;
using System.Text;
using System.Text.Json;
var root = Directory.CreateTempSubdirectory();
try {
    var history = new History(root.FullName);
    var token = "synthetic-private-value";
    var op = new Operation(Guid.NewGuid(),"pc","Local","operator","STANDARD USER",token,token,30,token,token,"unverified",Resources:token);
    var entry = new HistoryEntry(op,Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Reason:token);
    void Check(string label, bool condition) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    history.Save(entry);
    Check("pending approval interrupted before dispatch",history.Load().Single().Status == "interrupted_before_execution");
    entry = entry with { Status = "running",Started = DateTimeOffset.UtcNow };
    history.Save(entry);
    Check("interrupted dispatch recovered as unknown",history.Load().Single().Status == "result_unknown");
    entry = entry with { Status = "completed",Ended = DateTimeOffset.UtcNow,Result = new("completed",token,token,FileDiff:token) };
    history.Save(entry);
    Check("atomic updates preserve a single record",Directory.GetFiles(root.FullName).Length == 1 && history.Load().Single().Status == "completed");
    Check("free text and raw diff excluded from automatic storage",!File.ReadAllText(Directory.GetFiles(root.FullName).Single()).Contains(token));
    File.SetLastWriteTimeUtc(Directory.GetFiles(root.FullName).Single(),DateTime.UtcNow.AddDays(-31));
    Check("expired history removed on launch",!history.Load().Any() && Directory.GetFiles(root.FullName).Length == 0);
    string? Diff(byte[] bytes,bool consent) { using var output = new MemoryStream(); using var capture = new LimitedWrite(output,20000,consent); capture.Write(bytes,0,bytes.Length); return capture.NewFileDiff(); }
    Check("capture requires explicit selection",Diff("test"u8.ToArray(),false) == null);
    Check("new text file shows observed additions",Diff("x\r\ny\n"u8.ToArray(),true) == "@@ -0,0 +1,2 @@\r\n+x\r\n+y");
    Check("empty file evidence",Diff([],true) == "(New empty file)");
    Check("large and binary previews excluded",Diff(new byte[8193],true) == null && Diff([0xff],true) == null && Diff([0],true) == null);
    if (OperatingSystem.IsWindows()) {
        history.Save(entry); history.KeepReviewed(entry,[0]);
        var saved = history.Load().Single();
        Check("DPAPI round trip retains only selected field",saved.Op.Command == token && saved.Result!.Output != token);
        history.KeepReviewed(entry,[]); Check("reviewed copy removed",history.Load().Single().Op.Command != token);
    }
} finally { root.Delete(true); }
