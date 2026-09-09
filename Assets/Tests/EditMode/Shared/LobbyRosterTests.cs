using System.Collections.Generic;
using System.Linq;
using HypeSwarm.Shared.Net;
using NUnit.Framework;

namespace HypeSwarm.Shared.Tests
{
    /// <summary>
    /// Join and leave over a sixty-minute session (spec §3). Everything here is a lifecycle rule —
    /// the category the testing policy calls out as invisible until it isn't.
    /// </summary>
    [TestFixture]
    public sealed class LobbyRosterTests
    {
        static LobbyRoster Roster(int capacity = LobbyRoster.MaxPlayers) => new LobbyRoster(capacity);

        static LobbySlot Add(LobbyRoster roster, int connectionId, string name = "Player")
        {
            Assert.That(roster.TryAdd(connectionId, name, out var slot), Is.True);
            return slot;
        }

        [Test]
        public void ANewRoster_IsEmpty()
        {
            var roster = Roster();

            Assert.That(roster.Count, Is.Zero);
            Assert.That(roster.IsFull, Is.False);
            Assert.That(roster.Capacity, Is.EqualTo(LobbyRoster.MaxPlayers));
        }

        [Test]
        public void TheDesignedPartySize_IsFive()
        {
            Assert.That(LobbyRoster.MaxPlayers, Is.EqualTo(5), "spec §1: one to five players");
        }

        [Test]
        public void MembersJoin_InOrder_WithAscendingSlots()
        {
            var roster = Roster();

            Assert.That(Add(roster, 0).Index, Is.Zero);
            Assert.That(Add(roster, 1).Index, Is.EqualTo(1));
            Assert.That(Add(roster, 2).Index, Is.EqualTo(2));
            Assert.That(roster.Count, Is.EqualTo(3));
        }

        [Test]
        public void ASixthPlayer_IsRefusedRatherThanQueued()
        {
            var roster = Roster();

            for (var i = 0; i < LobbyRoster.MaxPlayers; i++)
            {
                Add(roster, i);
            }

            Assert.That(roster.IsFull, Is.True);
            Assert.That(roster.TryAdd(99, "Sixth", out _), Is.False);
            Assert.That(roster.Count, Is.EqualTo(LobbyRoster.MaxPlayers));
        }

        /// <summary>
        /// Mirror gives a reconnecting player a fresh connection id, but a duplicate can still
        /// arrive from a repeated join message. Admitting it twice would hand one socket two slots
        /// and leak one of them when it disconnects — a lobby that reports four players with three
        /// people in it.
        /// </summary>
        [Test]
        public void TheSameConnection_CannotJoinTwice()
        {
            var roster = Roster();

            Add(roster, 7);

            Assert.That(roster.TryAdd(7, "Again", out _), Is.False);
            Assert.That(roster.Count, Is.EqualTo(1));
        }

        [Test]
        public void LeavingFreesTheSpace()
        {
            var roster = Roster(capacity: 2);

            Add(roster, 0);
            Add(roster, 1);

            Assert.That(roster.TryRemove(0, out _), Is.True);
            Assert.That(roster.IsFull, Is.False);
            Assert.That(roster.TryAdd(2, "Late", out _), Is.True);
        }

        [Test]
        public void RemovingSomeoneWhoIsNotInTheLobby_ChangesNothing()
        {
            var roster = Roster();

            Add(roster, 0);

            Assert.That(roster.TryRemove(41, out _), Is.False);
            Assert.That(roster.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// The property the slot index exists for. Colours, HUD order, and spawn points key off it,
        /// so the player who was third must still be third after the second one quits.
        /// </summary>
        [Test]
        public void SlotsAreStable_WhenSomeoneElseLeaves()
        {
            var roster = Roster();

            Add(roster, 0);
            Add(roster, 1);
            var third = Add(roster, 2);

            roster.TryRemove(1, out _);

            Assert.That(roster.TryGet(2, out var stillThere), Is.True);
            Assert.That(stillThere.Index, Is.EqualTo(third.Index));
        }

        /// <summary>
        /// And the other half of it: a freed slot is reused. Counting upward forever would hand out
        /// index 7 in a five-player game, and every array sized by capacity would be wrong.
        /// </summary>
        [Test]
        public void AFreedSlot_IsGivenToTheNextPlayer()
        {
            var roster = Roster();

            Add(roster, 0);
            Add(roster, 1);
            Add(roster, 2);

            roster.TryRemove(1, out var left);

            Assert.That(Add(roster, 3).Index, Is.EqualTo(left.Index));
        }

        [Test]
        public void EveryOccupiedSlot_IsWithinCapacityAndUnique()
        {
            var roster = Roster();

            for (var i = 0; i < LobbyRoster.MaxPlayers; i++)
            {
                Add(roster, i);
            }

            roster.TryRemove(2, out _);
            roster.TryRemove(0, out _);
            Add(roster, 10);
            Add(roster, 11);

            var indices = roster.Members.Select(member => member.Index).ToList();

            Assert.That(indices, Is.Unique);
            Assert.That(indices, Is.All.InRange(0, LobbyRoster.MaxPlayers - 1));
        }

        // --- Events ------------------------------------------------------------------------

        [Test]
        public void JoiningAndLeaving_EachRaiseExactlyOnce()
        {
            var roster = Roster();
            var joined = new List<LobbySlot>();
            var left = new List<LobbySlot>();

            roster.Joined += joined.Add;
            roster.Left += left.Add;

            Add(roster, 4, "Hex");
            roster.TryRemove(4, out _);
            roster.TryRemove(4, out _);

            Assert.That(joined, Has.Count.EqualTo(1));
            Assert.That(left, Has.Count.EqualTo(1));
            Assert.That(left[0].DisplayName, Is.EqualTo("Hex"));
        }

        [Test]
        public void ARefusedJoin_RaisesNothing()
        {
            var roster = Roster(capacity: 1);
            var joined = 0;

            Add(roster, 0);
            roster.Joined += _ => joined++;

            Assert.That(roster.TryAdd(1, "Sixth", out _), Is.False);
            Assert.That(joined, Is.Zero);
        }

        /// <summary>
        /// Clearing is for shutting a server down, when there is nobody left to tell.
        /// </summary>
        [Test]
        public void Clearing_EmptiesTheRosterWithoutRaisingLeft()
        {
            var roster = Roster();
            var left = 0;

            Add(roster, 0);
            Add(roster, 1);
            roster.Left += _ => left++;

            roster.Clear();

            Assert.That(roster.Count, Is.Zero);
            Assert.That(left, Is.Zero);
            Assert.That(Add(roster, 0).Index, Is.Zero, "capacity must come back with it");
        }
    }
}
