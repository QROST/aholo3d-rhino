namespace Aholo3D.Core;

/// <summary>A non-owning bounded input; compressed attributes cannot consume their neighbours.</summary>
internal sealed class SpzBoundedStream(Stream input, long remaining) : Stream
{
    internal long Remaining { get; private set; } = remaining;
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        int read = input.Read(buffer[..(int)Math.Min(buffer.Length, Remaining)]);
        Remaining -= read;
        return read;
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Tracks the complete legacy payload so a missing gzip trailer cannot be accepted as success.</summary>
internal sealed class SpzCrcStream(Stream input) : Stream
{
    private static readonly uint[] Table = MakeTable();
    private uint _crc = uint.MaxValue;
    internal uint Crc => ~_crc;
    internal uint ModuloLength { get; private set; }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        int read = input.Read(buffer);
        foreach (byte value in buffer[..read]) _crc = Table[(_crc ^ value) & 255] ^ (_crc >> 8);
        ModuloLength = unchecked(ModuloLength + (uint)read);
        return read;
    }
    private static uint[] MakeTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; ++i)
        {
            uint value = i;
            for (int bit = 0; bit < 8; ++bit) value = (value >> 1) ^ ((value & 1) == 0 ? 0u : 0xEDB88320u);
            table[i] = value;
        }
        return table;
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
