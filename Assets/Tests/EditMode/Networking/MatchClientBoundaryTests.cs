using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Waribasi.MatchFlow;
using Waribasi.Rules;

namespace Waribasi.Networking.Tests
{
    //定義済みAssembly-CSharpの実装を反射で呼び、テストのために製品のアセンブリ境界を変更しない。
    public sealed class MatchClientBoundaryTests
    {
        private GameObject owner;
        private Component view;
        private Type viewType;
        private MatchAuthority match;

        [SetUp]
        public void SetUp()
        {
            viewType = RuntimeType("Waribasi.Networking.MatchClientState");
            owner = new GameObject("MatchClientBoundaryTests");
            view = owner.AddComponent(viewType);
            match = new MatchAuthority(Guid.NewGuid(), RuleEngine.FromPosition(new PlayerHands(1, 1), new PlayerHands(1, 1),
                PlayerId.PlayerA, playerACards: new[] { new CardDefinition(CardKind.Defense) }), 30, 100);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(owner);
        }

        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        private object Invoke(string name, params object[] arguments)
        {
            return viewType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(view, arguments);
        }

        private T Read<T>(string name)
        {
            return (T)viewType.GetProperty(name).GetValue(view);
        }

        private PublicMatchMessage Public()
        {
            return MatchMessages.Public(match, 0, 7, 3, 100);
        }

        [Test]
        public void InputWaitsForMatchingPrivateVersion()
        {
            Assert.That(Invoke("ReceivePublic", Public(), 0UL), Is.True);
            Assert.That(Read<bool>("CanAct"), Is.False);
            Assert.That(Invoke("ReceiveOwn", MatchMessages.OwnHand(match, PlayerId.PlayerA)), Is.True);
            Assert.That(Read<bool>("CanAct"), Is.True);
            var next = Public();
            next.sequence++;
            next.stateVersion++;
            Assert.That(Invoke("ReceivePublic", next, 0UL), Is.True);
            Assert.That(Read<bool>("CanAct"), Is.False);
        }

        [TestCase("duplicate")]
        [TestCase("older")]
        [TestCase("wrong-match")]
        [TestCase("unknown-client")]
        public void UnrelatedOrOutdatedPublicMessageCannotReplaceCurrentState(string kind)
        {
            var original = Public();
            original.sequence = 5;
            Invoke("ReceivePublic", original, 0UL);
            var message = Public();
            message.sequence = kind == "duplicate" ? 5 : kind == "older" ? 4 : 6;
            if (kind == "wrong-match")
            {
                message.matchId = Guid.NewGuid().ToString("N");
            }
            Assert.That(Invoke("ReceivePublic", message, kind == "unknown-client" ? 99UL : 0UL), Is.False);
            Assert.That(Read<PublicMatchMessage>("Public"), Is.SameAs(original));
        }

        [Test]
        public void OtherPlayersPrivateHandIsRejected()
        {
            Invoke("ReceivePublic", Public(), 0UL);
            Assert.That(Invoke("ReceiveOwn", MatchMessages.OwnHand(match, PlayerId.PlayerB)), Is.False);
            Assert.That(Read<bool>("HandIsCurrent"), Is.False);
        }

        [Test]
        public void GateOnlyDeliveryControlsInputWithoutChangingRuleVersion()
        {
            Invoke("ReceivePublic", Public(), 0UL);
            Invoke("ReceiveOwn", MatchMessages.OwnHand(match, PlayerId.PlayerA));
            var closed = Public();
            closed.sequence = 1;
            closed.gate = (int)InputGate.Presenting;
            Invoke("ReceivePublic", closed, 0UL);
            Assert.That(Read<bool>("CanAct"), Is.False);
            var opened = Public();
            opened.sequence = 2;
            Invoke("ReceivePublic", opened, 0UL);
            Assert.That(Read<bool>("CanAct"), Is.True);
        }

        [Test]
        public void ConfirmedResultSurvivesLocalDisconnectAndLaterMessages()
        {
            var ended = Public();
            ended.matchPhase = (int)MatchPhase.Finished;
            ended.winner = 0;
            Invoke("ReceivePublic", ended, 0UL);
            Invoke("InterruptLocally");
            Assert.That(Read<bool>("HasConfirmedResult"), Is.True);
            Assert.That(Read<bool>("CommunicationInterrupted"), Is.False);
            var replacement = Public();
            replacement.sequence = 100;
            replacement.winner = 1;
            Assert.That(Invoke("ReceivePublic", replacement, 0UL), Is.False);
            Assert.That(Read<PublicMatchMessage>("Public").winner, Is.Zero);
        }

        [Test]
        public void PendingRequestPreventsAnotherSubmissionUntilMatchingResponse()
        {
            Invoke("ReceivePublic", Public(), 0UL);
            Invoke("ReceiveOwn", MatchMessages.OwnHand(match, PlayerId.PlayerA));
            Invoke("CreateRequest", MatchOperation.Attack, 0, 0, 0, 0, 0L, false);
            Assert.That(Read<bool>("CanAct"), Is.False);
            Invoke("ReceiveResponse", new RequestResponseMessage { matchId = match.MatchId.ToString("N"), requestId = 999 });
            Assert.That(Read<bool>("HasPendingRequest"), Is.True);
            Invoke("ReceiveResponse", new RequestResponseMessage { matchId = match.MatchId.ToString("N"), requestId = 1 });
            Assert.That(Read<bool>("HasPendingRequest"), Is.False);
        }

        [TestCase("PublicStateRpc")]
        [TestCase("OwnHandRpc")]
        [TestCase("HistoryRpc")]
        [TestCase("ResponseRpc")]
        public void OutboundRpcInvocationIsRestrictedToTheServer(string methodName)
        {
            var method = RuntimeType("Waribasi.Networking.MatchNetworkController").GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            var attribute = method.GetCustomAttributesData().Single(a => a.AttributeType.FullName == "Unity.Netcode.RpcAttribute");
            var permission = attribute.NamedArguments.Single(a => a.MemberName == "InvokePermission").TypedValue;
            Assert.That(Enum.GetName(permission.ArgumentType, permission.Value), Is.EqualTo("Server"));
        }

        [TestCase("HandHantei", "RequestJudgeServerRpc")]
        [TestCase("PlayerHandAction", "HandChangeServerRpc")]
        [TestCase("CardAction", "UseCardServerRpc")]
        [TestCase("CardCreate", "SendCardClientRpc")]
        public void RetiredRpcHasNoCallableMethodOrRegistration(string typeName, string methodName)
        {
            Assert.That(RuntimeType(typeName).GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null);
        }

        [TestCase("Left", HandSide.Left)]
        [TestCase("Right", HandSide.Right)]
        public void LegacySidesAreMappedByNameInsteadOfTheirOppositeNumericValues(string legacy, HandSide expected)
        {
            var value = Enum.Parse(RuntimeType("HandType"), legacy);
            var result = viewType.GetMethod("ToSide", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { value });
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
