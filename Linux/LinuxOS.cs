using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PersistentBridge;

internal static class LinuxOS
{
    [DllImport("libc")] internal static extern uint geteuid();
    [DllImport("libc")] internal static extern uint umask(uint mask);
    [DllImport("libc")] internal static extern int getppid();
    [DllImport("libc")] private static extern int setpgid(int pid,int pgid);
    [DllImport("libc")] private static extern int prctl(int option, ulong arg, ulong b, ulong c, ulong d);
    [DllImport("libc")] internal static extern int kill(int pid,int signal);
    [StructLayout(LayoutKind.Explicit, Size=256)] private struct Statx {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
    }
    [DllImport("libc", SetLastError=true)] private static extern int statx(int dirfd,string path,int flags,uint mask,out Statx stat);
    internal static DirectoryInfo PrivateRoot()
    {
        if(!OperatingSystem.IsLinux())throw new PlatformNotSupportedException();
        var root = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local","state","powershellgptbridge"));
        Directory.CreateDirectory(root.FullName,UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (statx(-100,root.FullName,0x100,0x7ff,out var stat) != 0 || stat.Uid != geteuid() ||
            (stat.Mode & 0xf000) != 0x4000 || (stat.Mode & 0x1ff) != 0x1c0)
            throw new IOException("State directory must be a user-owned directory with mode 0700.");
        return root;
    }
    [DllImport("libc")] private static extern int fcntl(int fd,int command,int minimum);
    [DllImport("libc")] private static extern int dup2(int from,int to);
    internal static FileStream TakeProtocolStream(int fd,FileAccess access)
    {
        // Private CLOEXEC copy. Native commands subsequently inherit /dev/null for these descriptors.
        int copy=fcntl(fd,1030,3);
        if(copy<0) throw new IOException("Could not isolate worker protocol.");
        var handle=new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)copy,true);
        using var sink=File.OpenHandle("/dev/null",FileMode.Open,FileAccess.ReadWrite);
        if(dup2(sink.DangerousGetHandle().ToInt32(),fd)<0) {handle.Dispose();throw new IOException("Could not redirect worker console.");}
        return new FileStream(handle,access);
    }
    internal static bool PrepareWorker(int parent) => setpgid(0,0)==0 && prctl(1,9,0,0,0)==0 && getppid()==parent && geteuid()!=0;
    internal static bool Graphical => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
}
internal static class IdentityInfo {
    public static string Name => Environment.UserName;
    public static bool Elevated => LinuxOS.geteuid()==0;
    public static string Privilege => Elevated ? "ROOT" : "STANDARD USER";
}
internal sealed record Credential(string Username,string Method,string Secret,string KeyPath,bool Save=false);
internal static class CredentialStore {
    public static string Root => LinuxOS.PrivateRoot().FullName;
    public static Credential? Load(string target) => null;
    public static void Save(string target,Credential value) { } // Memory only, retained by the authenticated SSH session.
    public static void Forget(string target) { }
    public static Task<Credential?> Prompt(Window window,string target,CancellationToken token,bool sudo=false) => window.Credential(target,sudo,token);
}
