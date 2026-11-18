using System.Security.Principal;
namespace PersistentBridge;
internal static class IdentityInfo
{
    public static string Name => WindowsIdentity.GetCurrent().Name;
    public static bool Elevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static string Privilege => Elevated ? "ELEVATED — ADMINISTRATOR" : "STANDARD USER";
}
