// Handwired source: assets/site.css, assets/tool.css and assets/json-search.css. The file site.css gives the color
// tokens: paper, sheet, ink, rule, copper and its washes, the wire colors. The file tool.css gives the color of each
// display kind. The file json-search.css gives the strengths of the plain and the waiting cells of the map, 0.3 and
// 0.12 of ink. The colors of the window.

using Sharpwired.Raylib;

namespace JsonSearch;

/// <summary>The colors of the JSON search window.</summary>
public static class Palette
{
    /// <summary>--paper, the background of the window.</summary>
    public static readonly Rgba Paper = new(0xf3, 0xee, 0xe4, 255);

    /// <summary>--sheet, the raised surfaces: the field, the map and the document.</summary>
    public static readonly Rgba Sheet = new(0xff, 0xfd, 0xf8, 255);

    /// <summary>--ink, the text.</summary>
    public static readonly Rgba Ink = new(0x1e, 0x1a, 0x16, 255);

    /// <summary>--ink-2, the secondary text.</summary>
    public static readonly Rgba Ink2 = new(0x5f, 0x56, 0x4d, 255);

    /// <summary>--ink-3, the labels and the punctuation.</summary>
    public static readonly Rgba Ink3 = new(0x72, 0x67, 0x5c, 255);

    /// <summary>--rule, the 1 px lines around a surface.</summary>
    public static readonly Rgba Rule = new(0xdc, 0xd2, 0xc4, 255);

    /// <summary>--copper, the accent: the focus, the marks and the cells with a match.</summary>
    public static readonly Rgba Copper = new(0xa6, 0x4d, 0x1d, 255);

    /// <summary>--copper-wash (0.13), the fill under a match.</summary>
    public static readonly Rgba WashLight = new(0xa6, 0x4d, 0x1d, 33);

    /// <summary>--copper-wash-deep (0.25), the fill under the next match, so that two neighbor matches stay apart.</summary>
    public static readonly Rgba WashDeep = new(0xa6, 0x4d, 0x1d, 64);

    /// <summary>The fill of an example pattern (copper at 0.1).</summary>
    public static readonly Rgba ExampleWash = new(0xa6, 0x4d, 0x1d, 26);

    /// <summary>The track of a segment group (--wash, ink at 0.06 on paper).</summary>
    public static readonly Rgba SegmentTrack = new(0xe6, 0xe0, 0xd5, 255);

    /// <summary>The selected segment, white.</summary>
    public static readonly Rgba SegmentSelected = new(0xff, 0xff, 0xff, 255);

    /// <summary>The caret of the pattern field.</summary>
    public static readonly Rgba Caret = new(0x1e, 0x1a, 0x16, 254);

    /// <summary>The red of an error (--wire-red), for the line of a refused pattern.</summary>
    public static readonly Rgba Red = new(0xb3, 0x26, 0x1e, 255);

    /// <summary>The pixel of a document with a match: copper.</summary>
    public static readonly uint CellMatch = DrawPixels.Word(Copper);

    /// <summary>The pixel of a document without a match: ink at 0.3.</summary>
    public static readonly uint CellPlain = DrawPixels.Word(Ink with { A = 77 });

    /// <summary>The pixel of a document that a search in progress has not reached: ink at 0.12.</summary>
    public static readonly uint CellWaiting = DrawPixels.Word(Ink with { A = 31 });

    /// <summary>The cell colors of the map.</summary>
    public static readonly CellColors Cells = new(CellMatch, CellPlain, CellWaiting);
}
