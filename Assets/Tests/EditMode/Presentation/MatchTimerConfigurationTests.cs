using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Waribasi.MatchFlow;

namespace Waribasi.Presentation.Tests
{
    public sealed class MatchTimerConfigurationTests
    {
        private static Type RuntimeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Assembly-CSharp").GetType(name, true);
        }

        [TestCase(false, 60, 60)]
        [TestCase(false, 30, 30)]
        [TestCase(false, 0, 0)]
        [TestCase(true, 30, 60)]
        [TestCase(true, 0, 60)]
        public void NetworkSettingsUseOneMinuteUnlessRoomTimeIsExplicit(bool random, int requested, int expected)
        {
            var type = RuntimeType("Waribasi.Networking.MatchNetworkController");
            var configure = type.GetMethod("ConfigureNextMatch");
            var firstType = configure.GetParameters()[1].ParameterType;
            try
            {
                Assert.That(configure.GetParameters()[2].DefaultValue, Is.EqualTo(60));
                configure.Invoke(null, new[] { (object)random, Enum.ToObject(firstType, 2), requested });
                Assert.That(type.GetField("nextTurnSeconds", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), Is.EqualTo(expected));
                Assert.That(Convert.ToInt32(type.GetField("nextFirst", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)), Is.EqualTo(random ? 0 : 2));
            }
            finally
            {
                configure.Invoke(null, new[] { (object)false, Enum.ToObject(firstType, 0), 60 });
            }
        }

        [Test]
        public void SetupStartsWithOneMinuteAndLabelsRandomSettingsAccordingly()
        {
            var owner = new GameObject("TimerSetup");
            owner.SetActive(false);
            try
            {
                var type = RuntimeType("Waribasi.Presentation.MatchSetupPresenter");
                var presenter = owner.AddComponent(type);
                var setup = (MatchSetup)type.GetProperty("SelectedOnlineSetup").GetValue(presenter);
                Assert.That(setup.TurnSeconds, Is.EqualTo(60));
                string text = (string)type.GetMethod("SettingsLabel").Invoke(null, new object[] { new MatchSetup(MatchMode.Random) });
                Assert.That(text, Does.Contain("1分"));
                Assert.That(text, Does.Not.Contain("30秒"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void GuestRejectsOldRandomTimeAndRetainsSettingsOnRematch()
        {
            var owner = new GameObject("TimerGuestSettings");
            owner.SetActive(false);
            try
            {
                var type = RuntimeType("Waribasi.Networking.OnlineSessionController");
                var controller = owner.AddComponent(type);
                var messageType = type.GetNestedType("ControlMessage", BindingFlags.NonPublic);
                var receive = type.GetMethod("ReceiveSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                var setupField = type.GetField("remoteSetup", BindingFlags.Instance | BindingFlags.NonPublic);
                string session = Guid.NewGuid().ToString("N");
                object Message(int seconds, int round, long serial)
                {
                    var message = Activator.CreateInstance(messageType, true);
                    void Set(string field, object value)
                    {
                        messageType.GetField(field).SetValue(message, value);
                    }
                    Set("session", session);
                    Set("mode", (int)MatchMode.Random);
                    Set("seconds", seconds);
                    Set("round", round);
                    Set("serial", serial);
                    return message;
                }
                receive.Invoke(controller, new[] { Message(30, 1, 1) });
                Assert.That(setupField.GetValue(controller), Is.Null);
                receive.Invoke(controller, new[] { Message(60, 1, 2) });
                Assert.That(((MatchSetup)setupField.GetValue(controller)).TurnSeconds, Is.EqualTo(60));
                receive.Invoke(controller, new[] { Message(60, 2, 3) });
                var rematch = (MatchSetup)setupField.GetValue(controller);
                Assert.That(rematch.TurnSeconds, Is.EqualTo(60));
                receive.Invoke(controller, new[] { Message(30, 2, 4) });
                Assert.That(setupField.GetValue(controller), Is.SameAs(rematch));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}
