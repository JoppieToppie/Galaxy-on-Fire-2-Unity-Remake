# Player station economy

Persistent hosts and dedicated servers can place an optional `station-economy.json` next to `player-stations.json` in the world's profile folder. Restart the hosted world to apply edits. Each new project saves its material requirements when it starts. Existing projects retain their saved costs and running factory jobs retain their scheduled output.

```json
{
  "version": 1,
  "constructionCosts": [
    {
      "project": "jumpgate",
      "materials": [
        { "item": 156, "amount": 500 },
        { "item": 155, "amount": 250 },
        { "item": 118, "amount": 200 },
        { "item": 127, "amount": 150 }
      ]
    }
  ],
  "factoryRecipes": [
    {
      "id": 1,
      "output": 120,
      "outputAmount": 1,
      "inputs": [{ "item": 119, "amount": 2 }]
    }
  ]
}
```

- Cost overrides replace the full material list for the named project. Valid names are `station`, `reconstruction`, `jumpgate`, `storage`, `refinery`, `factory`, `hangar`, and `drydock`. Without a reconstruction override, its cost is calculated from the current station cost using the usual rebuild discount.
- A factory recipe with a built-in ID replaces that recipe. A new ID adds a recipe. Recipes registered by an extension keep their registered definition.
- Use item IDs from the server's active catalogue and unique items within each material list. Quantities must be integers from 1 to 100,000. The server logs the reason for discarding an invalid file and uses the defaults.
- Pilots can look up the active values with `/station modules`, `/station jumpgate-cost`, and `/station recipes`.

## Module purchase orders

The builder or authorised construction manager opens a project while docked at the station. Each station can commission one module at a time through buy orders. The individual owner or owning faction's leader or an officer can fund orders. Suppliers deliver while docked at the commissioning station. The last delivery installs the module and any unused escrow returns to the original funders. To finish with cargo, cancel open orders first. Delivered materials count toward the remaining cost.

## Public claim and orbit projects

An eligible owner can confirm retirement from Station Management. The next pilot who claims it pays 25 million credits. Private facilities and sponsored gate construction and management pause during the listing. Operational sponsored gates continue using their existing travel rules. The claim restores the station and resumes gate timers from the time remaining at retirement.

In flight, open Multiplayer and choose **Orbit projects** to see wreck claims, start an eligible reconstruction with a same-race model, or deliver materials. The eligible sponsor owner or faction member can allocate donated stock while in a gate's construction orbit.
