using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Multiplayer;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed partial class StationManagementWindow
    {
        void BuildListingForm(string id)
        {
            if (listingItem < 0)
            {
                AddCatalogueChoice("marketFleetIndex", "Personal stored hull", "fleet");
                AddNumber("marketHullPrice", "Hull price (credits)", 10000, 1, 1000000000);
                if (!string.IsNullOrEmpty(Value("marketFleetIndex")))
                    AddAction("List selected hull", () => Send("/station list-ship " + id + " " + Value("marketFleetIndex") + " " + Value("marketHullPrice")));
                return;
            }
            var item = NetGame.Db?.Item(listingItem);
            int available = Shop.CargoOf(listingItem);
            if (item == null || available <= 0) { AddInfo("Cargo", "This item is no longer in your cargo hold."); return; }
            AddInfo("Item", item.name);
            AddNumber("marketAmount", "Units", 1, 1, Math.Min(500, available));
            AddNumber("marketPrice", "Price per unit (credits)", Math.Max(1, item.minPrice), 1, 1000000000);
            AddAction("List from cargo", () => Send($"/station list-cargo {id} {listingItem} {Value("marketAmount")} {Value("marketPrice")}"));
        }

        void BuildMarketSettings(string id)
        {
            if (managementCatalogue?.canManageMarket == true)
            {
                AddSection("MARKETPLACE TAX", "");
                AddNumber("salesTax", "Sales tax (%)", selected.marketplaceTaxPercent, 0, 10);
                AddAction("Set marketplace tax", () => Send("/station tax " + id + " " + Value("salesTax")));
                if (Modding.ModAdvancedEconomy.PlayerMarketTrade)
                {
                    AddSection("NPC MARKET PROCUREMENT", "Allows budgeted NPC purchases from public commodity listings at your listed prices. Private storage is untouched.");
                    AddInfo("NPC purchases", selected.npcEconomyPurchases ? "Enabled" : "Disabled");
                    AddAction(selected.npcEconomyPurchases ? "Disable NPC purchases" : "Enable NPC purchases",
                        () => Send("/station npc-market " + id + (selected.npcEconomyPurchases ? " off" : " on")));
                }
            }
            AddDirectoryPager();
            AddAccountField("marketPilot", "Pilot");
            AddAction("View pilot marketplace rules", "/station pilot-market-rules " + id);
            if (managementCatalogue?.canSecurity == true || managementCatalogue?.canOwnership == true)
            {
                AddSection("PILOT MARKETPLACE ACCESS", "");
                AddField("marketAccessReason", "Reason for denying access", "");
                AddAction("Deny marketplace access", () => SendWithReason(
                    $"/station pilot-market-access {id} {Value("marketPilot")} deny", "marketAccessReason"));
                AddAction("Restore inherited access", () => Send($"/station pilot-market-access {id} {Value("marketPilot")} inherit"));
            }
            if (managementCatalogue?.canManageMarket != true) return;
            AddSection("PILOT TAX", "");
            AddChoice("marketPilotOperation", "Transaction", new List<(string, string)> { ("purchase", "Purchase"), ("sale", "Sale") },
                _ => drafts.Remove("marketPilotPercent"));
            int limit = NetPlayerStations.PilotMarketLimit;
            AddNumber("marketPilotPercent", "Percentage", 0, Value("marketPilotOperation") == "purchase" ? -limit : 0, limit);
            AddField("marketPilotReason", "Reason (optional)", "");
            string Command() => $"/station pilot-tax {id} {Value("marketPilot")} {Value("marketPilotOperation")}";
            AddAction("Apply pilot tax", () =>
            {
                string percent = Value("marketPilotPercent");
                SendWithOptionalReason(Command() + " " + percent, "marketPilotReason");
            });
            AddAction("Remove pilot tax override", () => Send(Command() + " inherit"));
            if (Modding.ModPlayerInfrastructure.Current.StationRacePoliciesEnabled)
                AddAction("Race policies", () => ChangeManagementPage("race-policies"));
        }

        void BuildStationBank(string id)
        {
            AddSection("STATION BANK", "Independent station funds. Marketplace tax revenue and voluntary credit donations are deposited here.");
            if (managementCatalogue?.canManageMarket == true || managementCatalogue?.canOwnership == true)
                AddInfo("Balance", managementCatalogue.stationBankCredits.ToString("N0") + " credits");
            long available = Math.Min(int.MaxValue, (long)Session.Credits);
            AddInfo("Your credits", available.ToString("N0"));
            if (available > 0)
            {
                AddNumber("bankDonation", "Credits to donate", Math.Min(1000L, available), 1, available);
                AddAction("Donate credits to station bank", () => Send("/station bank-donate " + id + " " + Value("bankDonation")));
            }
            if (managementCatalogue?.canManageMarket != true) return;
            AddSection("WITHDRAW CREDITS", "Transfer station funds to your personal credit balance. Withdrawals are recorded in the server audit log.");
            long withdrawable = Math.Min((long)int.MaxValue, managementCatalogue.stationBankCredits);
            if (withdrawable <= 0) { AddInfo("Withdrawal", "No funds available."); return; }
            AddNumber("bankWithdrawal", "Credits to withdraw", Math.Min(1000L, withdrawable), 1, withdrawable);
            AddAction("Withdraw credits from station bank", () => Send("/station bank-withdraw " + id + " " + Value("bankWithdrawal")));
        }

    }
}
