// No Handwired source. Handwired has no translator. The error of a refused construct in a plain
// method, with the message form of the plan, section "Error message rules":
// "<file>:<line>: <construct> is not supported in a [Handwired] method: <reason>", and for an
// inlined method, one " (called from <file>:<line>)" for each call site, innermost first.

namespace Sharpwired.IL;

/// <summary>
/// A construct of a plain method that the translator refuses. The method is the bad argument, so the
/// exception derives from <see cref="ArgumentException"/>. The message has no parameter name.
/// </summary>
public sealed class TranslationException : ArgumentException
{
    private readonly SourceLocation location;
    private readonly IReadOnlyList<SourceLocation> callSites;

    internal TranslationException(SourceLocation location, int ilOffset, string construct, string reason,
        IReadOnlyList<SourceLocation>? callSites = null)
        : base(FormatMessage(location, construct, reason, callSites))
    {
        this.location = location;
        this.callSites = callSites ?? [];
        SourceFile = location.File;
        Line = location.Line;
        IlOffset = ilOffset;
        Construct = construct;
        Reason = reason;
    }

    /// <summary>The document path in the PDB, or null when the method has no PDB.</summary>
    public string? SourceFile { get; }

    /// <summary>The start line of the last visible sequence point at or before the offset, or null.</summary>
    public int? Line { get; }

    /// <summary>The IL offset of the refused instruction in the method that holds it.</summary>
    public int IlOffset { get; }

    /// <summary>The name of the source construct, for example "the IL instruction box".</summary>
    public string Construct { get; }

    /// <summary>One sentence that states the rule.</summary>
    public string Reason { get; }

    /// <summary>
    /// The same refusal with one more call site after the current ones. The inliner adds the call
    /// site of each inlined method, so the call sites come innermost first (plan, "Error message
    /// rules", rule 4).
    /// </summary>
    internal TranslationException CalledFrom(SourceLocation site) => new(location, IlOffset, Construct, Reason, [.. callSites, site]);

    private static string FormatMessage(SourceLocation location, string construct, string reason, IReadOnlyList<SourceLocation>? callSites)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(construct);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var message = $"{location}: {construct} is not supported in a [Handwired] method: {reason}";
        foreach (var site in callSites ?? [])
        {
            message += $" (called from {site})";
        }

        return message;
    }
}
