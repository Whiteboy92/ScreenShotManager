using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ScreenShotManager.MusicSync.Services;

/// <summary>
/// Whole-file SHA-256 (what the phone verifies a download against) plus, for MP3s, a SHA-256 of
/// the audio alone with ID3v2/ID3v1/APEv2/Lyrics3 tags cut off. Tag editors rewrite the whole file,
/// so without the audio hash fixing one artist name would make the phone download the song again.
/// Both come from one sequential read.
/// </summary>
internal static class FileHashes
{
    private const int MaxTagsPerEnd = 4;

    /// <summary>The audio hash is null for formats whose tags we don't strip.</summary>
    public static (string Sha256, string? AudioSha256) Compute(FileStream stream)
    {
        var range = Path.GetExtension(stream.Name).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            ? Mp3AudioRange(stream)
            : null;

        stream.Position = 0;
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var audio = range == null ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long offset = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            whole.AppendData(buffer, 0, read);
            if (audio != null)
            {
                var from = Math.Max(offset, range!.Value.Start);
                var to = Math.Min(offset + read, range.Value.End);
                if (to > from)
                {
                    audio.AppendData(buffer, (int)(from - offset), (int)(to - from));
                }
            }

            offset += read;
        }

        return (Hex(whole), audio == null ? null : Hex(audio));
    }

    private static string Hex(IncrementalHash hash) => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

    /// <summary>Byte range between the leading and trailing tags; null when the file is too odd to trust.</summary>
    internal static (long Start, long End)? Mp3AudioRange(Stream stream)
    {
        var length = stream.Length;
        long start = 0;
        var header = new byte[10];

        // ID3v2 at the front (occasionally more than one), each followed by optional zero padding.
        for (var i = 0; i < MaxTagsPerEnd && ReadAt(stream, start, header) && header.AsSpan(0, 3).SequenceEqual("ID3"u8); i++)
        {
            if (((header[6] | header[7] | header[8] | header[9]) & 0x80) != 0)
            {
                return null;
            }

            var size = (header[6] << 21) | (header[7] << 14) | (header[8] << 7) | header[9];
            var hasFooter = (header[5] & 0x10) != 0;
            start += 10 + size + (hasFooter ? 10 : 0);
        }

        start = SkipZeros(stream, start, length);

        var end = length;
        var tail = new byte[128];
        for (var i = 0; i < MaxTagsPerEnd && end - start > 128; i++)
        {
            // ID3v1: fixed 128 bytes.
            if (ReadAt(stream, end - 128, tail) && tail.AsSpan(0, 3).SequenceEqual("TAG"u8))
            {
                end -= 128;
                continue;
            }

            // APEv2: 32-byte footer whose size covers items + footer, plus a 32-byte header if flagged.
            var footer = new byte[32];
            if (ReadAt(stream, end - 32, footer) && footer.AsSpan(0, 8).SequenceEqual("APETAGEX"u8))
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(footer.AsSpan(12));
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(20));
                var total = (long)size + ((flags & 0x80000000) != 0 ? 32 : 0);
                if (size < 32 || total > end - start)
                {
                    return null;
                }

                end -= total;
                continue;
            }

            // Lyrics3v2: "LYRICS200" preceded by a 6-digit size of everything from "LYRICSBEGIN".
            var lyrics = new byte[15];
            if (ReadAt(stream, end - 15, lyrics) && lyrics.AsSpan(6, 9).SequenceEqual("LYRICS200"u8)
                && int.TryParse(Encoding.ASCII.GetString(lyrics, 0, 6), out var lyricsSize))
            {
                if (lyricsSize + 15 > end - start)
                {
                    return null;
                }

                end -= lyricsSize + 15;
                continue;
            }

            break;
        }

        return end > start ? (start, end) : null;
    }

    private static long SkipZeros(Stream stream, long position, long length)
    {
        var buffer = new byte[4096];
        while (position < length && ReadAt(stream, position, buffer, allowShort: true))
        {
            var count = (int)Math.Min(buffer.Length, length - position);
            var firstNonZero = buffer.AsSpan(0, count).IndexOfAnyExcept((byte)0);
            if (firstNonZero >= 0)
            {
                return position + firstNonZero;
            }

            position += count;
        }

        return position;
    }

    private static bool ReadAt(Stream stream, long position, byte[] buffer, bool allowShort = false)
    {
        if (position < 0 || (!allowShort && position + buffer.Length > stream.Length))
        {
            return false;
        }

        stream.Position = position;
        var total = 0;
        int read;
        while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += read;
        }

        return allowShort ? total > 0 : total == buffer.Length;
    }
}
