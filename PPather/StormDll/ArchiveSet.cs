using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace StormDll;

public sealed partial class ArchiveSet
{
    /// <summary>
    /// Blizzard's incremental update archives, in both naming forms the clients
    /// use: `patch[-<locale>][-<n>].MPQ` (vanilla..WotLK) and
    /// `wow-update-<scope>-<build>.MPQ` (Cataclysm on). Their entries are PTCH
    /// deltas, not whole files, so they must be chained onto a base archive
    /// rather than searched alongside it.
    ///
    /// Getting this wrong is silent rather than loud: every one of these names
    /// sorts after the base archives (`common`, `expansion`, `lichking`,
    /// `world`...), so a first-match-wins scan simply never reaches them and
    /// bakes pre-patch geometry. Mists makes the cost obvious - 23 update
    /// archives holding updated terrain for 37% of its root ADTs - but a full
    /// WotLK install has the same exposure through `patch-3.MPQ`.
    ///
    /// The trailing number is the apply order. `patch.MPQ` has none and is the
    /// oldest, hence group 1 being optional.
    /// </summary>
    [GeneratedRegex(@"^(?:wow-update-|patch)[a-z\-]*?(\d*)\.mpq$", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateArchiveRegex();

    private readonly Archive[] archives;
    private readonly ILogger logger;

    public ArchiveSet(ILogger logger, string[] files)
    {
        this.logger = logger;

        // Split before opening anything: patch archives are chained onto the
        // bases, and the build number in the name is their apply order.
        List<string> baseFiles = new(files.Length);
        List<(string file, long build)> patchFiles = [];

        foreach (string file in files)
        {
            Match m = UpdateArchiveRegex().Match(Path.GetFileName(file));
            if (!m.Success)
            {
                baseFiles.Add(file);
                continue;
            }

            // `patch.MPQ` carries no number and applies first.
            _ = long.TryParse(m.Groups[1].ValueSpan, out long build);
            patchFiles.Add((file, build));
        }

        patchFiles.Sort((a, b) => a.build != b.build
            ? a.build.CompareTo(b.build)
            : string.CompareOrdinal(a.file, b.file));

        List<Archive> bases = OpenAll(baseFiles);

        // Patch archives are also opened standalone, but only as a last-resort
        // lookup: an update can introduce a wholly new file that no base lists,
        // and such a file is stored complete rather than as a PTCH delta.
        List<string> patchOnly = new(patchFiles.Count);
        foreach ((string file, long _) in patchFiles)
            patchOnly.Add(file);

        List<Archive> patches = OpenAll(patchOnly);

        if (patchFiles.Count > 0)
            ChainPatches(bases, patchFiles);

        // Bases first (they resolve to patched content), patch archives last -
        // the same relative order the old alphabetical scan produced, since
        // "wow-update-*" sorts after every base archive name.
        bases.AddRange(patches);
        archives = [.. bases];

        if (archives.Length == 0)
            logger.LogError(
                "No MPQ archive could be opened from {Count} file(s). " +
                "The native StormLib may be missing or built for the wrong " +
                "architecture/charset - pathing will not work.", files.Length);
    }

    private List<Archive> OpenAll(List<string> files)
    {
        // Only keep successfully opened archives. A failed open used to leave a
        // null slot that later NRE'd in GetStream/Exists. Now it is skipped and
        // logged, so a bad/mismatched native StormLib surfaces a clear message.
        List<Archive> opened = new(files.Count);

        for (int i = 0; i < files.Count; i++)
        {
            Archive a = new(files[i], out bool open, 0,
                OpenArchive.MPQ_OPEN_NO_LISTFILE |
                OpenArchive.MPQ_OPEN_NO_ATTRIBUTES |
                OpenArchive.MPQ_OPEN_NO_HEADER_SEARCH |
                OpenArchive.MPQ_OPEN_READ_ONLY);

            if (open && a.IsOpen())
            {
                if (a.FileCount == 0)
                {
                    // Opened fine but lists nothing - a stub archive such as
                    // Cataclysm's OldWorld.MPQ. Close it rather than searching
                    // it on every lookup.
                    a.SFileCloseArchive();

                    if (logger.IsEnabled(LogLevel.Trace))
                        logger.LogTrace("Archive[{Index}] empty listfile, skipped {File}", i, files[i]);

                    continue;
                }

                opened.Add(a);

                if (logger.IsEnabled(LogLevel.Trace))
                    logger.LogTrace("Archive[{Index}] open {File}", i, files[i]);
            }
            else
                logger.LogWarning("Archive failed to open: {File}", files[i]);
        }

        return opened;
    }

    /// <summary>
    /// Chains every patch onto each base that it actually touches. Skipping the
    /// bases a patch does not overlap matters: Mists has 23 patches and 14 base
    /// archives, and attaching blindly would open the same multi-hundred-MB
    /// archives hundreds of times over.
    /// </summary>
    private void ChainPatches(List<Archive> bases, List<(string file, long build)> patchFiles)
    {
        HashSet<string> patchNames = new(StringComparer.OrdinalIgnoreCase);

        List<Archive> probes = OpenAll([.. patchFiles.ConvertAll(p => p.file)]);
        foreach (Archive p in probes)
        {
            foreach (string n in p.Files)
                patchNames.Add(n);

            p.SFileCloseArchive();
        }

        foreach (Archive b in bases)
        {
            bool overlaps = false;
            foreach (string n in b.Files)
            {
                if (patchNames.Contains(n))
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
                continue;

            int attached = 0;
            foreach ((string file, long _) in patchFiles)
            {
                if (b.AttachPatch(file))
                    attached++;
                else
                    logger.LogWarning("Failed to attach patch {Patch}", Path.GetFileName(file));
            }

            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace("Chained {Count} patch archive(s) onto a base", attached);
        }

        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("MPQ patch chain: {Patches} update archive(s) over {Bases} base archive(s)",
                patchFiles.Count, bases.Count);
    }

    public MpqFileStream GetStream(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive a = archives[i];
            if (a.HasFile(fileName))
                return a.GetStream(fileName);
        }

        logger.LogWarning("fileName not found '{FileName}'", fileName.ToString());
        throw new FileNotFoundException($"{nameof(fileName)} - {fileName}");
    }

    public bool Exists(ReadOnlySpan<char> fileName)
    {
        for (int i = 0; i < archives.Length; i++)
        {
            Archive a = archives[i];
            if (a.HasFile(fileName))
                return true;
        }
        return false;
    }

    public void Close()
    {
        for (int i = 0; i < archives.Length; i++)
            archives[i].SFileCloseArchive();
    }
}