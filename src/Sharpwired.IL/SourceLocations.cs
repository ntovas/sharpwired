// No Handwired source. Handwired has no translator. The PDB reader of the plan, section
// "Decisions", question 3: PEReader on the assembly file, TryOpenAssociatedPortablePdb (a separate
// or an embedded portable PDB), and the sequence points of one method. The location of an IL offset
// is the last sequence point at or before the offset that is not hidden.

using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Sharpwired.IL;

/// <summary>
/// The source location of an IL offset. File and Line are null when the method has no PDB or no
/// visible sequence point before the offset. Fallback is the form "Type.Method+IL_offset".
/// </summary>
internal readonly record struct SourceLocation(string? File, int? Line, string Fallback)
{
    public override string ToString() => File is null || Line is null
        ? Fallback
        : $"{File}:{Line.Value.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>One sequence point of a method. A hidden point has no document and no line.</summary>
internal readonly record struct SequencePointInfo(int Offset, string? Document, int StartLine, bool IsHidden);

/// <summary>The sequence points of one method, read one time from its PDB.</summary>
internal sealed class SourceLocations
{
    private readonly string fallbackName;

    /// <summary>Creates the locations from sequence points in IL offset order.</summary>
    /// <param name="fallbackName">The name "Type.Method" of the form with no PDB.</param>
    /// <param name="points">The sequence points. An empty list means that the method has no PDB.</param>
    public SourceLocations(string fallbackName, IReadOnlyList<SequencePointInfo> points)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackName);
        ArgumentNullException.ThrowIfNull(points);
        this.fallbackName = fallbackName;
        Points = [.. points.OrderBy(p => p.Offset)];
    }

    /// <summary>The sequence points in IL offset order.</summary>
    public ImmutableArray<SequencePointInfo> Points { get; }

    /// <summary>True when the method has at least one visible sequence point.</summary>
    public bool HasPdb => Points.Any(p => !p.IsHidden);

    /// <summary>Reads the sequence points of the method from the portable PDB of its assembly.</summary>
    public static SourceLocations Read(MethodBase method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var name = $"{method.DeclaringType?.Name ?? "<module>"}.{method.Name}";
        return new SourceLocations(name, ReadPoints(method));
    }

    /// <summary>The location of an IL offset: the last visible sequence point at or before it.</summary>
    public SourceLocation Locate(int ilOffset)
    {
        var fallback = $"{fallbackName}+IL_{ilOffset.ToString("x4", CultureInfo.InvariantCulture)}";
        SequencePointInfo? found = null;
        foreach (var point in Points)
        {
            if (point.Offset > ilOffset)
            {
                break;
            }

            if (!point.IsHidden)
            {
                found = point;
            }
        }

        return found is { } visible
            ? new SourceLocation(visible.Document, visible.StartLine, fallback)
            : new SourceLocation(null, null, fallback);
    }

    private static List<SequencePointInfo> ReadPoints(MethodBase method)
    {
        var points = new List<SequencePointInfo>();
        var path = method.Module.Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return points;
        }

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.TryOpenAssociatedPortablePdb(path, OpenPdb, out var provider, out _) || provider is null)
        {
            return points;
        }

        using (provider)
        {
            var reader = provider.GetMetadataReader();
            var handle = MetadataTokens.MethodDebugInformationHandle(method.MetadataToken);
            if (MetadataTokens.GetRowNumber(handle) > reader.MethodDebugInformation.Count)
            {
                return points;
            }

            foreach (var point in reader.GetMethodDebugInformation(handle).GetSequencePoints())
            {
                points.Add(point.IsHidden
                    ? new SequencePointInfo(point.Offset, null, 0, true)
                    : new SequencePointInfo(point.Offset, reader.GetString(reader.GetDocument(point.Document).Name), point.StartLine, false));
            }
        }

        return points;
    }

    private static FileStream? OpenPdb(string pdbPath) => File.Exists(pdbPath) ? File.OpenRead(pdbPath) : null;
}
