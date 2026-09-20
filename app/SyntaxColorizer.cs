using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace LuminaIDE;

/// <summary>Paints the token spans produced by the Rust tokenizer, using the current theme's colours.</summary>
public sealed class SyntaxColorizer : DocumentColorizingTransformer
{
    // Flat [start, len, kind, ...] triples, sorted by start, non-overlapping.
    int[] _spans = [];

    public void SetSpans(int[] spans) => _spans = spans;

    /// <summary>
    /// Keeps existing spans roughly in place while the user types, until the
    /// debounced re-tokenize arrives. Spans that overlap an edit are trimmed.
    /// </summary>
    public void Adjust(int offset, int removed, int inserted)
    {
        var spans = _spans;
        int delta = inserted - removed, end = offset + removed;
        for (int i = 0; i + 2 < spans.Length; i += 3)
        {
            int s = spans[i], e = s + spans[i + 1];
            if (e <= offset) continue;                                   // before the edit
            if (s >= end) { spans[i] = s + delta; continue; }             // after the edit
            if (s <= offset && e >= end) { spans[i + 1] += delta; }       // edit inside the span
            else if (s < offset) { spans[i + 1] = offset - s; }           // edit clips the tail
            else { spans[i] = offset + inserted; spans[i + 1] = e - end; } // edit clips the head
            if (spans[i + 1] <= 0) spans[i + 2] = -1;                     // swallowed entirely
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var spans = _spans;
        int count = spans.Length / 3;
        if (count == 0) return;

        int lineStart = line.Offset, lineEnd = line.EndOffset;

        // Binary search for the first span that ends after this line starts.
        int lo = 0, hi = count;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (spans[mid * 3] + spans[mid * 3 + 1] <= lineStart) lo = mid + 1; else hi = mid;
        }

        var brushes = ThemeManager.SyntaxBrushes;
        for (int i = lo; i < count; i++)
        {
            int s = spans[i * 3], e = s + spans[i * 3 + 1], kind = spans[i * 3 + 2];
            if (s >= lineEnd) break;
            if (kind < 0 || kind >= brushes.Length) continue;

            var brush = brushes[kind];
            ChangeLinePart(Math.Max(s, lineStart), Math.Min(e, lineEnd), el => el.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
