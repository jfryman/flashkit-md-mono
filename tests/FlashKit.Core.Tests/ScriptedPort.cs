namespace FlashKit.Core.Tests;

/// <summary>
/// Records every byte written by <see cref="Device"/> and plays back canned
/// replies. Used to lock the exact wire format of the original client.
/// </summary>
sealed class ScriptedPort : ISerialPort
{
    public readonly List<byte> Written = new();
    readonly Queue<byte> replies = new();

    public string PortName => "SCRIPTED";
    public int ReadTimeout { get; set; }
    public int WriteTimeout { get; set; }

    public void EnqueueReply(params byte[] bytes)
    {
        foreach (var b in bytes) replies.Enqueue(b);
    }

    public void Open() { }
    public void Close() { }

    public void Write(byte[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++) Written.Add(buffer[offset + i]);
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (replies.Count == 0) throw new TimeoutException();
        int n = 0;
        while (n < count && replies.Count > 0) buffer[offset + n++] = replies.Dequeue();
        return n;
    }

    public int ReadByte()
    {
        if (replies.Count == 0) throw new TimeoutException();
        return replies.Dequeue();
    }
}
