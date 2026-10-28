using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchMovementTests
    {
        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(assembly =>
                assembly.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        private static void Set(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        [TestCase(PlayerId.PlayerA, HandSide.Left, 0, 0)]
        [TestCase(PlayerId.PlayerA, HandSide.Right, 3, 0)]
        [TestCase(PlayerId.PlayerB, HandSide.Left, 3, 10)]
        [TestCase(PlayerId.PlayerB, HandSide.Right, 0, 10)]
        public void HandsUseTheSameStableLayoutAndNormalizedRotationForEitherSeat(
            PlayerId player, HandSide side, float x, float z)
        {
            var hand = new GameObject("HandLayout");
            var place = RuntimeType("Waribasi.Presentation.MatchHandLayout").GetMethod("Place");
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    place.Invoke(null, new object[] { hand.transform, player, side });
                    Assert.That(hand.transform.position, Is.EqualTo(new Vector3(x, 0, z)));
                    var rotation = hand.transform.rotation;
                    Assert.That(Quaternion.Dot(rotation, rotation), Is.EqualTo(1).Within(0.00001));
                    Assert.That(Vector3.Dot(hand.transform.forward,
                        player == PlayerId.PlayerA ? Vector3.forward : Vector3.back), Is.EqualTo(1).Within(0.00001));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hand);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AcceptedAttackCompletesItsMovementEvenWhenItWins(bool winning)
        {
            var owner = new GameObject("MatchMovement");
            var hand = new GameObject("MovingHand");
            var network = owner.AddComponent(RuntimeType("Waribasi.Networking.MatchNetworkController"));
            var networkHand = hand.AddComponent(RuntimeType("Unity.Netcode.NetworkObject"));
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(
                new PlayerHands(1, 1), new PlayerHands(winning ? 0 : 1, 4), PlayerId.PlayerA), 30, 100);
            var request = new MatchRequest(authority.MatchId, 1, 0, MatchOperation.Attack,
                (int)HandSide.Left, (int)HandSide.Right);
            var update = network.GetType().GetMethod("UpdateMovement", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                Assert.That(authority.Submit(PlayerId.PlayerA, request, 100).Accepted, Is.True);
                Assert.That(authority.State.MatchPhase, Is.EqualTo(winning ? MatchPhase.Finished : MatchPhase.Active));
                var confirmed = authority.State;
                Set(network, "authority", authority);
                Set(network, "movingHand", networkHand);
                Set(network, "moveOrigin", Vector3.zero);
                Set(network, "moveContact", new Vector3(4, 0, 0));
                Set(network, "movementStarted", 100d);
                foreach (double now in new[] { 100.25, 100.5, 100.75, 101.0 })
                {
                    update.Invoke(network, new object[] { now });
                    float expected = now == 100.5 ? 4 : now == 101 ? 0 : 2;
                    Assert.That(hand.transform.position.x, Is.EqualTo(expected).Within(0.001), now.ToString());
                    Assert.That(authority.State, Is.SameAs(confirmed), "Presentation must not apply the attack twice.");
                }
                Assert.That(network.GetType().GetField("movingHand", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(network), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(hand);
            }
        }

        [TestCase(MatchEndReason.Surrender)]
        [TestCase(MatchEndReason.Disconnected)]
        public void ExternalEndStopsMovementWithoutChangingItsConfirmedResult(MatchEndReason reason)
        {
            var owner = new GameObject("InterruptedMovement");
            var hand = new GameObject("MovingHand");
            var network = owner.AddComponent(RuntimeType("Waribasi.Networking.MatchNetworkController"));
            var networkHand = hand.AddComponent(RuntimeType("Unity.Netcode.NetworkObject"));
            var authority = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(
                new PlayerHands(1, 1), new PlayerHands(1, 1), PlayerId.PlayerA), 30, 100);
            try
            {
                if (reason == MatchEndReason.Disconnected)
                {
                    authority.Disconnect(PlayerId.PlayerA, 100);
                }
                else
                {
                    authority.Submit(PlayerId.PlayerA,
                        new MatchRequest(authority.MatchId, 1, 0, MatchOperation.Surrender), 100);
                }
                var confirmed = authority.State;
                Set(network, "authority", authority);
                Set(network, "movingHand", networkHand);
                Set(network, "moveOrigin", Vector3.zero);
                Set(network, "moveContact", new Vector3(4, 0, 0));
                Set(network, "movementStarted", 100d);
                hand.transform.position = Vector3.right;
                network.GetType().GetMethod("UpdateMovement", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(network, new object[] { 100.25 });
                Assert.That(hand.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(authority.State, Is.SameAs(confirmed));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(hand);
            }
        }
    }
}
