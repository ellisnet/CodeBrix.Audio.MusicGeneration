using System;
using System.IO;

namespace CodeBrix.Audio.MusicGeneration.Tests;

/// <summary>
/// A writable stream that CANNOT SEEK - a pipe, a socket, a compressing stream.
/// </summary>
/// <remarks>
/// It is the difference between a format that patches its header when it knows the length of the
/// audio (WAV, AIFF) and one written strictly forwards (Ogg, Opus): the first refuses this stream
/// and the second takes it.
/// </remarks>
internal sealed class ForwardOnlyStream : Stream
{
    private readonly MemoryStream inner = new MemoryStream();

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => inner.Length;

    /// <summary>How many bytes have been written to it.</summary>
    public long BytesWritten => inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => inner.Position;
        set => throw new NotSupportedException("This stream cannot seek.");
    }

    /// <inheritdoc />
    public override void Flush() => inner.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("This stream cannot be read.");

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("This stream cannot seek.");

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException("This stream cannot be resized.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        inner.Write(buffer, offset, count);

    /// <summary>Everything written to it so far.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray() => inner.ToArray();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
