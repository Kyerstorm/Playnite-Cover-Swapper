using System;
using Playnite.SDK;
using PluginCoverShuffle.Domain;
using PluginCoverShuffle.Playnite.Integration;
using PluginCoverShuffle.Tests.Fakes;
using Xunit;

namespace PluginCoverShuffle.Tests.Playnite.Integration
{
    public class CoverShuffleNotificationServiceTests
    {
        private readonly FakeNotificationsApi _notifications = new FakeNotificationsApi();
        private readonly CoverShuffleNotificationService _service;

        public CoverShuffleNotificationServiceTests()
        {
            _service = new CoverShuffleNotificationService(_notifications);
        }

        [Theory]
        [InlineData(NotificationPreference.NotifyOnShuffle)]
        public void NotifyShuffled_WithNotifyOnShuffle_AddsInfoNotification(NotificationPreference preference)
        {
            var gameId = Guid.NewGuid();

            _service.NotifyShuffled(gameId, "Some Game", preference);

            Assert.Single(_notifications.AddedMessages);
            Assert.Equal(NotificationType.Info, _notifications.AddedMessages[0].Type);
        }

        [Theory]
        [InlineData(NotificationPreference.Silent)]
        [InlineData(NotificationPreference.NotifyOnErrorOnly)]
        public void NotifyShuffled_WithNonShuffleNotifyingPreference_AddsNothing(NotificationPreference preference)
        {
            _service.NotifyShuffled(Guid.NewGuid(), "Some Game", preference);

            Assert.Empty(_notifications.AddedMessages);
        }

        [Theory]
        [InlineData(NotificationPreference.NotifyOnShuffle)]
        [InlineData(NotificationPreference.NotifyOnErrorOnly)]
        public void NotifyShuffleFailed_WhenNotSilent_AddsErrorNotification(NotificationPreference preference)
        {
            _service.NotifyShuffleFailed(Guid.NewGuid(), "Some Game", "no covers", preference);

            Assert.Single(_notifications.AddedMessages);
            Assert.Equal(NotificationType.Error, _notifications.AddedMessages[0].Type);
        }

        [Fact]
        public void NotifyShuffleFailed_WhenSilent_AddsNothing()
        {
            _service.NotifyShuffleFailed(Guid.NewGuid(), "Some Game", "no covers", NotificationPreference.Silent);

            Assert.Empty(_notifications.AddedMessages);
        }

        [Fact]
        public void NotifyShuffled_CalledTwiceForSameGame_ReusesTheSameNotificationId()
        {
            var gameId = Guid.NewGuid();

            _service.NotifyShuffled(gameId, "Some Game", NotificationPreference.NotifyOnShuffle);
            _service.NotifyShuffled(gameId, "Some Game", NotificationPreference.NotifyOnShuffle);

            Assert.Equal(2, _notifications.AddedMessages.Count);
            Assert.Equal(_notifications.AddedMessages[0].Id, _notifications.AddedMessages[1].Id);
        }

        [Theory]
        [InlineData(NotificationPreference.NotifyOnShuffle)]
        [InlineData(NotificationPreference.NotifyOnErrorOnly)]
        public void NotifyMissingCovers_WhenNotSilent_AddsErrorNotification(NotificationPreference preference)
        {
            _service.NotifyMissingCovers(3, preference);

            Assert.Single(_notifications.AddedMessages);
            Assert.Equal(NotificationType.Error, _notifications.AddedMessages[0].Type);
            Assert.Contains("3", _notifications.AddedMessages[0].Text);
        }

        [Fact]
        public void NotifyMissingCovers_WhenSilent_AddsNothing()
        {
            _service.NotifyMissingCovers(3, NotificationPreference.Silent);

            Assert.Empty(_notifications.AddedMessages);
        }

        [Fact]
        public void NotifyMissingCovers_CalledTwice_ReusesTheSameNotificationId()
        {
            _service.NotifyMissingCovers(2, NotificationPreference.NotifyOnShuffle);
            _service.NotifyMissingCovers(5, NotificationPreference.NotifyOnShuffle);

            Assert.Equal(2, _notifications.AddedMessages.Count);
            Assert.Equal(_notifications.AddedMessages[0].Id, _notifications.AddedMessages[1].Id);
        }

        [Theory]
        [InlineData(NotificationPreference.NotifyOnShuffle)]
        [InlineData(NotificationPreference.NotifyOnErrorOnly)]
        public void NotifyUninstalledGamesWithCovers_WhenNotSilent_AddsInfoNotification(NotificationPreference preference)
        {
            _service.NotifyUninstalledGamesWithCovers(3, preference);

            Assert.Single(_notifications.AddedMessages);
            // Informational: nothing failed, the games just aren't installed anymore.
            Assert.Equal(NotificationType.Info, _notifications.AddedMessages[0].Type);
            Assert.Contains("3", _notifications.AddedMessages[0].Text);
        }

        [Fact]
        public void NotifyUninstalledGamesWithCovers_WhenSilent_AddsNothing()
        {
            _service.NotifyUninstalledGamesWithCovers(3, NotificationPreference.Silent);

            Assert.Empty(_notifications.AddedMessages);
        }

        [Fact]
        public void NotifyUninstalledGamesWithCovers_CalledTwice_ReusesTheSameNotificationId()
        {
            _service.NotifyUninstalledGamesWithCovers(2, NotificationPreference.NotifyOnShuffle);
            _service.NotifyUninstalledGamesWithCovers(5, NotificationPreference.NotifyOnShuffle);

            Assert.Equal(2, _notifications.AddedMessages.Count);
            Assert.Equal(_notifications.AddedMessages[0].Id, _notifications.AddedMessages[1].Id);
        }
    }
}
