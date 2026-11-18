using System.Text;
namespace PersistentBridge;

internal sealed class LimitedWrite(Stream inner, long limit, bool captureDiff = false) : Stream
{
    private long total;
    private readonly MemoryStream? preview = captureDiff ? new MemoryStream() : null;
    public string? NewFileDiff()
    {
        if (preview == null || total > 8192) return null;
        try {
            string value = new UTF8Encoding(false, true).GetString(preview.ToArray());
            if (value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) return null;
            if (value.Length == 0) return "(New empty file)";
            string normalized = value.ReplaceLineEndings("\n");
            string[] lines = (normalized.EndsWith('\n') ? normalized[..^1] : normalized).Split('\n');
            return $"@@ -0,0 +1,{lines.Length} @@\r\n" + string.Join("\r\n",lines.Select(line => "+" + line))
                + (normalized.EndsWith('\n') ? "" : "\r\n\\ No newline at end of file");
        } catch (DecoderFallbackException) { return null; }
    }
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => total; public override long Position { get => total; set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override void Write(byte[] buffer, int offset, int count) { if (total + count > limit) throw new IOException("Transfer limit exceeded"); inner.Write(buffer, offset, count); total += count;
        if (total <= 8192) preview?.Write(buffer, offset, count); }
    public override int Read(byte[] b, int o, int c) => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
}
