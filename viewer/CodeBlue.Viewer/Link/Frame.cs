namespace CodeBlue.Viewer.Link;

/// <summary>
/// Frame layer of the binary protocol (docs/development/protocol.md):
/// CRC-16/CCITT-FALSE plus COBS, with a 0x00 delimiter after each frame. A
/// port of firmware/main/frame.c; keep the two in step.
/// </summary>
internal static class Frame
{
    public const int MaxEncoded = 64;
    public const int MaxPayload = 60;

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    /// <summary>Builds a complete frame: COBS-encoded type, payload and CRC, then the delimiter.</summary>
    public static byte[] Encode(byte type, ReadOnlySpan<byte> payload = default)
    {
        if (payload.Length > MaxPayload)
            throw new ArgumentException($"Payload too long ({payload.Length} bytes).", nameof(payload));

        Span<byte> raw = stackalloc byte[payload.Length + 3];
        raw[0] = type;
        payload.CopyTo(raw[1..]);
        ushort crc = Crc16(raw[..(payload.Length + 1)]);
        raw[^2] = (byte)crc;
        raw[^1] = (byte)(crc >> 8);

        var output = new byte[raw.Length + raw.Length / 254 + 2];
        int codeAt = 0, o = 1;
        byte code = 1;
        foreach (byte b in raw)
        {
            if (b == 0)
            {
                output[codeAt] = code;
                codeAt = o++;
                code = 1;
            }
            else
            {
                output[o++] = b;
                if (++code == 0xFF)
                {
                    output[codeAt] = code;
                    codeAt = o++;
                    code = 1;
                }
            }
        }
        output[codeAt] = code;
        output[o++] = 0x00;
        return output[..o];
    }
}

/// <summary>Incremental receiver: feed it bytes as they arrive; invalid frames are dropped.</summary>
internal sealed class FrameReader
{
    private readonly byte[] _buf = new byte[Frame.MaxEncoded];
    private int _len;
    private bool _overflow;

    /// <summary>Returns true, with the frame's type and payload, when a byte completes a valid frame.</summary>
    public bool Feed(byte b, out byte type, out byte[] payload)
    {
        type = 0;
        payload = [];
        if (b != 0)
        {
            if (_len < _buf.Length) _buf[_len++] = b;
            else _overflow = true;
            return false;
        }

        bool ok = !_overflow && _len > 0 && TryDecode(_buf.AsSpan(0, _len), out type, out payload);
        _len = 0;
        _overflow = false;
        return ok;
    }

    private static bool TryDecode(ReadOnlySpan<byte> encoded, out byte type, out byte[] payload)
    {
        type = 0;
        payload = [];
        Span<byte> raw = stackalloc byte[Frame.MaxEncoded];
        int i = 0, o = 0;
        while (i < encoded.Length)
        {
            int code = encoded[i++];
            if (code == 0 || i + code - 1 > encoded.Length) return false;
            for (int k = 1; k < code; k++) raw[o++] = encoded[i++];
            if (code != 0xFF && i < encoded.Length) raw[o++] = 0;
        }
        if (o < 3) return false;

        ushort got = (ushort)(raw[o - 2] | (raw[o - 1] << 8));
        if (Frame.Crc16(raw[..(o - 2)]) != got) return false;
        type = raw[0];
        payload = raw[1..(o - 2)].ToArray();
        return true;
    }
}
