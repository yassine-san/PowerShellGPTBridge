using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PersistentBridge;

internal static class DesktopAccess
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder name, int length, out int needed);

    private static string Name(IntPtr handle)
    {
        var name = new StringBuilder(256);
        if (!GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot inspect desktop name.");
        return name.ToString();
    }

    // Called on the UI thread. Does not change or switch desktops.
    public static void Check()
    {
        if (!SystemInformation.UserInteractive)
            throw new InvalidOperationException("The Windows Forms window station is not visible.");
        var input = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS
        if (input == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot access the current input desktop. Unlock the desktop.");
        try
        {
            if (Name(input) != Name(GetThreadDesktop(GetCurrentThreadId())))
                throw new InvalidOperationException("UI thread is not on the current input desktop.");
        }
        finally { CloseDesktop(input); }
    }
}
