using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PersistentBridge;

internal sealed class Audit
{
    private readonly string path;
    private readonly object sync = new();

    public string Root => Path.GetDirectoryName(path)!;

    public Audit()
    {
#if LINUX_HOST
        var root = LinuxOS.PrivateRoot();
#else
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Missing user SID");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        var root = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "PersistentPSBridge"));
        root.Create(acl);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Unexpected audit directory link");
        root.SetAccessControl(acl);
        #endif
        // Each launch creates a private new log, inheriting the protected directory ACL.
        path = Path.Combine(root.FullName, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    public object Command(string? value)
    {
        if (value == null) return new { missing = true };
        return new { length = value.Length, sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))) };
    }

    public void Write(string kind, object data)
    {
        var line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, data });
        lock (sync) File.AppendAllText(path, line + Environment.NewLine);
    }
}
