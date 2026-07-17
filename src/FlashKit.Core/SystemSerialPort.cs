using System.IO.Ports;

namespace FlashKit.Core;

/// <summary>Adapter from <see cref="ISerialPort"/> to <c>System.IO.Ports.SerialPort</c>.</summary>
public sealed class SystemSerialPort : ISerialPort, IDisposable
{
    readonly SerialPort inner;

    public SystemSerialPort(string portName)
    {
        inner = new SerialPort(portName);
    }

    public string PortName => inner.PortName;

    public int ReadTimeout
    {
        get => inner.ReadTimeout;
        set => inner.ReadTimeout = value;
    }

    public int WriteTimeout
    {
        get => inner.WriteTimeout;
        set => inner.WriteTimeout = value;
    }

    public void Open() => inner.Open();
    public void Close() => inner.Close();
    public void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public int ReadByte() => inner.ReadByte();
    public void Dispose() => inner.Dispose();
}
