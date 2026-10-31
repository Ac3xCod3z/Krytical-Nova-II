using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// One answer to "is this body this machine's own player?" (plan 52): an entity-netcode body answers from its tag, every other body
    /// keeps NGO's own answer. Before, a DOTS body (never NGO-spawned) was "mine" on every machine: another player's run kicked my view.
    /// </summary>
    public class BodyIsMineTests
    {
        static GameObject Body(NetRoleKind kind, bool local, out Component part)
        {
            var go = new GameObject("body");
            var tag = go.AddComponent<BodyRoleTag>();
            if (kind != NetRoleKind.Offline) tag.Assign(kind, PlayerKey.None, 1, local);
            var child = new GameObject("Visuals");
            child.transform.SetParent(go.transform);
            part = child.transform;   // a part of the body, as a motor on the Visuals child asks
            return go;
        }

        [Test]
        public void TheLocalClientView_IsMine_WhateverNgoSays()
        {
            var go = Body(NetRoleKind.ClientView, true, out Component part);
            try
            {
                Assert.IsTrue(BodyRoleTag.IsMine(part, true));
                Assert.IsTrue(BodyRoleTag.IsMine(part, false));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AnotherPlayersClientView_AndAServerBody_AreNeverMine()
        {
            var remote = Body(NetRoleKind.ClientView, false, out Component remotePart);
            var server = Body(NetRoleKind.ServerBody, false, out Component serverPart);
            try
            {
                Assert.IsFalse(BodyRoleTag.IsMine(remotePart, true), "never NGO-spawned: NGO's !IsSpawned said yes, the tag says no");
                Assert.IsFalse(BodyRoleTag.IsMine(serverPart, true), "a server body is nobody's on this machine");
            }
            finally { Object.DestroyImmediate(remote); Object.DestroyImmediate(server); }
        }

        [Test]
        public void ANgoOrOfflineBody_KeepsNgosOwnAnswer()
        {
            var untagged = new GameObject("ngo body");
            var unfilled = Body(NetRoleKind.Offline, false, out Component unfilledPart);
            try
            {
                Assert.IsTrue(BodyRoleTag.IsMine(untagged.transform, true));
                Assert.IsFalse(BodyRoleTag.IsMine(untagged.transform, false));
                Assert.IsTrue(BodyRoleTag.IsMine(unfilledPart, true), "a tag never filled in is the offline avatar: NGO's answer");
                Assert.IsFalse(BodyRoleTag.IsMine(unfilledPart, false));
            }
            finally { Object.DestroyImmediate(untagged); Object.DestroyImmediate(unfilled); }
        }
    }
}
