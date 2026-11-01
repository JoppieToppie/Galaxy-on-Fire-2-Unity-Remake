// NetModsTests.cs
// Feature: CLAUDE.md "Mods" > Multiplayer (NetMods): the session's mods travel as "id@version#hash;..." (NetState), a
// joining game lists what it has installed as "id#hash;..." (its connection data), and the server refuses a game lacking
// one of the session's mods or having other files of it (ModInfo.Hash; on or off for single player doesn't matter),
// naming them ("name version"); a session without mods refuses nobody; the session's mods go when it ends.
// Expected values: hand-written lists in the documented form.

using System.Linq;
using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NetModsTests : SandboxTest
    {
        [TearDown]
        public void EndSession() => NetMods.End();

        [Test]
        public void ParsingTheLists()
        {
            var l = NetMods.Parse("plasma@1.2#abc;frontier#def;;bare");
            Assert.AreEqual(3, l.Count);
            Assert.AreEqual("plasma", l[0].id); Assert.AreEqual("1.2", l[0].version); Assert.AreEqual("abc", l[0].hash);
            Assert.AreEqual("frontier", l[1].id); Assert.AreEqual("", l[1].version); Assert.AreEqual("def", l[1].hash);
            Assert.AreEqual("bare", l[2].id); Assert.AreEqual("", l[2].version); Assert.AreEqual("", l[2].hash);
            Assert.IsEmpty(NetMods.Parse(""));
            Assert.IsEmpty(NetMods.Parse(null));
        }

        [Test]
        public void AVersionMayHoldAnAt()
        {
            var e = NetMods.Parse("mod@1.0@beta#h1")[0];
            Assert.AreEqual("mod", e.id);
            Assert.AreEqual("1.0@beta", e.version);
            Assert.AreEqual("h1", e.hash);
        }

        [Test]
        public void MissingModsByIdAndHash()
        {
            const string session = "plasma@1.2#abc;frontier@2.0#def";
            Assert.IsEmpty(NetMods.Missing(session, "plasma#abc;frontier#def"));
            Assert.IsEmpty(NetMods.Missing(session, "PLASMA#ABC;frontier#def;other#zzz"), "any case; extra mods don't matter");
            CollectionAssert.AreEqual(new[] { "frontier 2.0" }, NetMods.Missing(session, "plasma#abc"));
            CollectionAssert.AreEqual(new[] { "plasma 1.2" }, NetMods.Missing(session, "plasma#xyz;frontier#def"), "other files of it");
            Assert.AreEqual(2, NetMods.Missing(session, "").Count);
            Assert.IsEmpty(NetMods.Missing("", "anything#1"));
        }

        [Test]
        public void ASessionWithoutModsRefusesNobody()
        {
            NetMods.ApplyFromServer("");
            Assert.AreEqual("", NetMods.SessionList);
            Assert.IsNull(NetMods.Refusal(""));
            Assert.IsNull(NetMods.Refusal("whatever#1"));
        }

        [Test]
        public void AModdedSessionRefusesAGameWithoutItsMods()
        {
            NetMods.ApplyFromServer("plasma@1.2#abc;frontier@2.0#def");
            Assert.IsTrue(ModManager.InSession);
            Assert.AreEqual("plasma@1.2#abc;frontier@2.0#def", NetMods.SessionList);
            Assert.IsNull(NetMods.Refusal("plasma#abc;frontier#def"));
            string why = NetMods.Refusal("plasma#abc");
            Assert.IsNotNull(why);
            StringAssert.Contains("frontier 2.0", why, "names the mod it lacks");
            StringAssert.DoesNotContain("plasma", why);
        }

        [Test]
        public void TheSessionsModsGoWhenItEnds()
        {
            NetMods.ApplyFromServer("plasma@1.2#abc");
            NetMods.End();
            Assert.IsFalse(ModManager.InSession);
            Assert.AreEqual("", NetMods.SessionList);
            Assert.IsNull(NetMods.Refusal("nothing#0"));
        }

        [Test]
        public void ANewClientStartsWithoutMods()
        {
            NetMods.BeginClient();
            Assert.IsTrue(ModManager.InSession);
            Assert.AreEqual("", NetMods.SessionList);
            Assert.AreEqual(0, ModManager.UserMods.Count);
        }

        [Test]
        public void ASandboxHasNothingInstalled()
        {
            Assert.AreEqual("", NetMods.InstalledList);
            CollectionAssert.AreEqual(new[] { "plasma 1.2" }, NetMods.MissingForListing("plasma@1.2#abc"));
        }

        [Test]
        public void TheListFitsTheConnectionData()
        {
            Assert.LessOrEqual(NetMods.InstalledList.Length, NetMods.MaxListLength);
        }
    }
}
