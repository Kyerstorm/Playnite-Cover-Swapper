using System;
using System.Collections.Generic;
using PluginCoverShuffle.Playnite.Integration;

namespace PluginCoverShuffle.Tests.Fakes
{
    /// <summary>
    /// In-memory fake of <see cref="IPlayniteGameService"/> so cover-control
    /// logic can be tested without a live Playnite installation.
    /// </summary>
    public class FakePlayniteGameService : IPlayniteGameService
    {
        private readonly Dictionary<Guid, string> _coverReferences = new Dictionary<Guid, string>();
        private readonly Dictionary<Guid, string> _gameNames = new Dictionary<Guid, string>();
        private readonly Dictionary<Guid, bool> _installedStates = new Dictionary<Guid, bool>();

        public List<(Guid GameId, string CoverReference)> SetCoverReferenceCalls { get; } = new List<(Guid, string)>();

        /// <summary>When set, <see cref="SetCoverReference"/> throws for this game, simulating an unexpected per-game failure.</summary>
        public Guid? GameIdToThrowOn { get; set; }

        /// <summary>When set, <see cref="IsGameInstalled"/> throws for this game, simulating an unexpected per-game lookup failure.</summary>
        public Guid? GameIdToThrowOnIsGameInstalled { get; set; }

        /// <summary>
        /// Games in this set silently ignore <see cref="SetCoverReference"/>,
        /// mirroring the real <c>PlayniteGameService</c> when the game can no
        /// longer be resolved in Playnite's database - no exception, but the
        /// write simply never takes effect. Lets tests exercise a caller's
        /// read-back verification without relying on an exception path.
        /// </summary>
        public HashSet<Guid> GameIdsIgnoringSetCoverReference { get; } = new HashSet<Guid>();

        public void SeedCoverReference(Guid gameId, string coverReference)
        {
            _coverReferences[gameId] = coverReference;
        }

        public string GetCoverReference(Guid gameId)
        {
            return _coverReferences.TryGetValue(gameId, out var reference) ? reference : null;
        }

        public void SetCoverReference(Guid gameId, string coverReference)
        {
            if (GameIdToThrowOn == gameId)
            {
                throw new InvalidOperationException("Simulated failure for test purposes.");
            }

            SetCoverReferenceCalls.Add((gameId, coverReference));

            if (GameIdsIgnoringSetCoverReference.Contains(gameId))
            {
                return;
            }

            _coverReferences[gameId] = coverReference;
        }

        public void SeedGameName(Guid gameId, string name)
        {
            _gameNames[gameId] = name;
        }

        public string GetGameName(Guid gameId)
        {
            return _gameNames.TryGetValue(gameId, out var name) ? name : null;
        }

        /// <summary>Games default to installed unless explicitly marked otherwise, matching most test scenarios.</summary>
        public void SeedInstalled(Guid gameId, bool isInstalled)
        {
            _installedStates[gameId] = isInstalled;
        }

        public bool IsGameInstalled(Guid gameId)
        {
            if (GameIdToThrowOnIsGameInstalled == gameId)
            {
                throw new InvalidOperationException("Simulated failure for test purposes.");
            }

            return !_installedStates.TryGetValue(gameId, out var isInstalled) || isInstalled;
        }
    }
}
