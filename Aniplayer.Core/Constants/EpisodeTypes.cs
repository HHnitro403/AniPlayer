using System.Text.RegularExpressions;

namespace Aniplayer.Core.Constants;

public static class EpisodeTypes
{
    public const string Episode = "EPISODE";
    public const string Special = "SPECIAL";
    public const string Ova     = "OVA";
    public const string Oad     = "OAD";
    public const string Ncop    = "NCOP";
    public const string Nced    = "NCED";

    private static readonly HashSet<string> KnownSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        Special, Ova, Oad, Ncop, Nced, "Specials", "OVAs", "OADs"
    };

    // Patterns that appear at the end of folder names like "Show S1 - OVA"
    private static readonly (Regex pattern, string type)[] SuffixPatterns =
    {
        (new Regex(@"\b(?:specials?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), Special),
        (new Regex(@"\b(?:OVAs?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), Ova),
        (new Regex(@"\b(?:OADs?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), Oad),
        (new Regex(@"\b(?:NCOP)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), Ncop),
        (new Regex(@"\b(?:NCED)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled), Nced),
    };

    /// <summary>
    /// Detect episode type from folder name. Handles both exact matches ("OVA")
    /// and suffixed names ("High School DxD S1 - OVA").
    /// </summary>
    public static string FromFolderName(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return Episode;

        // Exact match first (fast path)
        if (KnownSubfolders.Contains(folderName))
        {
            // Map to canonical type
            if (folderName.StartsWith("Special", StringComparison.OrdinalIgnoreCase))
                return Special;
            if (folderName.StartsWith("OVA", StringComparison.OrdinalIgnoreCase))
                return Ova;
            if (folderName.StartsWith("OAD", StringComparison.OrdinalIgnoreCase))
                return Oad;
            if (folderName.Equals(Ncop, StringComparison.OrdinalIgnoreCase))
                return Ncop;
            if (folderName.Equals(Nced, StringComparison.OrdinalIgnoreCase))
                return Nced;
        }

        // Suffix match: "Show S1 - OVA", "Show - Specials", etc.
        foreach (var (pattern, type) in SuffixPatterns)
        {
            if (pattern.IsMatch(folderName))
                return type;
        }

        return Episode;
    }

    /// <summary>
    /// Returns true if the folder name is an exact known subfolder like "OVA", "Specials", "NCED",
    /// OR if it ends with a known type suffix like "Show S1 - OVA".
    /// </summary>
    public static bool IsKnownSubfolder(string folderName)
    {
        return FromFolderName(folderName) != Episode;
    }
}
