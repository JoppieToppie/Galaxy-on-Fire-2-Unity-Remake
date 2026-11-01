// ModBuilderViewSkins.cs
// The Mod Builder's Skins kind (part of ModBuilderView): new looks for the game's ships, as Modding/README.md "Textures
// (skins)" has them: a PNG in the mod's textures/ named like a game texture replaces it on every object that uses it
// (Modding.ModTextures). Left: the ships the project re-skins (ModProject.SkinShips), "Skin a game ship", Remove. Right: the
// selected ship's textures, read off its model's materials (colour, normal map, metallic / smoothness, the lights and engine
// glow layers, marked when other ships share them, and a freighter's / the battleship's burnt wreck set, CombatAssets'
// wreckDamaged), each with its picture (the replacement, else the game's), Replace (a PNG
// stored under the texture's exact name), Save original (the game's texture as a PNG to paint over, into the project's
// templates/ folder, which Export leaves out) and × (back to the game's). The turntable and Test fly show the skinned ship
// (ModBuilder.SkinShip). A change makes the game read the project again (the turntable ship is rebuilt with it).
// The project's new ships on a game model ("Skin a game ship" lists them too; the Ships page's "Its own textures…") take
// textures of their own: each Replace goes to textures/ships/<id>/ (ModProject.ShipSkinScope), so the game's ship of that
// model keeps its look (Modding.ModTextures, Database.ShipOwnTextures).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement skinList, skinPage;

        /// <summary>A ship's skinnable texture: its game name, what it is for, the game's texture, linear data or colour.</summary>
        /// <summary>... 'scope': the ship's assembly, under which a replacement for this ship only is kept (textures/&lt;scope&gt;/).</summary>
        struct SkinTexture { public string name, role, scope; public Texture texture; public bool linear, normal, shared; }

        /// <summary>The project's replacement of a texture as this ship shows it: its own (scoped), else every ship's;
        /// 'only' says which.</summary>
        string ShownSkin(SkinTexture t, out bool only)
        {
            string own = Project.SkinTexture(t.name, t.scope);
            only = own != null;
            return own ?? Project.SkinTexture(t.name);
        }
        readonly Dictionary<int, List<SkinTexture>> skinTextures = new Dictionary<int, List<SkinTexture>>();

        void BuildSkinsPanels(VisualElement left, VisualElement right)
        {
            Add(left, new Label(T("mbSkins", "Skins").ToUpperInvariant()), "mbv-heading", "gof-semibold");
            var scroll = Add(left, new ScrollView(ScrollViewMode.Vertical), "mbv-ship-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(scroll);
            skinList = scroll;
            Act(left, T("mbNewSkin", "Skin a game ship"), PickSkinShip, "mbv-add", "mbv-change");
            Act(left, T("mbRemoveSkin", "Remove skin"), AskRemoveSkin, "mbv-danger", "mbv-remove");
            Act(left, T("mbOtherTextures", "Other textures…"), ShowOtherTextures, "mbv-add");

            var page = Add(right, new ScrollView(ScrollViewMode.Vertical), "mbv-right-scroll");
            page.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            page.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            skinPage = Add(page, new VisualElement(), "mbv-page");
        }

        /// <summary>The project's replacements of the game's other textures (stations, planets, suns, rings, objects) and
        /// backdrop.json: the planets it retextures drawn as stars.</summary>
        void ShowOtherTextures()
        {
            modal.Clear();
            var box = Add(modal, new VisualElement(), "mbv-modal-box", "mbv-modal-box--list", "mbv-panel");
            Heading(box, T("mbOtherTextures", "Other textures…"));
            var scroll = Add(box, new ScrollView(ScrollViewMode.Vertical), "mbv-picker-scroll");
            Note(scroll, T("mbOtherTexturesNote", "A picture named like a game texture replaces it everywhere; reference.md lists the names."));
            foreach (var name in Project.OtherTextures())
            {
                string n = name;
                var row = Row(scroll);
                Add(row, new Label(n), "mbv-inline-label");
                if (ModImport.Available) Act(row, T("mbReplace", "Replace…"), () => PickImage(n, png => { Project.SetSkinTexture(n, png); SkinChanged(); ShowOtherTextures(); }), "mbv-step");
                Act(row, "×", () => { Project.RemoveSkinTexture(n); SkinChanged(); ShowOtherTextures(); }, "mbv-step");
            }
            var add = Row(scroll);
            if (ModImport.Available)
            {
                Act(add, T("mbBackdropTexture", "A planet, sun or ring…"), () =>
                {
                    var names = Resources.LoadAll<Material>("GoF2Backdrop").Select(m => m.mainTexture != null ? m.mainTexture.name : null)
                                         .Where(x => x != null).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                    ShowPicker(T("mbBackdropTexture", "A planet, sun or ring…"), names.Select(x => (x, (Texture2D)null, (Action)(() =>
                        PickImage(x, png => { Project.SetSkinTexture(x, png); SkinChanged(); ShowOtherTextures(); })))).ToList(), true);
                }, "mbv-add", "mbv-change");
                Act(add, T("mbTextureByName", "By its name…"), () => AskText(T("mbTextureByName", "By its name…"), T("mbTextureName", "The game texture's name"), "", x =>
                    PickImage(x, png => { Project.SetSkinTexture(x.Trim(), png); SkinChanged(); ShowOtherTextures(); }), 80), "mbv-add", "mbv-change");
            }

            Heading(scroll, T("mbPlanetsAsStars", "Planets as stars (backdrop.json)"));
            var bd = Project.Backdrop;
            bool stars = (string)bd?["planets"] == "star";
            FlagButton(Row(scroll), T("mbPlanetsStars", "Draw the planets this mod retextures as stars"), stars, v =>
            {
                var o = Project.Backdrop ?? new JObject();
                if (v) o["planets"] = "star"; else o.Remove("planets");
                Project.Backdrop = o;
                SkinChanged();
            }, ShowOtherTextures);
            if (stars)
            {
                Float(scroll, T("mbPlanetGlow", "Their glow (1 = none, default 2)"), bd["planetGlow"] is JValue g && g.Type != JTokenType.String ? (float)g : 2f, 1f, 8f,
                      v => { var o = Project.Backdrop ?? new JObject(); o["planetGlow"] = Math.Round(v, 2); Project.Backdrop = o; SkinChanged(); });
                Float(scroll, T("mbPlanetFlare", "Their flare when looked at (0..1, default 0.35)"), bd["planetFlare"] is JValue f && f.Type != JTokenType.String ? (float)f : 0.35f, 0f, 1f,
                      v => { var o = Project.Backdrop ?? new JObject(); o["planetFlare"] = Math.Round(v, 2); Project.Backdrop = o; SkinChanged(); });
            }
            Act(box, T("mbClose", "Close"), CloseModal);
            modal.AddToClassList("mbv-modal--shown");
        }

        /// <summary>The ship's own textures off its model's materials (the game's model, as the turntable shows it).</summary>
        List<SkinTexture> SkinTexturesOf(int ship)
        {
            if (skinTextures.TryGetValue(ship, out var cached)) return cached;
            var list = new List<SkinTexture>();
            var entry = Database.Shared.ShipAssembly(ship);
            var prefab = entry != null ? AssembledObject.LoadPrefab(entry) : null;
            // The project's new ship on a game model: its replacements are its own (textures/ships/<id>/).
            string shipScope = ModProject.ShipSkinScope(Project?.OwnSkinShip(ship));
            if (prefab != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // The hull's own texture names start with its model's ("ship_028_terran"; a copy's "<model>#ship=N" too);
                // anything else is shared.
                string own = entry.name;
                int hash = own.IndexOf(ModShipKits.VariantSeparator);
                if (hash > 0) own = own.Substring(0, hash);
                string ownUnnumbered = System.Text.RegularExpressions.Regex.Replace(own, "_[0-9]{3}_", "_");   // cargo_003_terran: cargo_terran_*
                var combat = GoF2Remake.Flight.CombatAssets.Load();
                var mats = new List<(Material m, bool wreck)>();
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null || mats.Any(x => x.m == m)) continue;
                        mats.Add((m, false));
                        // A freighter's or the battleship's wreck: the burnt material its hull takes when it dies.
                        int k = combat != null && combat.wreckIntact != null ? Array.IndexOf(combat.wreckIntact, m) : -1;
                        if (k >= 0 && combat.wreckDamaged != null && k < combat.wreckDamaged.Length && combat.wreckDamaged[k] != null)
                            mats.Add((combat.wreckDamaged[k], true));
                    }
                foreach (var (m, wreck) in mats)
                {
                    bool additive = m.shader.name.Contains("Additive");
                    foreach (var (slot, role, linear, normal) in new[]
                             {
                                 ("_BaseMap", "Colour", false, false), ("_MainTex", "Colour", false, false), ("_BumpMap", "Normal map", true, true),
                                 ("_MetallicGlossMap", "Metallic / smoothness", true, false), ("_EmissionMap", "Glow", false, false),
                             })
                    {
                        if (!m.HasProperty(slot) || !(m.GetTexture(slot) is Texture2D t) || string.IsNullOrEmpty(t.name) || !seen.Add(t.name)) continue;
                        string what = additive ? (t.name.Contains("engine") ? "Engine glow" : "Lights")
                                    : t.name.Contains("_emissive") || t.name.Contains("_lights") ? "Lights" : role;
                        if (wreck) what = "Wreck " + what.ToLowerInvariant();
                        bool shared = !t.name.StartsWith(own, StringComparison.OrdinalIgnoreCase) && !t.name.StartsWith(ownUnnumbered, StringComparison.OrdinalIgnoreCase);
                        list.Add(new SkinTexture { name = t.name, role = what, scope = shipScope ?? own, texture = t, linear = linear, normal = normal, shared = shared });
                    }
                }
                int Order(SkinTexture s) => (s.role.StartsWith("Wreck") ? 10 : 0) + (s.role.Replace("Wreck ", "").ToLowerInvariant() switch
                    { "colour" => 0, "normal map" => 1, "metallic / smoothness" => 2, "lights" => 3, "glow" => 3, _ => 4 });
                list = list.OrderBy(s => s.shared ? 1 : 0).ThenBy(Order).ThenBy(s => s.name).ToList();
            }
            skinTextures[ship] = list;
            return list;
        }

        /// <summary>Which ships share a texture, for its row ("Terran ships"): the race lights glow_&lt;race&gt;, the engine
        /// glows (ship_engine_glow every ship's, v_ship_engine_glow the Valkyrie add-on's Vossk ships'), else "ships".</summary>
        static string SharedBy(string texture)
        {
            string n = (texture ?? "").ToLowerInvariant();
            if (n.StartsWith("glow_"))
                return n.Substring(5) switch
                {
                    "terran" => T("mbSharedTerran", "Terran ships"),
                    "vossk" => T("mbSharedVossk", "Vossk ships"),
                    "nivelian" => T("mbSharedNivelian", "Nivelian ships"),
                    "midorian" => T("mbSharedMidorian", "Midorian ships"),
                    "pirates" => T("mbSharedPirates", "pirate ships"),
                    "void" => T("mbSharedVoid", "Void ships"),
                    _ => T("mbSharedShips", "ships"),
                };
            if (n == "v_ship_engine_glow") return T("mbSharedValkyrieVossk", "Vossk add-on ships");
            if (n == "ship_engine_glow") return T("mbSharedEngines", "ships");
            return T("mbSharedShips", "ships");
        }

        int ReplacedCount(int ship) => SkinTexturesOf(ship).Count(t => ShownSkin(t, out _) != null);

        void RebuildSkins()
        {
            skinList.Clear();
            if (Project == null) return;
            var ships = Project.SkinShips();
            if (!ships.Contains(ModBuilder.SkinShip)) ShowSkin(ships.Count > 0 ? ships[0] : -1, false);
            foreach (int ship in ships)
            {
                int s = ship;
                var row = new Button(() => { sound(false); ShowSkin(s, true); }) { focusable = false };
                row.AddToClassList("mbv-ship-row");
                row.EnableInClassList("mbv-ship-row--on", s == ModBuilder.SkinShip);
                var icon = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-ship-icon");
                var tex = ItemInfo.ShipIcon(s);
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-ship-text");
                Add(text, new Label(GameNames.Ship(s)) { pickingMode = PickingMode.Ignore }, "mbv-ship-name");
                int n = ReplacedCount(s);
                Add(text, new Label(n == 1 ? T("mbOneTexture", "1 texture replaced") : string.Format(T("mbTexturesN", "{0} textures replaced"), n))
                    { pickingMode = PickingMode.Ignore }, "mbv-ship-sub");
                skinList.Add(row);
            }
            if (ships.Count == 0) Note(skinList, T("mbNoSkins", "No skins yet. Pick a game ship to give it new textures."));
        }

        /// <summary>The skinned ship on the turntable (and in Test fly).</summary>
        void ShowSkin(int ship, bool rebuild)
        {
            if (ModBuilder.SkinShip != ship)
            {
                ModBuilder.SkinShip = ship;
                if (kind == ModBuilder.Kind.Skins) { ModBuilder.ApplyShip(); boundShip = -2; }
            }
            if (rebuild) { RebuildSkins(); BuildSkinPage(); }
        }

        void BuildSkinPage()
        {
            skinPage.Clear();
            int ship = ModBuilder.SkinShip;
            if (ship < 0) { Note(skinPage, T("mbPickSkin", "Add a skin on the left to start.")); return; }
            Heading(skinPage, string.Format(T("mbSkinOf", "Skin of the {0}"), GameNames.Ship(ship)));
            // The project's new ship on a game model: every texture is replaced on it alone.
            bool ownShip = ModProject.ShipSkinScope(Project.OwnSkinShip(ship)) != null;
            Note(skinPage, ownShip
                ? T("mbSkinNoteOwnShip", "Changes this ship only. Save the original, paint over it, then replace it (2048 x 2048 looks best).")
                : T("mbSkinNote", "Changes every ship of this model. Save the original, paint over it, then replace it (2048 x 2048 looks best)."));
            var list = SkinTexturesOf(ship);
            if (list.Count == 0) { Note(skinPage, T("mbNoTextures", "This ship has no textures that can be replaced.")); return; }
            foreach (var t in list)
            {
                var tex = t;
                string own = ShownSkin(t, out bool onlyThis);
                var row = Add(skinPage, new VisualElement(), "mbv-skin-row");
                var thumb = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-skin-thumb");
                Texture shown = own != null && ModManager.BuilderProject != null ? ModMaterials.Texture(ModManager.BuilderProject, own, t.linear, normal: t.normal) : null;
                shown ??= t.texture;
                if (shown != null && t.normal) shown = NormalThumb(shown, (own ?? "") + t.name);   // shown blue, like a normal map
                if (shown != null) thumb.style.backgroundImage = Background.FromTexture2D((Texture2D)shown);
                var info = Add(row, new VisualElement(), "mbv-skin-info");
                string group = SharedBy(t.name), shipName = GameNames.Ship(ship);
                string state = own == null ? "" : ownShip ? (onlyThis ? string.Format(T("mbReplacedOnly", "replaced for the {0} only"), shipName)
                                                                      : T("mbReplacedEveryShip", "replaced for every ship (the project's own file)"))
                             : !t.shared ? T("mbReplaced", "replaced")
                             : onlyThis ? string.Format(T("mbReplacedOnly", "replaced for the {0} only"), shipName)
                             : string.Format(T("mbReplacedAllOf", "replaced for all {0}"), group);
                Add(info, new Label((T("mbTexRole_" + t.role, t.role) + (own != null ? "  ·  " + state : "")).ToUpperInvariant()), "mbv-skin-role", "gof-semibold")
                    .EnableInClassList("mbv-skin-role--on", own != null);
                Add(info, new Label(t.name), "mbv-skin-name");
                if (t.shared && !ownShip)
                    Add(info, new Label(string.Format(T("mbSharedTextureOf", "Shared by all {0}. \"{1} only\" changes this ship; \"All {0}\" changes them all."),
                                                      group, shipName)), "mbv-note", "mbv-note--warn");
                if (t.role.EndsWith("etallic / smoothness"))
                    Add(info, new Label(T("mbMetallicHint", "Red: how metallic (the game's ships: none). Alpha: how shiny.")), "mbv-note");
                else if (t.normal)
                    Add(info, new Label(T("mbNormalHint", "An ordinary RGB normal map (the saved original is one).")), "mbv-note");
                var buttons = Add(info, new VisualElement(), "mbv-row");
                void Pick(string scope) => PickImage(T("mbPickSkinTexture", "Choose the new texture"), path =>
                {
                    Project.SetSkinTexture(tex.name, path, scope);
                    Project.AddSkin(ModBuilder.SkinShip);
                    SkinChanged();
                });
                if (ModImport.Available)
                {
                    if (ownShip) Act(buttons, T("mbReplace", "Replace…"), () => Pick(tex.scope));   // this ship only
                    else if (t.shared)
                    {
                        Act(buttons, string.Format(T("mbReplaceOnly", "{0} only…"), shipName), () => Pick(tex.scope));
                        Act(buttons, string.Format(T("mbReplaceAllOf", "All {0}…"), group), () => Pick(null));
                    }
                    else Act(buttons, T("mbReplace", "Replace…"), () => Pick(null));
                }
                Act(buttons, T("mbSaveOriginal", "Save original"), () => SaveTemplate(tex));   // its own status line
                if (own != null && (!ownShip || onlyThis))   // a new ship's page leaves the project's every-ship file alone
                    Act(buttons, "×", () => { Project.RemoveSkinTexture(tex.name, onlyThis ? tex.scope : null); SkinChanged(); }, "mbv-step");
            }
            var foot = Add(skinPage, new VisualElement(), "mbv-row");
            Act(foot, T("mbSaveAllOriginals", "Save all originals"), () =>
            {
                int saved = list.Count(t => SaveTemplate(t, quiet: true));
                if (saved == list.Count) Status(string.Format(T("mbTemplatesSaved", "Saved {0} originals in the project's templates folder."), saved), false);
            });
            if (!Application.isMobilePlatform)
                Act(foot, T("mbOpenTemplates", "Open the saved originals"), () =>
                {
                    string dir = System.IO.Path.Combine(Project.Folder, ModProject.TemplatesFolder);
                    System.IO.Directory.CreateDirectory(dir);
                    Application.OpenURL("file:///" + dir.Replace('\\', '/'));
                });
            Note(skinPage, T("mbTemplatesNote", "Saved originals go to the project's templates folder, not the exported mod."));
        }

        readonly Dictionary<string, Texture2D> normalThumbs = new Dictionary<string, Texture2D>();

        /// <summary>A normal map's thumbnail as an ordinary blue normal map: the game's are stored as X / Y only (DXT5nm-style
        /// or BC5), which shows pink. A 128 px copy read back and decoded like Save original.</summary>
        Texture2D NormalThumb(Texture src, string key)
        {
            if (normalThumbs.TryGetValue(key, out var cached) && cached != null) return cached;
            const int Size = 128;
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
                t.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                t.Apply();
                DecodeNormals(t, (src as Texture2D)?.format ?? TextureFormat.RGBA32);
                normalThumbs[key] = t;
                return t;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>A texture file changed: the game reads the project again and the turntable ship is rebuilt with it.</summary>
        void SkinChanged()
        {
            SaveNow();
            RequestReload(false);
            Status(T("mbUpdating", "Updating the preview…"), false);
            RebuildSkins();
            BuildSkinPage();
        }

        /// <summary>The game's texture as a PNG in templates/ (read back from the GPU: the game's textures aren't readable). A
        /// normal map is written as an ordinary RGB normal map (the imported one keeps only X and Y, in R/G or A/G).</summary>
        bool SaveTemplate(SkinTexture t, bool quiet = false)
        {
            var src = t.texture;
            if (src == null) return false;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32,
                                                t.linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, t.linear);
                copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                copy.Apply();
                if (t.normal) DecodeNormals(copy, (src as Texture2D)?.format ?? TextureFormat.RGBA32);
                string dir = System.IO.Path.Combine(Project.Folder, ModProject.TemplatesFolder);
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, t.name + ".png");
                System.IO.File.WriteAllBytes(path, copy.EncodeToPNG());
                if (!quiet) Status(string.Format(T("mbTemplateSaved", "Saved {0}.png in the project's templates folder."), t.name), false);
                return true;
            }
            catch (Exception e) { Status(string.Format(T("mbTemplateFailed", "The original couldn't be saved: {0}"), e.Message), true); return false; }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }

        /// <summary>An imported normal map as read back: X / Y in R / G (BC5) or A / G (DXT5nm-style), Z dropped. Written back
        /// as RGB = XYZ like a painted normal map, opaque.</summary>
        internal static void DecodeNormals(Texture2D tex, TextureFormat format)
        {
            var px = tex.GetPixels32();
            bool xInAlpha = format == TextureFormat.DXT5 || format == TextureFormat.DXT5Crunched || format == TextureFormat.ETC2_RGBA8 || format == TextureFormat.ASTC_4x4;
            if (!xInAlpha && format != TextureFormat.BC5)
            {
                // Unknown layout: an RGB normal map already has its blue channel high (Z toward the viewer).
                double blue = 0;
                for (int i = 0; i < px.Length; i += 97) blue += px[i].b;
                if (blue / Math.Max(1, px.Length / 97) > 128) return;
            }
            for (int i = 0; i < px.Length; i++)
            {
                float x = (xInAlpha ? px[i].a : px[i].r) / 255f * 2f - 1f, y = px[i].g / 255f * 2f - 1f;
                float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
                px[i] = new Color32((byte)Mathf.RoundToInt((x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((y * 0.5f + 0.5f) * 255f),
                                    (byte)Mathf.RoundToInt((z * 0.5f + 0.5f) * 255f), 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
        }

        void PickSkinShip()
        {
            var db = Database.Shared;
            var taken = new HashSet<int>(Project.SkinShips());
            var choices = new List<(string, Texture2D, Action)>();
            for (int i = 0; i < GameNames.OriginalShips; i++)
            {
                if (i == 13 || i == 14 || i == 15 || taken.Contains(i) || db.Ship(i) == null || db.ShipAssembly(i) == null) continue;
                int index = i;
                choices.Add((GameNames.Ship(i), ItemInfo.ShipIcon(i), () =>
                {
                    Project.AddSkin(index);
                    SaveNow();
                    ShowSkin(index, true);
                }));
            }
            // The project's new ships on a game model: textures of their own (the game's ship of that model keeps its look).
            foreach (var s in Project.Ships)
            {
                if (!ModProject.OwnsSkinOf(s)) continue;
                int index = ModBuilder.IndexOf(s);
                if (index < 0 || taken.Contains(index) || db.Ship(index) == null) continue;
                choices.Add((string.Format(T("mbOwnShipSkin", "{0} (your ship, its own textures)"), GameNames.Ship(index)), ItemInfo.ShipIcon(index), () =>
                {
                    Project.AddSkin(index);
                    SaveNow();
                    ShowSkin(index, true);
                }));
            }
            ShowPicker(T("mbNewSkinTitle", "Skin a game ship"), choices);
        }

        void AskRemoveSkin()
        {
            int ship = ModBuilder.SkinShip;
            if (ship < 0) return;
            Confirm(T("mbRemoveSkin", "Remove skin"), string.Format(T("mbRemoveSkinText", "Remove the {0}'s skin and its textures from the project?"), GameNames.Ship(ship)), () =>
            {
                // the ship's own replacements; one for every ship stays unless it is one of this model's own textures (a new
                // ship on a game model only has its own: the game ship's skin stays)
                bool ownShip = ModProject.ShipSkinScope(Project.OwnSkinShip(ship)) != null;
                foreach (var t in SkinTexturesOf(ship)) { Project.RemoveSkinTexture(t.name, t.scope); if (!t.shared && !ownShip) Project.RemoveSkinTexture(t.name); }
                Project.RemoveSkin(ship);
                SaveNow();
                RequestReload(false);
                var left = Project.SkinShips();
                ShowSkin(left.Count > 0 ? left[0] : -1, true);
            });
        }
    }
}
