// ModBuilderViewCampaign.cs
// The Mod Builder's Campaign kind (part of ModBuilderView), edited in the hangar: the project's campaign.json
// (ProjectCampaign: a new game of its own under Start new game, Modding/README.md "Campaigns") and its new-game options
// (gameoptions.json, ProjectGameOption: the Game options panel's cards). Left: the campaign (or "+ Make this mod a
// campaign") and the options, "+ New option", Remove. Right: the campaign's card (name, description, images), where it
// starts (a station and a ship: the game's, the project's own or another mod's, whose pick makes it a dependency), money,
// the ship's equipment and hold, the standings, its story quest, the galaxy / items / ships kept to the mods' own, the NPC
// fighters per race; or an option's card. The start ship stands on the turntable with its equipment
// (ModBuilder.PreviewShip / PreviewLoadout), so Test fly flies the campaign's first ship.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement campaignList, campaignPage;
        /// <summary>The option shown on the right (null: the campaign).</summary>
        ProjectGameOption currentOption;

        static readonly string[] TrafficRaces = { "terran", "vossk", "nivelian", "midorian", "pirate" };

        void BuildCampaignPanels(VisualElement left, VisualElement right)
        {
            campaignList = ListPanel(left, T("mbCampaign", "Campaign"));
            Act(left, T("mbNewOption", "+ New game option"), NewGameOption, "mbv-add");
            Act(left, T("mbRemoveSelected", "Remove selected"), AskRemoveCampaignEntry, "mbv-danger", "mbv-remove");
            campaignPage = PagePanel(right);
        }

        Texture2D ProjectImage(string path) =>
            string.IsNullOrEmpty(path) || ModManager.BuilderProject == null ? null : ModMaterials.Texture(ModManager.BuilderProject, path, false, true);

        void RebuildCampaign()
        {
            campaignList.Clear();
            if (Project == null) return;
            if (currentOption != null && !Project.GameOptions.Contains(currentOption)) currentOption = null;
            var c = Project.Campaign;
            if (c != null)
                ListRow(campaignList, c.Name ?? Project.Name, T("mbCampaignSub", "A new game of its own"), currentOption == null,
                        () => { currentOption = null; RebuildCampaign(); BuildCampaignPage(); }, ProjectImage(c.Image), "mbv-card-thumb");
            else
                Act(campaignList, T("mbMakeCampaign", "Make this mod a campaign"), () =>
                {
                    Project.Campaign = new ProjectCampaign(new Newtonsoft.Json.Linq.JObject { ["name"] = Project.Name, ["startStation"] = Session.StationIndex, ["startShip"] = 10, ["credits"] = 10000 });
                    currentOption = null;
                    MarkDirty();
                    RebuildCampaign();
                    BuildCampaignPage();
                }, "mbv-add", "mbv-change");
            if (Project.GameOptions.Count > 0) Heading(campaignList, T("mbGameOptions", "New-game options"));
            foreach (var op in Project.GameOptions)
            {
                var o = op;
                ListRow(campaignList, o.Name ?? o.Id, (o.Default ? T("mbDefaultOn", "On by default") : T("mbDefaultOff", "Off by default")) + "  ·  option(" + Project.Key(o.Id) + ")",
                        o == currentOption, () => { currentOption = o; RebuildCampaign(); BuildCampaignPage(); }, ProjectImage(o.Image), "mbv-card-thumb");
            }
            ShowCampaignShip();
        }

        /// <summary>The start ship with its equipment on the turntable.</summary>
        void ShowCampaignShip()
        {
            var c = Project.Campaign;
            int ship = c != null ? ModBuilder.ShipNumber(c.StartShip) : -1;
            var loadout = c?.Equipment.Select(e => new ItemStack(ModBuilder.ItemNumber(e.item), Mathf.Max(1, e.amount))).Where(s => s.item >= 0).ToList();
            ModBuilder.PreviewShip = ship;
            ModBuilder.PreviewLoadout = loadout;
            ModBuilder.ApplyShip();
            if (Session.ShipIndex != boundShip) boundShip = -2;
        }

        void NewGameOption() => AskText(T("mbNewOption", "+ New game option"), T("mbName", "Name"), "", name =>
        {
            currentOption = Project.AddGameOption(name);
            MarkDirty();
            RebuildCampaign();
            BuildCampaignPage();
        });

        void AskRemoveCampaignEntry()
        {
            if (currentOption != null)
            {
                var o = currentOption;
                Confirm(T("mbRemoveOption", "Remove option"), string.Format(T("mbRemoveText", "Remove \"{0}\" from the project?"), o.Name ?? o.Id), () =>
                {
                    Project.GameOptions.Remove(o);
                    currentOption = null;
                    MarkDirty();
                    RebuildCampaign();
                    BuildCampaignPage();
                });
                return;
            }
            if (Project.Campaign == null) return;
            Confirm(T("mbRemoveCampaign", "Remove the campaign"), T("mbRemoveCampaignText", "This mod stops being a campaign of its own (its content stays)."), () =>
            {
                Project.Campaign = null;
                MarkDirty();
                RebuildCampaign();
                BuildCampaignPage();
            });
        }

        /// <summary>A card image (a campaign's or an option's), copied into cards/.</summary>
        void CardImage(VisualElement parent, string label, string current, string baseName, Action<string> set)
        {
            var row = Row(parent);
            Add(row, new Label($"{label}: {(current != null ? System.IO.Path.GetFileName(current) : T("mbNone", "none"))}"), "mbv-inline-label");
            Act(row, T("mbChoose", "Choose…"), () => PickImage(T("mbPickCard", "Choose the card's picture (290 x 448 or a multiple)"), path =>
            {
                string old = current;
                set(Project.AddFile(path, "cards", baseName, ".png"));
                Project.DeleteFile(old, false);
                MarkDirty();
                RebuildCampaign();
                BuildCampaignPage();
            }));
            if (current != null) Act(row, "×", () => { set(null); Project.DeleteFile(current, false); MarkDirty(); RebuildCampaign(); BuildCampaignPage(); }, "mbv-step");
        }

        void BuildCampaignPage()
        {
            campaignPage.Clear();
            if (currentOption != null) { JsonButton(campaignPage, currentOption.Json, () => { SaveNow(); RequestReload(false); BuildCampaignPage(); }); BuildOptionPage(currentOption); return; }
            var c = Project.Campaign;
            if (c == null)
            {
                Note(campaignPage, T("mbNoCampaign", "A campaign is a new game of its own, a card in Start new game. Make one on the left, or add new-game options."));
                return;
            }
            JsonButton(campaignPage, c.Json, () => { SaveNow(); RequestReload(false); BuildCampaignPage(); });
            // The card as the menu shows it.
            var card = Add(campaignPage, new VisualElement(), "mbv-card-preview");
            var img = ProjectImage(c.Image);
            if (img != null) card.style.backgroundImage = new StyleBackground(img);
            if (c.ShowTitle) Add(card, new Label((c.Name ?? Project.Name).ToUpperInvariant()), "mbv-card-title", "gof-semibold");

            Heading(campaignPage, T("mbCard", "Card"));
            Text(campaignPage, T("mbName", "Name"), c.Name, 60, v => { c.Name = v.Trim(); MarkDirty(); RebuildCampaign(); BuildCampaignPage(); });
            Text(campaignPage, T("mbDescription", "Description"), c.Description, 600, v => { c.Description = v; MarkDirty(); }, true);
            CardImage(campaignPage, T("mbPicture", "Picture"), c.Image, "campaign", v => c.Image = v);
            CardImage(campaignPage, T("mbPictureHover", "Picture when selected"), c.ImageHover, "campaign_hover", v => c.ImageHover = v);
            var trow = Row(campaignPage);
            FlagButton(trow, T("mbShowTitle", "Name on the card"), c.ShowTitle, v => c.ShowTitle = v, () => BuildCampaignPage());

            Heading(campaignPage, T("mbStart", "The start"));
            RefField(campaignPage, T("mbStartStation", "Docked at"), RefKind.Station, c.StartStation, v => { c.StartStation = v; MarkDirty(); BuildCampaignPage(); }, clear: false);
            RefField(campaignPage, T("mbStartShip", "Flying"), RefKind.Ship, c.StartShip, v => { c.StartShip = v; MarkDirty(); RebuildCampaign(); BuildCampaignPage(); RequestReload(false); }, clear: false);
            Int(campaignPage, T("mbCredits", "Credits"), c.Credits, 0, 999999999, v => { c.Credits = v; MarkDirty(); });
            StackList(campaignPage, T("mbEquipment", "Mounted on the ship"), c.Equipment, l => { c.Equipment = l; MarkDirty(); ShowCampaignShip(); BuildCampaignPage(); }, true);
            Act(campaignPage, T("mbEquipmentFromShip", "Use the test loadout of the Ships tab's ship"), () =>
            {
                var s = Current;
                var l = s != null ? Project.Loadout(s) : null;
                if (l == null || l.Count == 0) { Status(T("mbNoLoadout", "The Ships tab's ship has no test loadout."), true); return; }
                c.Equipment = l.Select(x => new ProjectStack(RefOfItem(x.item), x.amount)).ToList();
                MarkDirty();
                ShowCampaignShip();
                BuildCampaignPage();
            });
            StackList(campaignPage, T("mbCargo", "In the hold"), c.Cargo, l => { c.Cargo = l; MarkDirty(); BuildCampaignPage(); }, false);
            Heading(campaignPage, T("mbStanding", "Standing"));
            var st = c.Standing;
            var srow = campaignPage;
            Int(srow, T("mbTerranVossk", "Terran (+) / Vossk (-)"), st.x, -100, 100, v => { c.Standing = new Vector2Int(v, c.Standing.y); MarkDirty(); });
            Int(srow, T("mbNivelianMidorian", "Nivelian (+) / Midorian (-)"), st.y, -100, 100, v => { c.Standing = new Vector2Int(c.Standing.x, v); MarkDirty(); });

            Heading(campaignPage, T("mbStory", "Story"));
            RefField(campaignPage, T("mbStoryQuest", "Its story mission (starts with the game)"), RefKind.Quest, c.Quest, v => { c.Quest = v; MarkDirty(); BuildCampaignPage(); },
                     T("mbNoQuest", "none"));
            Note(campaignPage, T("mbStoryQuestNote", "Made in Missions; the mod's other missions start by their own conditions."));

            Heading(campaignPage, T("mbWorld", "The world"));
            var wrow = Row(campaignPage);
            FlagButton(wrow, T("mbModGalaxy", "Only mods' systems"), c.ModOnly("galaxy"), v => c.SetModOnly("galaxy", v), () => BuildCampaignPage());
            FlagButton(wrow, T("mbModItems", "Only mods' items"), c.ModOnly("items"), v => c.SetModOnly("items", v), () => BuildCampaignPage());
            FlagButton(wrow, T("mbModShips", "Only mods' ships"), c.ModOnly("ships"), v => c.SetModOnly("ships", v), () => BuildCampaignPage());
            Note(campaignPage, T("mbModWorldNote", "Only this mod's systems exist; shops sell only mods' items and ships."));
            Caption(campaignPage, T("mbTraffic", "Ships the NPC fighters fly"));
            foreach (var race in TrafficRaces)
            {
                string r = race;
                var ships = c.TrafficShips(race);
                var row = Row(campaignPage);
                Add(row, new Label($"{char.ToUpperInvariant(race[0]) + race.Substring(1)}: {(ships.Count == 0 ? T("mbTheGames", "the game's") : string.Join(", ", ships.Select(x => RefLabel(RefKind.Ship, x, x))))}"), "mbv-inline-label");
                Act(row, "+", () => PickRef(RefKind.Ship, T("mbTrafficShip", "A ship they fly"), e => { var l = c.TrafficShips(r); if (!l.Contains(e.value)) l.Add(e.value); c.SetTrafficShips(r, l); MarkDirty(); BuildCampaignPage(); }), "mbv-step");
                if (ships.Count > 0) Act(row, "×", () => { c.SetTrafficShips(r, null); MarkDirty(); BuildCampaignPage(); }, "mbv-step");
            }
            Note(campaignPage, T("mbCampaignTry", "Export the mod and turn it on: its card shows in Start new game."));
        }

        /// <summary>An item's reference: the project's own or another mod's by its key, a game item by its number.</summary>
        string RefOfItem(int item) => ModContent.ItemKey(item) ?? item.ToString();

        /// <summary>A list of items with amounts (equipment, cargo); 'mounted': secondaries take their ammo as the amount.</summary>
        void StackList(VisualElement parent, string caption, List<ProjectStack> stacks, Action<List<ProjectStack>> set, bool mounted)
        {
            Caption(parent, caption);
            for (int i = 0; i < stacks.Count; i++)
            {
                int n = i;
                var row = Row(parent);
                var e = ModRefs.Find(RefKind.Item, stacks[i].item, Project);
                var icon = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-item-icon");
                var tex = e != null ? RefIcon(RefKind.Item, e) : null;
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                Add(row, new Label(RefLabel(RefKind.Item, stacks[i].item, "?")), "mbv-inline-label");
                Int(row, "", stacks[i].amount, 1, 100000, v => { var l = new List<ProjectStack>(stacks); l[n] = new ProjectStack(l[n].item, v); set(l); });
                Act(row, "×", () => { var l = new List<ProjectStack>(stacks); l.RemoveAt(n); set(l); }, "mbv-step");
            }
            Act(parent, T("mbAddItem", "+ Add an item"), () => PickRef(RefKind.Item, caption, e =>
            {
                var l = new List<ProjectStack>(stacks) { new ProjectStack(e.value, mounted && e.type == 1 ? ModBuilder.Ammo : 1) };
                set(l);
            }, mounted ? (Func<RefEntry, bool>)(e => e.type >= 0 && e.type <= 3) : null), "mbv-add");
        }

        void BuildOptionPage(ProjectGameOption o)
        {
            var card = Add(campaignPage, new VisualElement(), "mbv-card-preview");
            var img = ProjectImage(o.Image);
            if (img != null) card.style.backgroundImage = new StyleBackground(img);
            if (o.ShowTitle) Add(card, new Label((o.Name ?? o.Id).ToUpperInvariant()), "mbv-card-title", "gof-semibold");
            Heading(campaignPage, T("mbGameOption", "New-game option"));
            Text(campaignPage, T("mbName", "Name"), o.Name, 60, v => { o.Name = v.Trim(); MarkDirty(); RebuildCampaign(); BuildCampaignPage(); });
            Text(campaignPage, T("mbDescription", "Description"), o.Description, 400, v => { o.Description = v; MarkDirty(); }, true);
            CardImage(campaignPage, T("mbPicture", "Picture"), o.Image, "option_" + o.Id, v => o.Image = v);
            CardImage(campaignPage, T("mbPictureHover", "Picture when selected"), o.ImageHover, "option_" + o.Id + "_hover", v => o.ImageHover = v);
            var row = Row(campaignPage);
            FlagButton(row, T("mbShowTitle", "Name on the card"), o.ShowTitle, v => o.ShowTitle = v, () => BuildCampaignPage());
            FlagButton(row, T("mbOnByDefault", "On by default"), o.Default, v => o.Default = v, () => { RebuildCampaign(); BuildCampaignPage(); });
            Note(campaignPage, string.Format(T("mbOptionNote", "A switch on the Game options panel; conditions ask for it with option({0})."), Project.Key(o.Id)));
        }
    }
}
