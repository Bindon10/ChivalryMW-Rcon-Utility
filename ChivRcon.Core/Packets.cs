using System.Text;

namespace ChivRcon.Core;

/// <summary>
/// Builds an outgoing frame. Wire format (from AOCRConPacket.uc, all big-endian):
///   ushort MessageType | int PayloadLength | payload bytes
/// Strings are: int Length | UTF-8 bytes. QWORDs (SteamID64) are written as
/// Uid.B (high dword) then Uid.A (low dword), i.e. a plain big-endian uint64.
/// </summary>
public sealed class PacketBuilder
{
    private readonly List<byte> _payload = new();

    public PacketBuilder AddInt32(int value)
    {
        _payload.Add((byte)(value >> 24));
        _payload.Add((byte)(value >> 16));
        _payload.Add((byte)(value >> 8));
        _payload.Add((byte)value);
        return this;
    }

    public PacketBuilder AddUInt64(ulong value)
    {
        for (int shift = 56; shift >= 0; shift -= 8)
            _payload.Add((byte)(value >> shift));
        return this;
    }

    public PacketBuilder AddString(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value ?? string.Empty);
        AddInt32(utf8.Length);
        _payload.AddRange(utf8);
        return this;
    }

    /// <summary>Produce the complete frame (header + payload) for the given type.</summary>
    public byte[] Build(RconMessageType type)
    {
        var frame = new byte[6 + _payload.Count];
        frame[0] = (byte)((ushort)type >> 8);
        frame[1] = (byte)(ushort)type;
        int len = _payload.Count;
        frame[2] = (byte)(len >> 24);
        frame[3] = (byte)(len >> 16);
        frame[4] = (byte)(len >> 8);
        frame[5] = (byte)len;
        _payload.CopyTo(frame, 6);
        return frame;
    }
}

/// <summary>Sequentially reads values out of a received payload.</summary>
public sealed class PacketReader
{
    private readonly byte[] _data;
    private int _pos;

    public PacketReader(byte[] payload) => _data = payload;

    public int Remaining => _data.Length - _pos;

    public int ReadInt32()
    {
        EnsureAvailable(4);
        int v = (_data[_pos] << 24) | (_data[_pos + 1] << 16) | (_data[_pos + 2] << 8) | _data[_pos + 3];
        _pos += 4;
        return v;
    }

    public ulong ReadUInt64()
    {
        EnsureAvailable(8);
        ulong v = 0;
        for (int i = 0; i < 8; i++)
            v = (v << 8) | _data[_pos + i];
        _pos += 8;
        return v;
    }

    public string ReadString()
    {
        int len = ReadInt32();
        if (len < 0 || len > Remaining)
            throw new InvalidDataException($"Bad string length {len} (remaining {Remaining}).");
        string s = Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return s;
    }

    /// <summary>Reads a string if enough bytes remain, otherwise null (e.g. optional weapon name on KILL).</summary>
    public string? TryReadString() => Remaining >= 4 ? ReadString() : null;

    private void EnsureAvailable(int count)
    {
        if (Remaining < count)
            throw new InvalidDataException($"Payload truncated: need {count}, have {Remaining}.");
    }
}
