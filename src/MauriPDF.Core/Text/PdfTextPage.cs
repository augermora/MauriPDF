using System.Text;

namespace MauriPDF.Core.Text;

/// <summary>Normalized visible-page coordinates: top-left origin, independent of raster size.</summary>
public readonly record struct TextPoint(double X, double Y);

public readonly record struct TextBounds(double Left, double Top, double Right, double Bottom)
{
    public bool HasArea => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right)
        && double.IsFinite(Bottom) && Right > Left && Bottom > Top;
}

/// <summary>Array position is PDFium's logical character index, including generated spaces/newlines.</summary>
public readonly record struct TextCharacter(uint Unicode, TextBounds Bounds);

/// <summary>Immutable managed data. Safe to share between the worker cache and selection; no native resources.</summary>
public sealed class PdfTextPage
{
    public const int MaximumCharacters = 32_768;
    private readonly TextCharacter[] _characters;

    public PdfTextPage(ReadOnlySpan<TextCharacter> characters)
    {
        if (characters.Length > MaximumCharacters) throw new ArgumentOutOfRangeException(nameof(characters));
        _characters = characters.ToArray();
        // PDFium on Windows may expose a supplementary scalar as two UTF-16 character entries.
        // Keep native indices stable, but store the scalar once and make the continuation nonselectable.
        for (int index = 0; index + 1 < _characters.Length; index++)
        {
            uint high = _characters[index].Unicode, low = _characters[index + 1].Unicode;
            if (high is < 0xD800 or > 0xDBFF || low is < 0xDC00 or > 0xDFFF) continue;
            TextBounds a = _characters[index].Bounds, b = _characters[index + 1].Bounds;
            TextBounds bounds = !a.HasArea ? b : !b.HasArea ? a : new(
                Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
            _characters[index] = new(0x10000 + ((high - 0xD800) << 10) + low - 0xDC00, bounds);
            _characters[index + 1] = default;
            index++;
        }
    }

    public int Count => _characters.Length;
    public long EstimatedBytes => 128L + Count * 40L;
    public TextCharacter this[int index] => _characters[index];

    /// <summary>Returns an insertion offset. Distances are measured in display pixels, not raster pixels.</summary>
    public int? HitTest(TextPoint point, double displayWidth, double displayHeight, double tolerancePixels)
    {
        int nearest = -1;
        double distance = tolerancePixels * tolerancePixels;
        for (int index = 0; index < Count; index++)
        {
            TextCharacter character = _characters[index];
            TextBounds box = character.Bounds;
            if (!box.HasArea || character.Unicode is 0 or 10 or 13) continue;
            double dx = (point.X - Math.Clamp(point.X, box.Left, box.Right)) * displayWidth;
            double dy = (point.Y - Math.Clamp(point.Y, box.Top, box.Bottom)) * displayHeight;
            double squared = dx * dx + dy * dy;
            if (squared > distance || (nearest >= 0 && squared == distance)) continue;
            nearest = index;
            distance = squared;
        }
        if (nearest < 0) return null;
        TextBounds hit = _characters[nearest].Bounds;
        // Infer advance direction from a nearby character on the same line (also handles page rotation).
        double vx = 1, vy = 0;
        int neighbor = nearest + 1 < Count ? nearest + 1 : nearest - 1;
        if (neighbor >= 0 && (_characters[neighbor].Unicode is 0 or 10 or 13 || !_characters[neighbor].Bounds.HasArea)) neighbor = nearest - 1;
        if (neighbor >= 0 && _characters[neighbor].Bounds.HasArea)
        {
            TextBounds other = _characters[neighbor].Bounds;
            double dx = (other.Left + other.Right - hit.Left - hit.Right) * displayWidth;
            double dy = (other.Top + other.Bottom - hit.Top - hit.Bottom) * displayHeight;
            double limit = 4 * Math.Max((hit.Right - hit.Left) * displayWidth, (hit.Bottom - hit.Top) * displayHeight);
            if (Math.Abs(dx) + Math.Abs(dy) < limit && (Math.Abs(dx) > 0 || Math.Abs(dy) > 0))
            {
                vx = neighbor > nearest ? dx : -dx;
                vy = neighbor > nearest ? dy : -dy;
            }
        }
        double projection = (point.X - (hit.Left + hit.Right) / 2) * displayWidth * vx
            + (point.Y - (hit.Top + hit.Bottom) / 2) * displayHeight * vy;
        return nearest + (projection >= 0 ? 1 : 0);
    }

    public void AppendText(StringBuilder output, int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, Count);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        Span<char> utf16 = stackalloc char[2];
        for (int index = start; index < end; index++)
        {
            uint code = _characters[index].Unicode;
            if (code == 0) continue; // Unmapped characters are not invented.
            if (Rune.TryCreate(code, out Rune rune)) output.Append(utf16[..rune.EncodeToUtf16(utf16)]);
            else output.Append(Rune.ReplacementChar.ToString());
        }
    }
}
