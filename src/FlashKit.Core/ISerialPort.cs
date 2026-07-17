namespace FlashKit.Core;

/// <summary>
/// Minimal serial-port surface used by <see cref="Device"/>. Production code
/// wraps <c>System.IO.Ports.SerialPort</c> via <see cref="SystemSerialPort"/>;
/// tests substitute in-memory fakes so CI never needs the programmer hardware.
/// </summary>
public interface ISerialPort
{
    string PortName { get; }
    int ReadTimeout { get; set; }
    int WriteTimeout { get; set; }
    void Open();
    void Close();
    void Write(byte[] buffer, int offset, int count);
    int Read(byte[] buffer, int offset, int count);
    int ReadByte();
}
