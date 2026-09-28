using System;

namespace PluginCoverShuffle.Playnite.Integration
{
    /// <summary>
    /// One managed game (has at least one stored cover) that Playnite
    /// currently reports as not installed. Returned by
    /// <see cref="UninstalledGameScanService.Scan"/> for the caller to
    /// display; carries no behavior of its own.
    /// </summary>
    public class UninstalledGameCandidate
    {
        public Guid GameId { get; set; }

        public string GameName { get; set; }

        public int CoverCount { get; set; }
    }
}
