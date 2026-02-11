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

    public static string FromFolderName(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return Episode;

        if (folderName.Equals("Specials", StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals(Special, StringComparison.OrdinalIgnoreCase))
            return Special;

        if (folderName.Equals(Ova, StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals("OVAs", StringComparison.OrdinalIgnoreCase))
            return Ova;

        if (folderName.Equals(Oad, StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals("OADs", StringComparison.OrdinalIgnoreCase))
            return Oad;

        if (folderName.Equals(Ncop, StringComparison.OrdinalIgnoreCase))
            return Ncop;

        if (folderName.Equals(Nced, StringComparison.OrdinalIgnoreCase))
            return Nced;

        return Episode;
    }

    public static bool IsKnownSubfolder(string folderName) =>
        KnownSubfolders.Contains(folderName);
}
