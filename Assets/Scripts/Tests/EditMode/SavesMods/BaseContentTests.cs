// What the remake adds to the game (ModManager.BaseContent, Resources/GoF2Base): there with no mod on, numbered by a key so
// it never takes a number from a mod, and no mod to the player.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class BaseContentTests : SavesModsTest
    {
        const string Key = "gof2:khador_slipstream";

        [Test]
        public void TheOriginalTablesStayAsTheyWere()
        {
            var db = Database.Load();
            Assert.AreEqual(GameNames.OriginalItems, ModContent.OriginalItemCount, "a new base item goes in Resources/GoF2Base, never in items.json");
            Assert.AreEqual(GameNames.OriginalItems, db.Items.Count(i => !i.modded));
        }

        [Test]
        public void TheKhadorSlipstreamIsThereWithNoModOn()
        {
            var db = Database.Load();
            var drive = db.Item(ModContent.ItemIndexOf(Key));
            Assert.IsNotNull(drive);
            Assert.AreEqual("Khador Slipstream", GameNames.Item(drive.index));
            Assert.Greater(drive.Attr(CruiseDrive.SpeedAttr), 0, "cruises in an orbit");
            Assert.Greater(drive.Attr(CruiseDrive.InterstellarAttr), 0, "and between the systems");
            Assert.AreEqual(0, drive.occurrence, "in no shop");
            Assert.IsNotNull(ModContent.ItemKey(drive.index));
        }

        [Test]
        public void ItsRecipeIsTheKhadorDrivesGrownWithOneChromoPlasma()
        {
            var db = Database.Load();
            var recipe = db.Item(ModContent.ItemIndexOf(Key)).blueprint;
            foreach (var good in db.Item(85).blueprint)
                Assert.Greater(recipe.Find(r => r.item == good.item)?.amount ?? 0, good.amount, good.name);
            Assert.AreEqual(1, recipe.Find(r => r.item == 210)?.amount);
            Assert.AreEqual(db.Item(85).blueprint.Count + 1, recipe.Count);
        }

        [Test]
        public void KhadorSellsItAtThynomeFromTheSupernovaOn()
        {
            var db = Database.Load();
            int product = ModContent.ItemIndexOf(Key);
            var seller = ModBlueprints.Of(product).seller;
            Assert.AreEqual("Khador", seller.name);
            bool Sold(int station) => AgentGenerator.CreateAgents(db, station).Exists(a => a.offer == AgentOffer.SellBlueprint && a.sellBlueprint == product);
            Session.FreePlay = false;
            Session.CampaignMission = 83;
            Assert.IsFalse(Sold(10), "before the add-on");
            Session.CampaignMission = 84;
            Assert.IsTrue(Sold(10));
            Assert.IsTrue(Story.LoungeUnlocked(10), "a lounge the player can open");
            Assert.IsFalse(Sold(11), "only there");
            Session.FreePlay = true;
            Session.CampaignMission = 20;
            Assert.IsTrue(Sold(10), "at once in free play");
        }

        [Test]
        public void BuyingItFromKhadorTeachesTheBlueprint()
        {
            var db = Database.Load();
            int product = ModContent.ItemIndexOf(Key);
            Session.FreePlay = true;
            Session.Credits = 500000;
            var khador = AgentGenerator.CreateAgents(db, 10).Find(a => a.offer == AgentOffer.SellBlueprint && a.sellBlueprint == product);
            var chat = new LoungeChat(db, khador, 10, null);
            chat.Start();
            StringAssert.StartsWith("Just a word, Mr. Maxwell.", chat.Text);
            StringAssert.Contains("Khador Slipstream", chat.Text);
            Assert.AreEqual(LoungeChat.Outcome.Confirm, chat.Choose(LoungeChat.Choice.Okay));
            chat.Confirm();
            Assert.IsTrue(Blueprints.IsUnlocked(product));
            Assert.AreEqual(100000, Session.Credits);
            StringAssert.Contains("relief", chat.Text);
            Assert.IsTrue(Blueprints.Products(db).Exists(i => i.index == product));
        }

        [Test]
        public void ItsIconIsItsOwn()
        {
            var png = ModManager.BaseContent.Source.ReadBytes("icons/khador_slipstream.png");
            Assert.IsNotNull(png, "the file, not the texture: an edit-mode test can't release one (ModMaterials)");
            Assert.AreEqual((byte)'P', png[1], "a PNG, kept as bytes");
            Assert.IsNotEmpty(ModContent.Parse(ModManager.BaseContent).items[0].json["icon"].ToString());
        }

        [Test]
        public void AModsNumbersAreNotTouched()
        {
            WriteMod("d", "t_after", "1.0", "", ("items.json", "[ { 'id': 'gun', 'base': 2, 'name': 'Gun' } ]"));
            ScanAndEnable("t_after");
            Database.Load();
            int gun = ModContent.ItemIndexOf("t_after:gun"), base_ = ModContent.ItemIndexOf(Key);
            Assert.GreaterOrEqual(gun, GameNames.OriginalItems);
            Assert.AreNotEqual(gun, base_);
            Assert.AreEqual(gun, ModRegistry.All(ModRegistry.Kind.Item)["t_after:gun"]);
        }

        [Test]
        public void ItIsNoModToThePlayerASessionOrASave()
        {
            Database.Load();
            Assert.Contains(ModManager.BaseContent, ModManager.Active.ToList());
            Assert.IsEmpty(ModManager.UserMods);
            Assert.IsFalse(ModManager.Installed.Contains(ModManager.BaseContent));
            Assert.AreEqual("", ModManager.ActiveSignature);
            var save = new SaveData();
            ModSaves.Record(save, Database.Load());
            Assert.IsEmpty(save.mods);
            Assert.IsTrue(save.modKeys.Exists(k => k.key == Key), "its number is saved by key, like a mod's");
            Assert.IsEmpty(ModSaves.MissingMods(save));
        }

        [Test]
        public void NoModMayTakeItsId()
        {
            WriteMod("d", ModManager.BaseId, "1.0", "");
            ModManager.Scan();
            Assert.IsTrue(Installed(ModManager.BaseId).Broken);
        }
    }
}
