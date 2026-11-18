using System.Diagnostics;
using System.Runtime.InteropServices;
namespace PersistentBridge;
internal static class WindowsParent
{
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr p,int type,[Out] IntPtr[] info,int bytes,out int used);
    public static int Id() {
        using var self=Process.GetCurrentProcess();var info=new IntPtr[6];
        if(NtQueryInformationProcess(self.Handle,0,info,6*IntPtr.Size,out _)!=0)throw new IOException("Cannot identify tunnel parent.");
        return info[5].ToInt32();
    }
}
