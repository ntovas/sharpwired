// No Handwired source. Handwired has no translator. The tests check the PDB reader of the plan,
// section "Decisions", question 3. The location of an IL offset is the last sequence point at or
// before the offset that is not hidden. C# inputs use a separate portable PDB, F# inputs an
// embedded portable PDB.

using System.Collections.Immutable;
using static Sharpwired.IL.Tests.FrontEndSupport;

namespace Sharpwired.IL.Tests;

public class SourceLocationTests
{
    [Fact]
    public void A_csharp_offset_maps_to_the_line_of_its_statement_in_the_separate_pdb()
    {
        var path = CSharpInputPath();
        Assert.True(File.Exists(Path.ChangeExtension(typeof(SourceLocationTests).Assembly.Location, ".pdb")));
        var method = IlDecoder.Decode(CSharpInput("TwoStatements"));
        var subtract = method.Instructions.Single(i => i.Name == "sub");
        var location = method.Locations.Locate(subtract.Offset);
        Assert.Equal(path, location.File);
        Assert.Equal(MarkerLine(path, "second-statement"), location.Line);
        Assert.Equal($"{path}:{location.Line}", location.ToString());
    }

    [Fact]
    public void An_fsharp_offset_maps_to_the_line_of_its_expression_in_the_embedded_pdb()
    {
        var path = FSharpInputPath();
        var assembly = typeof(FSharpInputs.FrontEnd).Assembly;
        Assert.False(File.Exists(Path.ChangeExtension(assembly.Location, ".pdb")));
        var method = IlDecoder.Decode(FSharpInput("twoStatements"));
        var subtract = method.Instructions.Single(i => i.Name == "sub");
        var location = method.Locations.Locate(subtract.Offset);
        Assert.Equal(path, location.File);
        Assert.Equal(MarkerLine(path, "fs-second-statement"), location.Line);
    }

    [Fact]
    public void An_offset_after_a_hidden_point_maps_to_the_visible_point_before_it()
    {
        var locations = new SourceLocations("Probe.F",
        [
            new SequencePointInfo(0, "a.fs", 10, false),
            new SequencePointInfo(4, null, 0, true),
            new SequencePointInfo(9, "a.fs", 12, false),
        ]);
        Assert.Equal(new SourceLocation("a.fs", 10, "Probe.F+IL_0000"), locations.Locate(0));
        Assert.Equal(new SourceLocation("a.fs", 10, "Probe.F+IL_0004"), locations.Locate(4));
        Assert.Equal(new SourceLocation("a.fs", 10, "Probe.F+IL_0008"), locations.Locate(8));
        Assert.Equal(new SourceLocation("a.fs", 12, "Probe.F+IL_0009"), locations.Locate(9));
        Assert.Equal("a.fs:12", locations.Locate(30).ToString());
    }

    [Fact]
    public void An_offset_before_each_visible_point_has_the_form_with_no_pdb()
    {
        var locations = new SourceLocations("Probe.F", [new SequencePointInfo(0, null, 0, true), new SequencePointInfo(6, "a.fs", 3, false)]);
        Assert.Equal("Probe.F+IL_0005", locations.Locate(5).ToString());
        Assert.Null(locations.Locate(5).File);
        Assert.Null(locations.Locate(5).Line);
    }

    [Fact]
    public void Each_hidden_fsharp_point_maps_to_the_visible_point_before_it()
    {
        // The F# compiler writes hidden sequence points. Each one that follows a visible point gives
        // the line of that visible point.
        var checkedPoints = 0;
        foreach (var name in new[] { "conditional", "twoStatements", "andChain", "constantLoop" })
        {
            var locations = IlDecoder.Decode(FSharpInput(name)).Locations;
            ImmutableArray<SequencePointInfo> points = locations.Points;
            for (var i = 0; i < points.Length; i++)
            {
                if (!points[i].IsHidden)
                {
                    continue;
                }

                var before = points.Take(i).LastOrDefault(p => !p.IsHidden && p.Offset <= points[i].Offset);
                if (before.Document is null)
                {
                    continue;
                }

                var location = locations.Locate(points[i].Offset);
                Assert.Equal(before.StartLine, location.Line);
                Assert.Equal(before.Document, location.File);
                checkedPoints++;
            }
        }

        Assert.True(checkedPoints > 0, "the F# inputs have no hidden point after a visible point");
    }

    [Fact]
    public void A_method_with_no_pdb_gives_the_type_method_and_offset()
    {
        var max = typeof(Math).GetMethod(nameof(Math.Max), [typeof(float), typeof(float)])!;
        var locations = SourceLocations.Read(max);
        Assert.False(locations.HasPdb);
        var location = locations.Locate(0);
        Assert.Equal("Math.Max+IL_0000", location.ToString());
        Assert.Null(location.File);
        Assert.Equal("Math.Max+IL_001a", locations.Locate(26).ToString());
    }

    [Fact]
    public void A_translation_exception_lists_the_call_sites_innermost_first()
    {
        var error = new TranslationException(new SourceLocation("b.cs", 7, "B.G+IL_0003"), 3, "a comparison", "comparison operands need finite declared ranges",
            [new SourceLocation("a.cs", 20, "A.F+IL_0010"), new SourceLocation(null, null, "C.H+IL_0002")]);
        Assert.Equal("b.cs:7: a comparison is not supported in a [Handwired] method: comparison operands need finite declared ranges"
            + " (called from a.cs:20) (called from C.H+IL_0002)", error.Message);
        Assert.Equal("b.cs", error.SourceFile);
        Assert.Equal(7, error.Line);
        Assert.Equal(3, error.IlOffset);
        Assert.Equal("a comparison", error.Construct);
        Assert.Equal("comparison operands need finite declared ranges", error.Reason);
    }
}
