using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetPlayerStations
    {

        /// <summary>Public, presentation-neutral dry-dock state for future station screens and animated build visuals.</summary>
        [Serializable]
        public sealed class DryDockVisualStatus
        {
            public int stationId;
            public bool installed, operational, building;
            public int storedHulls, hullCapacity, blueprintItem = -1, shipIndex = -1;
            public string stationName, blueprintName;
            public long startedUtc, completesUtc, durationSeconds, remainingSeconds;
            public float progress;
            public bool consumesBlueprint;
        }

        /// <summary>
        /// Read-only status snapshot for visual/UI layers. It contains no owner-private storage or profile data.
        /// Times are UTC Unix seconds; progress is clamped to 0..1.
        /// </summary>
        public static DryDockVisualStatus GetDryDockVisualStatus(int id)
        {
            var record = Find(id);
            if (record == null) return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            bool installed = record.offeredForClaimUtc == 0 && (record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false);
            bool building = installed && record.dryDockBuilding;
            long duration = Math.Max(1L, record.dryDockCompletesUtc - record.dryDockStartedUtc);
            long remaining = building ? Math.Max(0L, record.dryDockCompletesUtc - now) : 0L;
            var db = NetGame.Db;
            return new DryDockVisualStatus
            {
                stationId = record.id,
                stationName = record.name,
                installed = installed,
                operational = installed && !record.underConstruction,
                building = building,
                storedHulls = NetGame.IsServer ? (record.dryDockShips?.Count ?? 0) : record.visibleDryDockHullCount,
                hullCapacity = MaxDryDockShipsPerStation,
                blueprintItem = building ? record.dryDockBlueprintItem : -1,
                shipIndex = building ? record.dryDockShip : -1,
                blueprintName = building ? (db?.Ship(record.dryDockShip)?.name ?? ("Ship " + record.dryDockShip)) : null,
                startedUtc = building ? record.dryDockStartedUtc : 0,
                completesUtc = building ? record.dryDockCompletesUtc : 0,
                durationSeconds = building ? duration : 0,
                remainingSeconds = remaining,
                progress = building ? Math.Max(0f, Math.Min(1f, 1f - remaining / (float)duration)) : 0f,
                consumesBlueprint = building && record.dryDockConsumeBlueprint,
            };
        }

        static long nextDryDockRetryUtc, nextTransactionRetryUtc;
        const int FactorySecondsPerBatch = 60;

        static int DryDockBuildDuration(int productId, int shipIndex)
        {
            var definition = Modding.ModBlueprints.Of(productId);
            if (definition != null && definition.constructionTimeSeconds > 0)
                return definition.constructionTimeSeconds;
            string kind = Modding.ShipClassRules.ClassOf(NetGame.Db?.Ship(shipIndex));
            if (string.Equals(kind, Modding.ShipClassRules.Frigate, StringComparison.OrdinalIgnoreCase)) return 21600;
            if (string.Equals(kind, Modding.ShipClassRules.Freighter, StringComparison.OrdinalIgnoreCase)) return 28800;
            if (string.Equals(kind, Modding.ShipClassRules.Cruiser, StringComparison.OrdinalIgnoreCase)) return 43200;
            if (string.Equals(kind, Modding.ShipClassRules.Carrier, StringComparison.OrdinalIgnoreCase)) return 64800;
            return 7200;
        }

        static void ClearDryDockJob(PlayerStationRecord record)
        {
            record.dryDockBuilding = false;
            record.dryDockBlueprintItem = -1;
            record.dryDockShip = -1;
            record.dryDockStartedUtc = 0;
            record.dryDockCompletesUtc = 0;
            record.dryDockConsumeBlueprint = false;
            record.dryDockBuilderProfile = null;
        }

        /// <summary>Finish a due job exactly once. A failed save restores the active job for retry on the next tick.</summary>
        static bool CompleteDryDockJob(PlayerStationRecord record, long now)
        {
            if (record == null || !record.dryDockBuilding || record.dryDockCompletesUtc > now) return false;
            record.dryDockShips ??= new List<StoredShip>();
            if (record.dryDockShips.Count >= MaxDryDockShipsPerStation) return false;
            int ship = record.dryDockShip;
            int race = NetGame.Db?.Systems?.Find(x => x.index == record.system)?.raceId ?? 0;
            record.dryDockShips.Add(new StoredShip(ship, race, new List<int>()));
            record.dryDockShipOwners ??= new List<string>();
            record.dryDockShipOwners.Add(record.dryDockBuilderProfile ?? record.ownerProfile ?? "");
            long started = record.dryDockStartedUtc, completes = record.dryDockCompletesUtc;
            int blueprint = record.dryDockBlueprintItem;
            bool consume = record.dryDockConsumeBlueprint;
            string builder = record.dryDockBuilderProfile;
            ClearDryDockJob(record);
            if (Save()) return true;
            record.dryDockShips.RemoveAt(record.dryDockShips.Count - 1);
            record.dryDockShipOwners.RemoveAt(record.dryDockShipOwners.Count - 1);
            record.dryDockBuilding = true;
            record.dryDockBlueprintItem = blueprint;
            record.dryDockShip = ship;
            record.dryDockStartedUtc = started;
            record.dryDockCompletesUtc = completes;
            record.dryDockConsumeBlueprint = consume;
            record.dryDockBuilderProfile = builder;
            return false;
        }

        /// <summary>Called by the authoritative server update loop; due jobs survive restarts via UTC timestamps.</summary>
        internal static void Tick()
        {
            if (!NetGame.IsServer || !sessionActive) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // A durable write-ahead journal is the transaction's commit point. If a profile or
            // registry write temporarily fails after the journal is created, retry it during the
            // live session instead of waiting for another station command or a server restart.
            if (now >= nextTransactionRetryUtc && File.Exists(TransactionPath))
            {
                bool recovered = RecoverPendingTransaction();
                bool pending = File.Exists(TransactionPath);
                if (recovered && !pending) persistenceFaulted = false;
                nextTransactionRetryUtc = pending ? now + 5 : 0;
            }
            if (registry?.stations == null) return;
            PublishStationManagementAccess();
            TickJumpgates(); // fixed-destination gate projects share the persistent UTC construction lifecycle.
            if (registry?.stations == null) return;
            TickStationDestruction();
            if (registry?.stations == null) return;
            TickOrbitalStructures(now);
            if (registry?.stations == null) return;
            TickWreckClaims();
            if (registry?.stations == null) return; // a failed station save can disable the registry
            TickFactoryJobs(now);
            if (now < nextDryDockRetryUtc) return;
            foreach (var record in registry.stations)
                if (record != null && record.offeredForClaimUtc == 0 && record.dryDockBuilding && record.dryDockCompletesUtc <= now)
                    if (!CompleteDryDockJob(record, now)) nextDryDockRetryUtc = now + 5;
        }

        /// <summary>List ship blueprints that this station's dry dock can construct.</summary>
        internal static string DryDockBlueprintList(NetPlayer by, int id)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out _)) return "Dry dock access is private to the owner and authorised faction members.";
            if (record.underConstruction) return "Finish station construction before using the dry dock.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false))
                return "This station has no dry dock module.";
            if (record.dryDockBuilding) return "The dry dock is occupied by " + (NetGame.Db?.Ship(record.dryDockShip)?.name ?? "a ship") +
                ". Remaining seconds: " + Math.Max(0L, record.dryDockCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + ".";
            var db = NetGame.Db;
            if (db == null) return "The ship database is not ready.";
            var lines = new List<string> { "Dry dock ship blueprints (product item id):" };
            foreach (var product in GoF2Remake.Data.Blueprints.Products(db))
            {
                int ship = Modding.ModBlueprints.ShipOf(product.index);
                if (ship < 0 || !Modding.ModBlueprints.RequiresDryDock(product.index, db) ||
                    Modding.ModContent.IsBlueprintRestricted(product.index) || IsBlueprintConsumed(product.index) || !GoF2Remake.Data.Blueprints.IsUnlocked(product.index)) continue;
                lines.Add(product.index + ": " + (db.Ship(ship)?.name ?? ("Ship " + ship)) +
                    " — " + (Modding.ShipClassRules.ClassLabel(db.Ship(ship)) ?? "Dry-dock restricted"));
            }
            if (lines.Count == 1) lines.Add("No unlocked dry-dock ship blueprints are available.");
            lines.Add("Start with /station drydock-build <station id> <blueprint item id>.");
            return string.Join("\n", lines);
        }

        /// <summary>
        /// Start a timed, persistent dry-dock job. The full recipe is reserved from station storage at start;
        /// the completed bare hull appears in the dry dock when the UTC timer expires.
        /// </summary>
        internal static string DryDockBuildShip(NetPlayer by, int id, int productId)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManageHangar(by, record, out string builderAccount))
                return "Dock at this station with an authorised profile to use its dry dock.";
            if (record.underConstruction) return "Finish station construction before building ships.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "drydock" && a.level > 0) ?? false))
                return "Install a dry dock module first.";
            CompleteDryDockJob(record, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (record.dryDockBuilding)
                return "The dry dock is already constructing " + (NetGame.Db?.Ship(record.dryDockShip)?.name ?? "a ship") +
                    "; remaining seconds: " + Math.Max(0L, record.dryDockCompletesUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + ".";
            if (StorageCapacity(record) <= 0)
                return "Install station storage first; ship construction materials are drawn from station storage.";
            record.dryDockShips ??= new List<StoredShip>();
            if (record.dryDockShips.Count >= MaxDryDockShipsPerStation)
                return "This station's dry dock is full.";
            var db = NetGame.Db;
            var product = db?.Item(productId);
            int shipIndex = Modding.ModBlueprints.ShipOf(productId);
            if (product == null || shipIndex < 0 || db.Ship(shipIndex) == null)
                return "That item is not a valid ship blueprint.";
            if (Modding.ModContent.IsBlueprintRestricted(productId) || !Modding.ModContent.ItemAvailableFor(productId, "blueprints"))
                return "That blueprint is unavailable in this multiplayer session.";
            if (!Modding.ModBlueprints.RequiresDryDock(productId, db))
                return "That blueprint does not require a dry dock.";
            if (IsBlueprintConsumed(productId) || !GoF2Remake.Data.Blueprints.IsUnlocked(productId))
                return "That ship blueprint has not been unlocked or has already been consumed in this world.";
            if (Modding.ModBlueprints.RequiresShipRefusal(productId, by.ShipIndex) is string refusal)
                return refusal;
            if (product.blueprint == null || product.blueprint.Count == 0)
                return "That ship blueprint has no valid construction recipe.";

            EnsureStorageLots(record);
            var required = new Dictionary<int, int>();
            foreach (var part in product.blueprint)
            {
                if (part == null || part.item < 0 || part.amount <= 0 || db.Item(part.item) == null)
                    return "The ship blueprint contains an invalid ingredient; nothing was consumed.";
                int previous = required.TryGetValue(part.item, out int old) ? old : 0;
                if (previous > int.MaxValue - part.amount)
                    return "The ship blueprint requires more than the supported quantity of one material; nothing was consumed.";
                required[part.item] = previous + part.amount;
            }
            foreach (var pair in required)
            {
                var stack = record.storage.Find(x => x != null && x.item == pair.Key);
                if (stack == null || stack.amount < pair.Value || OwnedStorageAmount(record, builderAccount, pair.Key) < pair.Value)
                    return "Insufficient personally owned " + (db.Item(pair.Key)?.name ?? ("item " + pair.Key)) +
                        " in station storage; required: " + pair.Value + ".";
            }

            var previousStorage = new List<PlayerStationStorage>();
            foreach (var entry in record.storage)
                if (entry != null) previousStorage.Add(new PlayerStationStorage { item = entry.item, amount = entry.amount });
            var previousLots = CloneStorageLots(record);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            int duration = DryDockBuildDuration(productId, shipIndex);
            var definition = Modding.ModBlueprints.Of(productId);
            bool consumeBlueprint = definition != null && definition.consumeBlueprint;
            foreach (var pair in required)
            {
                var stack = record.storage.Find(x => x != null && x.item == pair.Key);
                RemoveStorageLots(record, builderAccount, pair.Key, pair.Value);
                stack.amount -= pair.Value;
            }
            record.storage.RemoveAll(x => x == null || x.amount <= 0);
            record.dryDockBuilding = true;
            record.dryDockBlueprintItem = productId;
            record.dryDockShip = shipIndex;
            record.dryDockStartedUtc = now;
            record.dryDockCompletesUtc = now + duration;
            record.dryDockConsumeBlueprint = consumeBlueprint;
            record.dryDockBuilderProfile = builderAccount;
            if (consumeBlueprint)
            {
                registry.consumedBlueprints ??= new List<int>();
                if (!registry.consumedBlueprints.Contains(productId)) registry.consumedBlueprints.Add(productId);
            }
            if (!Save())
            {
                record.storage = previousStorage;
                record.storageLots = previousLots;
                ClearDryDockJob(record);
                if (consumeBlueprint) registry?.consumedBlueprints?.Remove(productId);
                return "The dry-dock job could not be saved; all materials and the blueprint were restored.";
            }
            if (consumeBlueprint) GoF2Remake.Data.Session.UnlockedBlueprints.Remove(productId);
            return "Dry dock construction started for " + db.Ship(shipIndex).name + " at " + record.name +
                ". Estimated duration: " + duration + " seconds. The recipe has been reserved from station storage" +
                (consumeBlueprint ? "; the blueprint was consumed." : "; the blueprint remains unlocked.") +
                " Check /station drydock <id> for progress.";
        }

        // GoF2's paired ore/core commodities. A refinery converts ten units of raw ore into one core.
        static readonly int[,] RefineryRecipes =
        {
            { 154, 165 }, { 155, 166 }, { 156, 167 }, { 157, 168 },
            { 158, 169 }, { 159, 170 }, { 160, 171 }, { 161, 172 },
            { 162, 173 }, { 163, 174 }, { 164, 175 }, { 217, 218 },
        };

        sealed class FactoryRecipe
        {
            public int id, output, outputAmount;
            public (int item, int amount)[] inputs;
        }

        // Built-in starter recipes. Mods can register any validated item output through RegisterFactoryRecipe.
        // Recipe identifiers are stable command-facing IDs; item IDs are resolved against the active database.
        static readonly List<FactoryRecipe> BuiltInFactoryRecipes = new List<FactoryRecipe>
        {
            new FactoryRecipe { id = 1, output = 120, outputAmount = 1, inputs = new[] { (119, 2) } }, // Plastics
            new FactoryRecipe { id = 2, output = 125, outputAmount = 1, inputs = new[] { (156, 2), (155, 1) } }, // Mechanical Supplies
            new FactoryRecipe { id = 3, output = 129, outputAmount = 1, inputs = new[] { (118, 1), (126, 1) } }, // Optics
            new FactoryRecipe { id = 4, output = 128, outputAmount = 1, inputs = new[] { (118, 1), (127, 1) } }, // Com. Devices
            new FactoryRecipe { id = 101, output = 98, outputAmount = 1, inputs = new[] { (97, 1), (119, 1) } }, // Medical Supplies
            new FactoryRecipe { id = 102, output = 114, outputAmount = 1, inputs = new[] { (119, 2), (126, 1) } }, // Explosives
            new FactoryRecipe { id = 103, output = 118, outputAmount = 1, inputs = new[] { (127, 1), (120, 1) } }, // Electronics
            new FactoryRecipe { id = 104, output = 121, outputAmount = 1, inputs = new[] { (118, 1), (127, 1), (119, 1) } }, // Nanotech
            new FactoryRecipe { id = 105, output = 122, outputAmount = 1, inputs = new[] { (125, 1), (124, 1), (118, 1) } }, // Energy Cells
            new FactoryRecipe { id = 106, output = 130, outputAmount = 1, inputs = new[] { (156, 1), (119, 1) } }, // Hydraulics
        };

        static readonly List<FactoryRecipe> RegisteredFactoryRecipes = new List<FactoryRecipe>();

        /// <summary>Register a recipe from a mod or server extension. IDs are stable command-facing identifiers.</summary>
        public static bool RegisterFactoryRecipe(int id, int output, int outputAmount,
            params (int item, int amount)[] inputs)
        {
            if (id <= 0 || outputAmount <= 0 || !NetGuard.Item(output) || NetGame.Db?.Item(output) == null ||
                inputs == null || inputs.Length == 0 || BuiltInFactoryRecipes.Exists(recipe => recipe.id == id) ||
                RegisteredFactoryRecipes.Exists(recipe => recipe.id == id)) return false;
            var validated = new List<(int item, int amount)>();
            foreach (var input in inputs)
            {
                if (!NetGuard.Item(input.item) || NetGame.Db?.Item(input.item) == null ||
                    input.amount <= 0 || validated.Exists(x => x.item == input.item)) return false;
                validated.Add(input);
            }
            economyRecipes.Remove(id);
            RegisteredFactoryRecipes.Add(new FactoryRecipe { id = id, output = output, outputAmount = outputAmount, inputs = validated.ToArray() });
            return true;
        }

        internal static string InstallFactory(NetPlayer by, int id) => InstallModuleFromCargo(by, id, "factory");

        internal static string FactoryRecipeList()
        {
            var lines = new List<string> { "Factory recipes (one batch each):" };
            foreach (var recipe in EffectiveFactoryRecipes())
            {
                string output = NetGame.Db?.Item(recipe.output)?.name ?? ("Item " + recipe.output);
                var inputs = new List<string>();
                foreach (var input in recipe.inputs)
                    inputs.Add(input.amount + " x " + (NetGame.Db?.Item(input.item)?.name ?? ("Item " + input.item)));
                lines.Add("#" + recipe.id + ": " + string.Join(" + ", inputs) + " -> " +
                    recipe.outputAmount + " x " + output);
            }
            lines.Add("Produce with /station manufacture <station id> <recipe id> <batches> (1–100); each batch takes " +
                FactorySecondsPerBatch + " seconds.");
            lines.Add("Owner automation: /station repeat <station id> <recipe id> continuous|<total batches> [batches per cycle].");
            lines.Add("Cancel automation: /station repeat-cancel <station id> <order id>.");
            return string.Join("\n", lines);
        }

        static FactoryRecipe FindFactoryRecipe(int recipeId)
        {
            if (economyRecipes.TryGetValue(recipeId, out var recipe)) return recipe;
            return BuiltInFactoryRecipes.Find(candidate => candidate.id == recipeId) ??
                RegisteredFactoryRecipes.Find(candidate => candidate.id == recipeId);
        }

        static bool HasFactoryModule(PlayerStationRecord record) =>
            record?.addons?.Exists(a => a != null && a.kind == "factory" && a.level > 0) == true;

        static string StartFactoryJob(PlayerStationRecord record, string account, FactoryRecipe recipe,
            int batches, int repeatOrderId)
        {
            if (record == null || recipe == null) return "Unknown factory recipe.";
            if (record.underConstruction) return "Finish station construction before manufacturing.";
            if (!HasFactoryModule(record)) return "Install a factory module first.";
            int capacity = StorageCapacity(record);
            if (capacity <= 0) return "The station needs a storage module for factory inputs and output.";
            if (batches < 1 || batches > 100) return "Manufacture between 1 and 100 batches at a time.";
            record.factoryJobs ??= new List<PlayerFactoryProductionJob>();
            if (record.factoryJobs.Count > 0) return "The factory is already processing a production job.";
            if (!NetGuard.Item(recipe.output) || NetGame.Db?.Item(recipe.output) == null)
                return "The recipe output is unavailable in this game's item database.";
            EnsureStorageLots(record);

            var inputStacks = new List<(PlayerStationStorage stack, int amount)>();
            foreach (var input in recipe.inputs)
            {
                if (input.amount <= 0 || input.amount > int.MaxValue / batches)
                    return "That batch count exceeds the supported input quantity.";
                int required = input.amount * batches;
                var stack = record.storage.Find(x => x != null && x.item == input.item);
                if (stack == null || stack.amount < required || OwnedStorageAmount(record, account, input.item) < required)
                    return "Insufficient personally owned " + (NetGame.Db?.Item(input.item)?.name ?? ("item " + input.item)) +
                        " in station storage; required: " + required + ".";
                inputStacks.Add((stack, required));
            }

            if (recipe.outputAmount <= 0 || recipe.outputAmount > int.MaxValue / batches)
                return "That batch count exceeds the supported output quantity.";
            int produced = recipe.outputAmount * batches;
            var outputStack = record.storage.Find(x => x != null && x.item == recipe.output);
            if (outputStack != null && outputStack.amount > int.MaxValue - produced)
                return "The factory output stack has reached its maximum supported quantity.";
            if (outputStack == null && record.storage.Count >= MaxStorageItemTypes)
                return "Storage has reached its item-type limit.";

            long stored = 0;
            foreach (var stack in record.storage)
                if (stack != null) stored += Math.Max(0, stack.amount);
            long consumed = 0;
            foreach (var input in inputStacks) consumed += input.amount;
            if (stored - consumed + produced > capacity)
                return "Station storage has insufficient room reserved for the factory output.";

            var previousStorage = new List<PlayerStationStorage>();
            foreach (var stack in record.storage)
                if (stack != null) previousStorage.Add(new PlayerStationStorage { item = stack.item, amount = stack.amount });
            var previousLots = CloneStorageLots(record);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var job = new PlayerFactoryProductionJob
            {
                recipeId = recipe.id, batches = batches, repeatOrderId = repeatOrderId,
                operatorProfile = account, startedUtc = now,
                completesUtc = now + (long)FactorySecondsPerBatch * batches,
                outputItem = recipe.output, outputAmount = produced,
                reservedInputs = new List<PlayerStationMaterial>()
            };
            foreach (var input in inputStacks)
            {
                RemoveStorageLots(record, account, input.stack.item, input.amount);
                input.stack.amount -= input.amount;
                job.reservedInputs.Add(new PlayerStationMaterial { item = input.stack.item, required = input.amount, delivered = input.amount });
                if (input.stack.amount == 0) record.storage.Remove(input.stack);
            }
            record.factoryJobs.Add(job);
            if (!Save())
            {
                record.storage = previousStorage;
                record.storageLots = previousLots;
                record.factoryJobs.Remove(job);
                return "The factory job could not be saved; all input materials remain in storage.";
            }
            return "Factory job started: " + batches + " batch(es), " + (long)FactorySecondsPerBatch * batches +
                " seconds. Inputs have been reserved; output will be added when the job completes.";
        }

        internal static string Manufacture(NetPlayer by, int id, int recipeId, int batches)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at this station with a controlled profile to operate its factory.";
            return StartFactoryJob(record, account, FindFactoryRecipe(recipeId), batches, -1);
        }

        internal static string FactoryStatus(NetPlayer by, int id)
        {
            var station = Find(id);
            if (!IsStationContext(by, station, out string account) ||
                !HasStationPermission(station, account, PlayerStationPermission.StationOperations))
                return "Dock here with station operations permission to view the factory queue.";
            if (!HasFactoryModule(station)) return "This station has no factory.";
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var lines = new List<string>();
            foreach (var job in station.factoryJobs ?? new List<PlayerFactoryProductionJob>())
                if (job != null)
                    lines.Add($"Recipe #{job.recipeId} · {job.batches:N0} batches · {Math.Max(0, job.completesUtc - now):N0}s remaining");
            foreach (var order in station.factoryRepeatOrders ?? new List<PlayerFactoryRepeatOrder>())
            {
                if (order == null) continue;
                string quantity = order.continuous ? "continuous" : $"{order.remainingBatches:N0} remaining";
                lines.Add($"Repeat #{order.id} · recipe #{order.recipeId} · {quantity}" + (order.paused ? " · paused" : ""));
            }
            return lines.Count > 0 ? string.Join("\n", lines) : "The factory queue is empty.";
        }

        internal static string SetFactoryRepeatOrder(NetPlayer by, int id, int recipeId,
            bool continuous, int totalBatches, int batchesPerCycle)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at the station using a controlled profile to configure repeat orders.";
            bool automationAuthority = record.ownershipType == PlayerStationOwnershipType.Individual
                ? string.Equals(record.ownerProfile, account, StringComparison.Ordinal)
                : HasStationPermission(record, account, PlayerStationPermission.StationOperations) &&
                    string.Equals(NetFactions.Of(account)?.id, record.ownerFactionId, StringComparison.Ordinal);
            if (!automationAuthority)
                return "Repeat orders are restricted to the individual station owner or authorised members of its owning faction.";
            if (!HasFactoryModule(record) || record.underConstruction)
                return "An operational factory module is required.";
            if (FindFactoryRecipe(recipeId) == null) return "Unknown recipe. Use /station recipes to list available recipes.";
            if (batchesPerCycle < 1 || batchesPerCycle > 100)
                return "Each repeat cycle must contain between 1 and 100 batches.";
            if (continuous ? totalBatches != 0 : totalBatches < 1 || totalBatches > 100000)
                return "Choose continuous, or a fixed total between 1 and 100,000 batches.";
            record.factoryRepeatOrders ??= new List<PlayerFactoryRepeatOrder>();
            if (record.factoryRepeatOrders.Count >= 32) return "This factory already has 32 repeat orders.";
            int orderId = Math.Max(1, record.nextFactoryRepeatOrderId);
            if (orderId == int.MaxValue) return "The factory repeat-order ID range is exhausted.";
            var order = new PlayerFactoryRepeatOrder
            {
                id = orderId, recipeId = recipeId, continuous = continuous,
                remainingBatches = continuous ? 0 : totalBatches,
                batchesPerCycle = batchesPerCycle, createdByProfile = account,
                createdUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            record.factoryRepeatOrders.Add(order);
            record.nextFactoryRepeatOrderId = orderId + 1;
            if (!Save())
            {
                record.factoryRepeatOrders.Remove(order);
                record.nextFactoryRepeatOrderId = orderId;
                return "The repeat order could not be saved.";
            }
            return "Factory repeat order #" + orderId + " created (" +
                (continuous ? "continuous" : totalBatches + " total batches") + ").";
        }

        internal static string CancelFactoryRepeatOrder(NetPlayer by, int id, int orderId)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!IsStationContext(by, record, out string account))
                return "Dock at the station using a controlled profile to cancel repeat orders.";
            bool automationAuthority = record.ownershipType == PlayerStationOwnershipType.Individual
                ? string.Equals(record.ownerProfile, account, StringComparison.Ordinal)
                : HasStationPermission(record, account, PlayerStationPermission.StationOperations) &&
                    string.Equals(NetFactions.Of(account)?.id, record.ownerFactionId, StringComparison.Ordinal);
            if (!automationAuthority)
                return "Repeat orders are restricted to the individual station owner or authorised members of its owning faction.";
            var order = record.factoryRepeatOrders?.Find(x => x != null && x.id == orderId);
            if (order == null) return "No repeat order with that ID exists.";
            int index = record.factoryRepeatOrders.IndexOf(order);
            record.factoryRepeatOrders.RemoveAt(index);
            if (!Save())
            {
                record.factoryRepeatOrders.Insert(index, order);
                return "The repeat-order cancellation could not be saved.";
            }
            return "Factory repeat order #" + orderId + " cancelled. Any active production batch will finish.";
        }

        static void TickFactoryJobs(long now)
        {
            if (registry?.stations == null) return;
            foreach (var record in registry.stations)
            {
                if (record == null || record.offeredForClaimUtc > 0 || !HasFactoryModule(record) || record.underConstruction) continue;
                record.storage ??= new List<PlayerStationStorage>();
                record.factoryJobs ??= new List<PlayerFactoryProductionJob>();
                record.factoryRepeatOrders ??= new List<PlayerFactoryRepeatOrder>();
                if (record.factoryJobs.Count > 0)
                {
                    var job = record.factoryJobs[0];
                    if (job == null) { record.factoryJobs.RemoveAt(0); Save(); continue; }
                    if (job.completesUtc > now) continue;
                    EnsureStorageLots(record);
                    var output = record.storage.Find(x => x != null && x.item == job.outputItem);
                    var previousLots = CloneStorageLots(record);
                    long stored = 0;
                    foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
                    if (stored + job.outputAmount > StorageCapacity(record) ||
                        (output == null && record.storage.Count >= MaxStorageItemTypes) ||
                        (output != null && output.amount > int.MaxValue - job.outputAmount)) continue;

                    int oldAmount = output?.amount ?? 0;
                    bool addedStack = false;
                    if (output == null)
                    {
                        record.storage.Add(output = new PlayerStationStorage { item = job.outputItem });
                        addedStack = true;
                    }
                    output.amount += job.outputAmount;
                    AddStorageLot(record, job.operatorProfile, job.outputItem, job.outputAmount);
                    record.factoryJobs.RemoveAt(0);
                    var order = job.repeatOrderId > 0
                        ? record.factoryRepeatOrders.Find(x => x != null && x.id == job.repeatOrderId) : null;
                    int previousRemaining = order?.remainingBatches ?? 0;
                    if (order != null && !order.continuous)
                    {
                        order.remainingBatches = Math.Max(0, order.remainingBatches - job.batches);
                        if (order.remainingBatches == 0) record.factoryRepeatOrders.Remove(order);
                    }
                    if (!Save())
                    {
                        if (addedStack) record.storage.Remove(output);
                        else output.amount = oldAmount;
                        record.storageLots = previousLots;
                        record.factoryJobs.Insert(0, job);
                        if (order != null)
                        {
                            order.remainingBatches = previousRemaining;
                            if (!record.factoryRepeatOrders.Contains(order)) record.factoryRepeatOrders.Add(order);
                        }
                        continue;
                    }
                }

                if (record.factoryJobs.Count != 0) continue;
                bool resumed = false;
                foreach (var paused in record.factoryRepeatOrders)
                {
                    if (paused == null || !paused.paused) continue;
                    var pausedRecipe = FindFactoryRecipe(paused.recipeId);
                    if (pausedRecipe == null || !NetGuard.Item(pausedRecipe.output) ||
                        NetGame.Db?.Item(pausedRecipe.output) == null) continue;
                    int resumeBatches = Math.Min(100, Math.Max(1, paused.batchesPerCycle));
                    if (!paused.continuous) resumeBatches = Math.Min(resumeBatches, Math.Max(0, paused.remainingBatches));
                    bool hasInputs = resumeBatches > 0;
                    long consumed = 0;
                    if (hasInputs)
                        foreach (var input in pausedRecipe.inputs)
                        {
                            if (input.amount <= 0 || input.amount > int.MaxValue / resumeBatches ||
                                OwnedStorageAmount(record, paused.createdByProfile, input.item) < input.amount * resumeBatches)
                            { hasInputs = false; break; }
                            consumed += (long)input.amount * resumeBatches;
                        }
                    long produced = (long)pausedRecipe.outputAmount * resumeBatches;
                    var pausedOutput = record.storage.Find(x => x != null && x.item == pausedRecipe.output);
                    long storedNow = 0;
                    foreach (var stack in record.storage) if (stack != null) storedNow += Math.Max(0, stack.amount);
                    if (produced <= 0 || produced > int.MaxValue ||
                        (pausedOutput == null && record.storage.Count >= MaxStorageItemTypes) ||
                        (pausedOutput != null && pausedOutput.amount > int.MaxValue - produced) ||
                        storedNow - consumed + produced > StorageCapacity(record))
                        hasInputs = false;
                    if (hasInputs) { paused.paused = false; resumed = true; }
                }
                if (resumed) Save();
                var next = record.factoryRepeatOrders.Find(x => x != null && !x.paused);
                if (next == null) continue;
                var recipe = FindFactoryRecipe(next.recipeId);
                if (recipe == null) { next.paused = true; Save(); continue; }
                int batches = Math.Min(100, Math.Max(1, next.batchesPerCycle));
                if (!next.continuous) batches = Math.Min(batches, Math.Max(0, next.remainingBatches));
                if (batches <= 0) { record.factoryRepeatOrders.Remove(next); Save(); continue; }
                string result = StartFactoryJob(record, next.createdByProfile, recipe, batches, next.id);
                if (!result.StartsWith("Factory job started:", StringComparison.Ordinal))
                {
                    next.paused = true;
                    Save();
                }
            }
        }

        internal static string InstallRefinery(NetPlayer by, int id) => InstallModuleFromCargo(by, id, "refinery");

        internal static string Refine(NetPlayer by, int id, int oreItem, int amount)
        {
            var record = Find(id);
            if (record == null) return "No player station with that id exists.";
            if (!CanManage(by, record, out string account))
                return "Dock at this station and use its controlling profile to operate the refinery.";
            if (record.underConstruction) return "Finish station construction before refining materials.";
            if (!(record.addons?.Exists(a => a != null && a.kind == "refinery" && a.level > 0) ?? false))
                return "Install a refinery module first.";
            if (StorageCapacity(record) <= 0) return "The station needs a storage module for refinery input and output.";
            if (amount < 10 || amount > 500 || amount % 10 != 0)
                return "Refine between 10 and 500 ore, in multiples of 10.";

            int outputItem = -1;
            for (int i = 0; i < RefineryRecipes.GetLength(0); i++)
                if (RefineryRecipes[i, 0] == oreItem) { outputItem = RefineryRecipes[i, 1]; break; }
            if (outputItem < 0) return "That commodity cannot be refined into a core.";

            EnsureStorageLots(record);
            var input = record.storage.Find(x => x != null && x.item == oreItem);
            if (input == null || input.amount < amount || OwnedStorageAmount(record, account, oreItem) < amount) return "Your profile does not own enough of that ore in station storage.";
            int batches = amount / 10;
            var output = record.storage.Find(x => x != null && x.item == outputItem);
            long stored = 0;
            foreach (var stack in record.storage) if (stack != null) stored += Math.Max(0, stack.amount);
            if (output != null && output.amount > int.MaxValue - batches)
                return "The refined core stack has reached its maximum supported quantity.";
            // Ten units become one: refining frees nine units of capacity per batch.
            if (output == null && record.storage.Count >= MaxStorageItemTypes)
                return "Storage has reached its item-type limit.";
            if (stored - amount + batches > StorageCapacity(record))
                return "Station storage has insufficient room for the refined cores.";

            var previousLots = CloneStorageLots(record);
            RemoveStorageLots(record, account, oreItem, amount);
            input.amount -= amount;
            if (input.amount == 0) record.storage.Remove(input);
            if (output == null) record.storage.Add(output = new PlayerStationStorage { item = outputItem });
            output.amount += batches;
            AddStorageLot(record, account, outputItem, batches);
            if (!Save())
            {
                output.amount -= batches;
                record.storageLots = previousLots;
                if (output.amount == 0) record.storage.Remove(output);
                if (input.amount == 0) record.storage.Add(input);
                input.amount += amount;
                return "The refining operation could not be saved; the input ore remains in storage.";
            }
            string oreName = NetGame.Db?.Item(oreItem)?.name ?? ("Item " + oreItem);
            string coreName = NetGame.Db?.Item(outputItem)?.name ?? ("Item " + outputItem);
            return "Refined " + amount + " " + oreName + " into " + batches + " " + coreName + " at " + record.name + ".";
        }
    }
}
