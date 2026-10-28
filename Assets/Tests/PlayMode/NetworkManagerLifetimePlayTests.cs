using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Waribasi.Presentation.PlayTests
{
    public sealed class NetworkManagerLifetimePlayTests
    {
        private readonly List<GameObject> owners = new List<GameObject>();
        private Type managerType;
        private Type lifetimeType;

        private Component Singleton => (Component)managerType.GetProperty("Singleton").GetValue(null);

        private void Initialize()
        {
            lifetimeType.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }

        private Component CreateManager()
        {
            var owner = new GameObject("NetworkManagerLifetimeTest");
            owners.Add(owner);
            return owner.AddComponent(managerType);
        }

        [SetUp]
        public void SetUp()
        {
            managerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Unity.Netcode.NetworkManager"))
                .Single(type => type != null);
            lifetimeType = AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == "Assembly-CSharp")
                .GetType("Waribasi.Networking.NetworkManagerLifetime", true);
            Assert.That(Singleton == null, Is.True, "Run in the Test Runner's isolated Play Mode scene.");
            Initialize();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var owner in owners)
            {
                if (owner != null)
                {
                    UnityEngine.Object.Destroy(owner);
                }
            }
            owners.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator DuplicateIsDisabledBeforeEnableAndDestroyedWithoutReplacingSingleton()
        {
            var original = CreateManager();
            Assert.That(Singleton, Is.SameAs(original));
            var duplicate = CreateManager();
            Assert.That(duplicate.gameObject.activeSelf, Is.False);
            Assert.That(Singleton, Is.SameAs(original));
            yield return null;
            Assert.That(duplicate == null, Is.True);
            Assert.That(original != null && original.gameObject.activeSelf, Is.True);
            Assert.That(Singleton, Is.SameAs(original));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DestroyingOriginalAllowsANewManager()
        {
            var original = CreateManager();
            UnityEngine.Object.Destroy(original.gameObject);
            yield return null;
            Assert.That(Singleton == null, Is.True);
            var replacement = CreateManager();
            yield return null;
            Assert.That(replacement != null && replacement.gameObject.activeSelf, Is.True);
            Assert.That(Singleton, Is.SameAs(replacement));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ReinitializingAcrossRunsKeepsOneSubscriptionAndOneManager()
        {
            Initialize();
            Initialize();
            var instantiated = (Delegate)managerType.GetField("OnInstantiated",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.That(instantiated.GetInvocationList().Count(handler => handler.Method.DeclaringType == lifetimeType), Is.EqualTo(1));
            var original = CreateManager();
            CreateManager();
            CreateManager();
            yield return null;
            Assert.That(owners.Count(owner => owner != null), Is.EqualTo(1));
            Assert.That(Singleton, Is.SameAs(original));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
