// Handwired source: src/json-search/program.ts (writeDocumentCells). The cells of one document, as both programs
// read them: the first 512 code points, -1 after the text, and the length of the text in code points. That is
// 513 FP32 values, 2,052 bytes. A surrogate pair is one code point, and a lone surrogate is its own code point.
// Each value is a whole number in -1..1,114,111 or a length in 0..1,048,576, so each cell holds its
// declared range, and the runners do not check the document cells again.

namespace JsonSearch;

/// <summary>The document cells of the highlighter and the search.</summary>
public static class DocumentCells
{
    /// <summary>The number of code points that the programs read.</summary>
    public const int Capacity = Kinds.Capacity;

    /// <summary>The cells of one document: 512 code points and the length.</summary>
    public const int CellsPerDocument = Capacity + 1;

    /// <summary>The largest length that is written. A longer text writes this length, which still reads as longer than 512.</summary>
    public const int LengthLimit = 1_048_576;

    /// <summary>Write the cells of one text.</summary>
    /// <exception cref="ArgumentException">The span does not hold 513 cells.</exception>
    public static void Write(Span<float> cells, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (cells.Length != CellsPerDocument) throw new ArgumentException($"the cells of a document are {CellsPerDocument} values, got {cells.Length}", nameof(cells));
        var length = 0;
        for (var index = 0; index < text.Length; index++)
        {
            int codePoint = text[index];
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                codePoint = char.ConvertToUtf32(text[index], text[index + 1]);
                index++;
            }
            if (length < Capacity) cells[length] = codePoint;
            length++;
        }
        cells[Math.Min(length, Capacity)..Capacity].Fill(-1);
        cells[Capacity] = Math.Min(length, LengthLimit);
    }

    /// <summary>The cells of each text, text after text, 513 cells each.</summary>
    public static float[] WriteAll(IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var cells = new float[texts.Count * CellsPerDocument];
        Parallel.For(0, texts.Count, row => Write(cells.AsSpan(row * CellsPerDocument, CellsPerDocument), texts[row]));
        return cells;
    }
}
