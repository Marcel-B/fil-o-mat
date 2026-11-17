using System.Buffers.Binary;
using System.Text;

namespace Filomat.Api.Printers;

/// <summary>
/// Liest die Slicer-Angaben (Filamentverbrauch, Typ) aus dem Kopf einer Binär-G-Code-Datei (<c>.bgcode</c>). Der
/// Druckermetadaten-Block steht gleich nach dem Dateikopf, unkomprimiert als <c>schlüssel=wert</c>-Zeilen; es reicht
/// also, den Anfang der Datei zu lesen. Aufbau: https://github.com/prusa3d/libbgcode/blob/main/doc/specifications.md
/// </summary>
public static class BgcodeMetadata
{
    /// <summary>So viel wird höchstens gelesen (Vorschaubilder stehen vor den Druckmetadaten).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    private const ushort FileMetadata = 0, GCode = 1, SlicerMetadata = 2, PrinterMetadata = 3, PrintMetadata = 4, Thumbnail = 5;

    /// <summary>
    /// Schlüssel und Werte aus dem Drucker- oder Druckmetadaten-Block, sobald einer davon den Filamentverbrauch nennt.
    /// <c>null</c>, wenn der Strom keine Binär-G-Code-Datei ist oder vorher endet.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[10];
        if (!await FillAsync(stream, header, cancellationToken) || Encoding.ASCII.GetString(header, 0, 4) != "GCDE") return null;
        var checksumSize = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) == 1 ? 4 : 0;
        long read = header.Length;

        var blockHeader = new byte[12];
        while (read < MaxBytes)
        {
            if (!await FillAsync(stream, blockHeader.AsMemory(0, 8), cancellationToken)) return null;
            var type = BinaryPrimitives.ReadUInt16LittleEndian(blockHeader);
            var compression = BinaryPrimitives.ReadUInt16LittleEndian(blockHeader.AsSpan(2));
            long size = BinaryPrimitives.ReadUInt32LittleEndian(blockHeader.AsSpan(4));
            read += 8;
            if (compression != 0)
            {
                if (!await FillAsync(stream, blockHeader.AsMemory(8, 4), cancellationToken)) return null;
                size = BinaryPrimitives.ReadUInt32LittleEndian(blockHeader.AsSpan(8));
                read += 4;
            }
            if (type == GCode) return null;
            var parameters = type == Thumbnail ? 6 : 2;

            if (type is PrinterMetadata or PrintMetadata && compression == 0 && size <= MaxBytes)
            {
                var block = new byte[parameters + size];
                if (!await FillAsync(stream, block, cancellationToken)) return null;
                var encoding = BinaryPrimitives.ReadUInt16LittleEndian(block);
                if (encoding == 0)
                {
                    var values = ParseIni(Encoding.UTF8.GetString(block, parameters, (int)size));
                    if (values.ContainsKey("filament used [g]") || values.ContainsKey("filament used [mm]")) return values;
                }
                if (!await SkipAsync(stream, checksumSize, cancellationToken)) return null;
            }
            else if (type is FileMetadata or PrinterMetadata or PrintMetadata or Thumbnail or SlicerMetadata)
            {
                if (!await SkipAsync(stream, parameters + size + checksumSize, cancellationToken)) return null;
            }
            else return null;
            read += parameters + size + checksumSize;
        }
        return null;
    }

    private static Dictionary<string, string> ParseIni(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return values;
    }

    private static Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
        FillAsync(stream, buffer.AsMemory(), cancellationToken);

    private static async Task<bool> FillAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private static async Task<bool> SkipAsync(Stream stream, long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (count > 0)
        {
            var chunk = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellationToken);
            if (chunk == 0) return false;
            count -= chunk;
        }
        return true;
    }
}
