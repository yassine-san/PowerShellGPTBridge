using System.Text.Json;
namespace PersistentBridge;
internal sealed record WorkerRequest(string Command, int Seconds);
internal sealed record WorkerHello(string Identity, string Privilege, string Directory);
internal static class Wire
{
    public static async Task Send<T>(Stream stream, T value, CancellationToken token) {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 1_000_000) throw new IOException("IPC message too large");
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken token) {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header, token); int length = BitConverter.ToInt32(header);
        if (length < 1 || length > 1_000_000) throw new IOException("IPC message size invalid");
        byte[] body = new byte[length]; await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<T>(body) ?? throw new IOException("IPC message invalid");
    }
}
