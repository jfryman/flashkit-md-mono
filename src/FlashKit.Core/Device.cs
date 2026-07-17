namespace FlashKit.Core;

/// <summary>
/// Wire protocol to the FlashKit MD programmer.
///
/// Ported from the original Windows client's Device.cs with logic, wire
/// format, and method names kept verbatim for diffability against
/// src-extracted/flashkit-md/Device.cs. Differences from the original:
/// the static SerialPort field is replaced by an injected ISerialPort,
/// and connect()'s port scanning lives in DeviceConnector.
/// </summary>
public class Device
{
    const byte CMD_ADDR = 0;
    const byte CMD_LEN = 1;
    const byte CMD_RD = 2;
    const byte CMD_WR = 3;
    const byte CMD_RY = 4;
    const byte CMD_DELAY = 5;
    const byte PAR_INC = 128;
    const byte PAR_SINGE = 64;
    const byte PAR_DEV_ID = 32;
    const byte PAR_MODE8 = 16;

    readonly ISerialPort port;

    public Device(ISerialPort port)
    {
        this.port = port;
    }

    public string getPortName()
    {
        return port.PortName;
    }

    public int getID()
    {
        int id;
        byte[] cmd = new byte[1];
        cmd[0] = CMD_RD | PAR_SINGE | PAR_DEV_ID;
        port.Write(cmd, 0, 1);
        id = port.ReadByte() << 8;
        id |= port.ReadByte();
        return id;
    }

    public void setDelay(int val)
    {
        byte[] cmd = new byte[2];
        cmd[0] = CMD_DELAY;
        cmd[1] = (byte)val;
        port.Write(cmd, 0, cmd.Length);
    }

    public UInt16 readWord(int addr)
    {
        UInt16 val = 0;
        addr /= 2;

        byte[] cmd = new byte[7];

        cmd[0] = CMD_ADDR;
        cmd[1] = (byte)(addr >> 16);
        cmd[2] = CMD_ADDR;
        cmd[3] = (byte)(addr >> 8);
        cmd[4] = CMD_ADDR;
        cmd[5] = (byte)(addr);

        cmd[6] = CMD_RD | PAR_SINGE;

        port.Write(cmd, 0, cmd.Length);

        val = (UInt16)(port.ReadByte() << 8);
        val |= (UInt16)port.ReadByte();

        return val;
    }

    public void writeWord(int addr, UInt16 data)
    {
        byte[] cmd = new byte[9];
        addr /= 2;

        cmd[0] = CMD_ADDR;
        cmd[1] = (byte)(addr >> 16);
        cmd[2] = CMD_ADDR;
        cmd[3] = (byte)(addr >> 8);
        cmd[4] = CMD_ADDR;
        cmd[5] = (byte)(addr);

        cmd[6] = CMD_WR | PAR_SINGE;
        cmd[7] = (byte)(data >> 8);
        cmd[8] = (byte)(data);

        port.Write(cmd, 0, 9);
    }

    public void writeByte(int addr, byte data)
    {
        byte[] cmd = new byte[8];
        addr /= 2;

        cmd[0] = CMD_ADDR;
        cmd[1] = (byte)(addr >> 16);
        cmd[2] = CMD_ADDR;
        cmd[3] = (byte)(addr >> 8);
        cmd[4] = CMD_ADDR;
        cmd[5] = (byte)(addr);

        cmd[6] = CMD_WR | PAR_SINGE | PAR_MODE8;
        cmd[7] = data;

        port.Write(cmd, 0, cmd.Length);
    }

    public void read(byte[] buff, int offset, int len)
    {
        int rd_len;

        while (len > 0)
        {
            rd_len = len > 65536 ? 65536 : len;
            byte[] cmd = new byte[5];
            cmd[0] = CMD_LEN;
            cmd[1] = (byte)(rd_len / 2 >> 8);
            cmd[2] = CMD_LEN;
            cmd[3] = (byte)(rd_len / 2);
            cmd[4] = CMD_RD | PAR_INC;

            port.Write(cmd, 0, 5);

            for (int i = 0; i < rd_len; )
            {
                i += port.Read(buff, offset + i, rd_len - i);
            }
            len -= rd_len;
            offset += rd_len;
        }
    }

    public void write(byte[] buff, int offset, int len)
    {
        int wr_len;

        while (len > 0)
        {
            wr_len = len > 65536 ? 65536 : len;
            byte[] cmd = new byte[5];
            cmd[0] = CMD_LEN;
            cmd[1] = (byte)(wr_len / 2 >> 8);
            cmd[2] = CMD_LEN;
            cmd[3] = (byte)(wr_len / 2);
            cmd[4] = CMD_WR | PAR_INC;

            port.Write(cmd, 0, 5);

            port.Write(buff, offset, wr_len);

            len -= wr_len;
            offset += wr_len;
        }
    }

    public void setAddr(int addr)
    {
        byte[] buff = new byte[6];
        addr /= 2;

        buff[0] = CMD_ADDR;
        buff[1] = (byte)(addr >> 16);
        buff[2] = CMD_ADDR;
        buff[3] = (byte)(addr >> 8);
        buff[4] = CMD_ADDR;
        buff[5] = (byte)(addr);

        port.Write(buff, 0, 6);
    }

    public void flashErase(int addr)
    {
        byte[] cmd;
        addr /= 2;

        cmd = new byte[8 * 8];

        for (int i = 0; i < cmd.Length; i += 8)
        {
            cmd[0 + i] = CMD_ADDR;
            cmd[1 + i] = (byte)(addr >> 16);
            cmd[2 + i] = CMD_ADDR;
            cmd[3 + i] = (byte)(addr >> 8);
            cmd[4 + i] = CMD_ADDR;
            cmd[5 + i] = (byte)(addr);

            cmd[6 + i] = CMD_WR | PAR_SINGE | PAR_MODE8;
            cmd[7 + i] = 0x30;
            addr += 4096;
        }

        writeWord(0x555 * 2, 0xaa);
        writeWord(0x2aa * 2, 0x55);
        writeWord(0x555 * 2, 0x80);
        writeWord(0x555 * 2, 0xaa);
        writeWord(0x2aa * 2, 0x55);

        port.Write(cmd, 0, cmd.Length);
        flashRY();
    }

    void flashRY()
    {
        byte[] cmd = new byte[2];
        cmd[0] = CMD_RY;
        cmd[1] = CMD_RD | PAR_SINGE;

        port.Write(cmd, 0, 2);
        port.ReadByte();
        port.ReadByte();
    }

    public void flashUnlockBypass()
    {
        writeByte(0x555 * 2, 0xaa);
        writeByte(0x2aa * 2, 0x55);
        writeByte(0x555 * 2, 0x20);
    }

    public void flashResetByPass()
    {
        writeWord(0, 0xf0);
        writeByte(0, 0x90);
        writeByte(0, 0x00);
    }

    public void flashWrite(byte[] buff, int offset, int len)
    {
        len /= 2;
        byte[] cmd = new byte[6 * len];

        for (int i = 0; i < cmd.Length; i += 6)
        {
            cmd[0 + i] = CMD_WR | PAR_SINGE | PAR_MODE8;
            cmd[1 + i] = 0xA0;
            cmd[2 + i] = CMD_WR | PAR_SINGE | PAR_INC;
            cmd[3 + i] = buff[offset++];
            cmd[4 + i] = buff[offset++];
            cmd[5 + i] = CMD_RY;
        }

        port.Write(cmd, 0, cmd.Length);
    }
}
