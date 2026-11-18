using System.Security.Cryptography;
namespace PersistentBridge;
internal static class HostKeyPin
{
    public static string Fingerprint(byte[] key) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
    public static bool Matches(string expected,byte[] key) => string.Equals(expected,Fingerprint(key),StringComparison.Ordinal);
    public static bool Accept(string? pin,byte[] received,Func<string,bool> reviewNew,
        Action<string,bool> auditKnown,CancellationToken token)
    {
        if(token.IsCancellationRequested) return false;
        string fingerprint=Fingerprint(received);
        if(pin==null) return reviewNew(fingerprint) && !token.IsCancellationRequested;
        bool matches=string.Equals(pin,fingerprint,StringComparison.Ordinal);
        auditKnown(fingerprint,matches);
        return matches && !token.IsCancellationRequested;
    }
}
