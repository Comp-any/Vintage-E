# Telling Vintage-E about your mod's crafting methods

Vintage-E works out every item's EMC value from the recipes it can read: the crafting grid, every recipe list a mod
system holds whose type is the game's `IRecipeBase`, alloys, and the game's own item properties (smelting, grinding,
crushing, drying, the barrel, the fruit press and so on). A machine whose recipes live anywhere else (in a block's
attributes, in a folder of your own recipe files, in code) is invisible to it, so what the machine makes is valued some
other way or guessed.

A **method document** tells Vintage-E about such a machine or process: what each recipe uses and makes (with chances),
the tools it wears out, and what else it costs: power, heat, machine time or flat EMC. Vintage-E charges those costs into
the value of what's made, and explains every number in `/emc dump` and the doctor (`/emc diagnose`).

**Format version: 1** (Vintage-E 1.23.0 and later; the keys marked *1.23.1* below need 1.23.1). A document says which
format it is written in; a newer format than a Vintage-E reads is left out with a message to update. Format 1 can still
gain keys until it is declared stable; a document that works now keeps working.

For an editor that checks documents as you type, point `"$schema"` at
[`crafting-methods.schema.json`](crafting-methods.schema.json) (a JSON schema of every key below).

Neither way of sending a document needs a dependency on Vintage-E: without it, the file is never read and the event
never fires.

## Two ways to send one

### 1. A file

Put the document in your mod at `assets/<yourmodid>/config/vintagee-methods/<any name>.json`. Data only, no code. Codes
without a domain are your mod's (as in the game's own asset files), except in recipes a reader makes: there they get the
domain of the data read (below).

### 2. From your code, with no reference to VintageE.dll

When Vintage-E collects recipes (at server start, after `/emc recalc`, and when asked again), it pushes the event
`vintagee:collectmethods` on the game's event bus with a `TreeAttribute`. Put your document in it as a string, under your
mod's id:

```csharp
public override void StartServerSide(ICoreServerAPI api)
{
    api.Event.RegisterEventBusListener(OnVintageEMethods, 0.5, "vintagee:collectmethods");
}

void OnVintageEMethods(string eventName, ref EnumHandling handling, IAttribute data)
{
    if (data is ITreeAttribute tree) tree.SetString(Mod.Info.ModID, BuildMethodDocument());   // the JSON below, as text
}
```

- The tree also holds `format` (the newest format this Vintage-E reads) and `vintageeVersion`; leave those alone.
- Don't set `handling` to `PreventSubsequent`: the mods after you would not be asked.
- Don't throw: the game's event bus stops at a listener that throws, and the mods after it are missed (the doctor names
  you).
- Several documents: use keys like `yourmodid:press` and `yourmodid:mixer`; the part before `:` or `/` is the mod.
- If your recipes change while the server runs (a config reload), push `vintagee:methodschanged`, ideally with your
  mod id as the data: `api.Event.PushEvent("vintagee:methodschanged", new StringAttribute(Mod.Info.ModID))`. Vintage-E
  collects again and works the values out again a second later (a burst of pushes is one remap).

Register the listener in `StartServerSide` (or earlier). Vintage-E collects when the game reaches its RunGame phase,
after every mod has started.

### 3. The server's own (*1.23.1*)

A server owner (or you, while writing a document) can put documents in `VintagestoryData/ModConfig/vintagee-methods/`.
They are read again on every collect, so `/emc methods reload` picks up an edit without a restart. Codes without a
domain are the game's there, unless the document says `"domain": "yourmodid"`.

- A method or energy unit in a server document **replaces a mod's with the same code**: that's how a server changes a
  mod's numbers. `/emc methods copy <modid>` writes a copy of the mod's documents there to edit (delete it to go back);
  `supersedes` entries the mod itself wasn't allowed are left out of the copy, so it changes nothing until edited.
- A server document's `supersedes` may name any recipe list: the server decides what counts there.
- For a mod author: put your draft there with `"domain": "yourmodid"`, edit, `/emc methods reload`, and check with
  `/emc diagnose methodcheck yourmodid`; then move it into your mod's assets.

## The format

```json
{
  "format": 1,
  "requiresMod": "mymod",
  "energyUnits": {
    "mymod:work": {
      "name": "kN·s",
      "emcPerUnit": 0.002,
      "generators": [ { "burns": "game:charcoal", "quantity": 1, "makes": 16000, "note": "the steam engine" } ]
    }
  },
  "methods": [
    {
      "code": "mymod:press",
      "name": "mymod:method-press",
      "machine": "block:mymod:press-*",
      "description": "Presses the item on the depot under it, with a die fitted.",
      "kind": "processing",
      "supersedes": [],
      "recipes": [
        {
          "code": "plate",
          "inputs":  [ { "code": "game:ingot-*", "name": "metal", "quantity": 1, "skipVariants": [ "lead" ] } ],
          "tools":   [ { "code": "mymod:die-plate", "wear": 1, "breakChance": "5%" } ],
          "outputs": [
            { "code": "game:metalplate-{metal}", "main": true },
            { "code": "game:metalbit-{metal}", "quantity": { "avg": 2, "var": 1 }, "chance": "25%" }
          ],
          "costs": { "energy": { "unit": "mymod:work", "amount": 400 }, "seconds": 4 },
          "note": "Needs the plate die."
        }
      ]
    }
  ]
}
```

Keys starting with `//` are comments and ignored anywhere. Any other unknown key is a warning (with a "did you mean"),
and is ignored.

### Top level

| Key | |
|---|---|
| `format` | `1`. Missing reads as 1 with a warning. |
| `name`, `description` | Optional, for people reading the file. |
| `requiresMod` | A mod id or a list: the document is only used when all of them are loaded. An entry `"a\|b"` is met by either mod (*1.23.1*; in Vintage-E's own and the server's documents, the one that's loaded names the mod the document is for). It also lets a method supersede those mods' recipe lists (below). |
| `domain` | The domain codes without one get (*1.23.1*; default: your mod's, or the game's in a server document). |
| `$schema` | Ignored: for editors. |
| `energyUnits` | Energy units your recipes use: code → unit (below). |
| `methods` | The list of methods. |

### A method

| Key | |
|---|---|
| `code` | Unique, `modid:name` (the domain is added when left out). |
| `name` | A lang key (looked up on the server) or plain text. Shown in explanations. |
| `machine` | The block or item that does it (wildcards allowed, `block:`/`item:` in front if you like; the domain is added after it), shown and linked in explanations. |
| `description` | A line about what it does. |
| `kind` | `crafting` (the default) or `processing`. Either way, an ingredient with no value can be worked out from the rest when it is the recipe's only unknown (the server's `InferValuesFromCrafting`, on by default), and a recipe with several outputs and none marked main shares backwards as below. A processing recipe with one ingredient also gives that ingredient what its output is worth when it has no value (as smelting does). |
| `supersedes` | Recipe lists Vintage-E already reads by itself that this method describes, by list name (as `/emc diagnose mapping` shows the sources, e.g. `MyModSystem.PressRecipes`) or by recipe class name (`PressRecipe` or its full name). Those free copies are left out, so the copy with the power cost is the one that counts. Only lists of your own mod, or of a mod in `requiresMod`, can be superseded: an entry naming the game's or another mod's list is refused (the doctor says so). A method switched off, or with no recipe read, supersedes nothing. |
| `recipes` | The list of recipes. |
| `readers` | Recipes read from your mod's own data instead of listed (below). |

### A recipe

| Key | |
|---|---|
| `code` | Its name inside the method (defaults to its position in the list, `#1`, `#2`…; a reader's recipes `r1#1`, `r1#2`… for the first reader). `DisabledMethods` and the doctor name it as `method/recipe`. |
| `inputs` | What it uses up (below). May be empty if the recipe has a cost or wears a tool (a pump: power → water). |
| `tools` | What it needs but doesn't use up. Costs nothing unless it has `wear` or `breakChance`. |
| `outputs` | What it makes. |
| `oneOf` | A weighted table: exactly one entry comes out per run. |
| `costs` | Its extra costs. |
| `note` | A line shown with the recipe in explanations. |
| `enabled` | `false` leaves it out (handy for readers: `"enabled": "$enabled"` copies the data's own flag). `methodcheck` counts these. A recipe switched off can still be copied with `like`, so it can serve as a template. |
| `like` | *1.23.1*: the code of an earlier recipe of the same method to copy; the keys given here replace its (and `costs` key by key: `"costs": { "seconds": 2 }` changes only the time). For an upgraded machine: `{ "code": "plate-fast", "like": "plate", "tools": [ the upgrade, no wear ], "costs": { "seconds": 2 } }` (the cheaper one counts). |
| `steps` | *1.23.1*: a process done in turns (sequenced assembly): a list of `{ "inputs", "tools", "costs", "repeat", "note" }`, added up into this recipe (each step's amounts × its `repeat`). The outputs stay on the recipe. A table for `seconds` or `emc` can only be in one place. |

`true` and `false` may also be written as strings (`"true"`), as readers copy them from data.

### Ingredients, tools and outputs

Inputs and outputs take the keys below. Tools take only `code`, `type`, `quantity` (*1.23.1*: how many are worn at once),
`name`, `wear`, `breakChance`, `allowedVariants`, `skipVariants` and `anyOf` (another key is an unknown-key warning).

**Any number** (*1.23.1*) may also be a formula, `"=2 * 30"` (+ - * / %, brackets, `min`, `max`, `round`, `floor`,
`ceil`, `abs`; in a reader's template also `$fields`, `count` and `default`, below), or a **table by variant**:
`{ "by": "metal", "values": { "iron": 600, "copper": 400 }, "default": 400 }` is the number for what the input named
`metal` matched; a variant with no number and no `default` is left out (the doctor says so). Tables work for amounts,
chances, weights, shares, wear, break chances and every cost.

| Key | |
|---|---|
| `code` | A code (`game:ingot-copper`), `item:`/`block:` in front if you like; inputs and tools may use wildcards (the cheapest valued match counts). Outputs can't be wildcards. |
| `type` | `item` or `block` (when a code could be both). |
| `quantity` | How many (default 1), or the game's `{ "avg": 3, "var": 1 }` (its average counts). |
| `litres` | For a liquid instead of `quantity`: converted with the liquid's own items per litre. |
| `name` (inputs, tools) | Names what this input's single `*` matched, so other codes in the recipe can use `{name}` (letters, digits, `_` and `-`; capitals count). One copy of the recipe is made for each match; a copy whose output (or other code) doesn't exist is skipped, as the game would. A `name` on a code with no `*` is ignored; two or more `*` with a name, two inputs with the same name, or more than 8 named inputs in one recipe leave the recipe out (each name multiplies its copies; at most 4000 in all). |
| `anyOf` (inputs, tools) | *1.23.1*: instead of a code, a list of alternatives, each with its own code, amount and keys ("1 rendered fat or 10 lard"): one copy of the recipe per choice, and the cheapest counts. An alternative whose code matches nothing (its mod isn't loaded) is left out. Past 32 combinations of choices in one recipe, each alternative is tried once with the other slots at their first; no `anyOf` inside another. |
| `allowedVariants`, `skipVariants` | Which values of the `*` count (as in the game's recipes). |
| `returns` (inputs) | What's handed back after the run (an empty bucket): its value is taken off. |
| `chance` | `0.25` or `"25%"`. For an output: how often it comes. For an input: how often it is used up (below 10%, an input is never worked out backwards from the recipe, like a tool's wear). |
| `main` (outputs) | Marks the output the recipe is for (see below). |
| `weight` (oneOf) | The entry's weight in the table. |
| `share` (outputs, oneOf) | *1.23.1*: its weight when the cost is shared out (default 1 each, so evenly). |
| `waitForValue` (outputs, oneOf, returns) | *1.23.1*: `false` = as a byproduct with no value yet it counts as nothing, instead of the recipe waiting for one (only when costing forwards: an ingredient is never worked out backwards from the recipe until it has a value). |
| `wear` (tools) | Durability lost per run. |
| `breakChance` (tools) | How often it breaks (`"5%"`). |

### Costs

| Key | |
|---|---|
| `energy` | `{ "unit": "mymod:work", "amount": 400 }`, or a list of them. |
| `heat` | `{ "temperature": 900, "seconds": 30 }`, or a list of them (*1.23.1*): priced at what fuel for that fire would cost (charcoal and coal's value per °C·s of burning). |
| `seconds` | How long one run takes. Free unless the server sets `MethodEmcPerSecond`, but the doctor uses it to check for free-power farms, so please give it with every powered recipe. |
| `emc` | A flat EMC amount. |

### An energy unit

| Key | |
|---|---|
| `name` | How it is shown ("MJ", "kN·s"). |
| `emcPerUnit` | Your suggested price in EMC per unit. |
| `generators` | What your own engines burn to make it: `{ "burns": code, "quantity": 1, "makes": 16000, "note": "..." }`. A fuel with no base value is priced as fuel (its burn temperature × time at the heat rate). |
| `onlyFromFuel` | *1.23.1*: `true` when nothing makes this energy for free (no windmills, water wheels or solar): the free-power check leaves it alone. |

## How it is valued

- **Everything counts as its average.** An output at 25% counts as 0.25 per run, `{ "avg": 2 }` as 2, a `oneOf` entry
  as its share of the weights, an input with a chance as that fraction of it, a tool as wear ÷ its durability plus its
  break chance. So if 1 ingot gives a gear 25% of the time, one gear is worth 4 ingots, and burning what comes out never
  earns more than went in.
- **Main output and byproducts.** With a single output in all (`outputs` and `oneOf` together), or with one marked
  `"main": true`, the recipe values that output; the other outputs' values (and the `oneOf` entries') are taken off, like a bucket handed back, and
  the recipe waits until they have values (unless one says `"waitForValue": false`). Otherwise (several outputs, or a
  `oneOf` table, and none marked main), the cost is shared out like squeezing honeycomb: outputs that already have a
  value keep it, the rest share what's left, evenly or by their `share`. When several recipes share out to the same
  thing, the lowest share wins, as with any value. If every output has a value and one ingredient doesn't, that
  ingredient is worked out backwards from them.
- **The cheapest way still wins.** A value is the cheapest way of making the item, so a recipe's power cost only shows in
  a value when your machine is the cheapest way to make it.
- **An energy unit's price**: the server's `EnergyEmc` setting, else what your generators make it cost (the cheapest;
  only base values and fuel's heat count, so prices are known before anything is worked out), else your `emcPerUnit`,
  else nothing (the doctor warns).
- **Free power.** Windmills make power for nothing, so power charged into a value is EMC from nothing when the item is
  burned. The doctor warns when a recipe in use earns more than 1 EMC a second of machine time from its power alone (a
  quarter of an Energy Collector MK1). Keep prices modest, or mark a unit `onlyFromFuel` when it really can't be made
  for free.
- A recipe whose output is also one of its single-choice inputs has that amount netted off (a catalyst written as an
  ingredient).
- Costs that change with the variant (an iron plate takes more power than a copper one) are a table by variant; costs
  your data gives in other units (ticks, load × time) are a formula in a reader.

## Readers: recipes from your mod's own data

Instead of listing recipes, a method can point at data your mod already keeps, and Vintage-E reads it at every start, so
the method stays current when you add recipes:

```json
"readers": [
  {
    "from": "attribute",
    "collectible": "block:mymod:press-*",
    "attribute": "pressRecipes",
    "recipe": {
      "code": "${Die}-${Input}",
      "inputs":  [ { "code": "$Input", "quantity": "$InputQuantity", "name": "wildcard" } ],
      "tools":   [ { "code": "mymod:die-${Die}" } ],
      "outputs": [ { "code": "$Output", "type": "$OutputType", "quantity": "$OutputQuantity" } ],
      "costs":   { "seconds": "$Seconds", "energy": { "unit": "mymod:work", "amount": 400 } }
    }
  },
  {
    "from": "assets",
    "folder": "recipes/mymachine",
    "domain": "mymod",
    "recipe": { "inputs": [ { "each": "$Ingredients", "code": "$Code", "quantity": "$Quantity" } ], "outputs": [ { "code": "$Output.Code" } ] }
  }
]
```

- `from: attribute` reads the attribute (a dotted path) of every block/item `collectible` matches; a list gives one recipe
  per entry. The same list on every variant of a block counts once.
- `from: assets` reads every JSON file in `folder` (of `domain`, default your mod); a file that is a list gives one recipe
  per entry. `list` (a dotted path) picks a list inside each file.
- In the template, `"$Field"` is a field of the source entry (dots go deeper, `"$Output.Code"`, `"$Steps.0.Item"`; names
  ignore case; `"$"` alone is the entry itself), and `"${Field}"` puts a field's text inside a longer string. A stack
  whose code is missing is left out (an optional second ingredient). In a list, `{ "each": "$List", ... }` makes one
  entry per element of that list (per value, for an object such as a grid recipe's ingredient map), with fields
  relative to the element.
- *1.23.1*: inside `each`, `"$^Field"` is a field of the entry around it (`$^^` two levels up), `"$@key"` the key when
  `each` goes through an object and `"$@index"` the place in a list.
- *1.23.1*: a string starting with `=` is a formula worked out from the data: `"=$Load * $Seconds"`,
  `"=$Ticks / 20"`, `"=count($^pattern, $@key)"` (how often a grid key comes up in the pattern; a pattern written as a
  list counts as its rows joined with commas). A formula whose field is missing is left out, like a missing `"$Field"`,
  unless it says what to use instead: `"=default($stackSize, 1) * 2"`. A field name in a formula is letters, digits,
  `_` and dots, so `"=$Seconds-1"` is Seconds less 1.
- *1.23.1*: an ingredient slot's alternatives: `{ "each": "$ingredients", "anyOf": [ { "each": "$inputs", "code": "$code",
  "quantity": "$quantity" } ] }`; a slot none of whose alternatives has a code is left out.
- Codes without a domain in the recipes a reader makes get the domain of the data: the block's or item's, or the file's.
  The same recipe read twice (the same text) counts once.
- Your `{wildcard}` placeholders work as they are if the input reading the `*` has `"name": "wildcard"`.

## Checking what Vintage-E understood

- `/emc diagnose methodcheck <yourmodid>`: every problem in your documents with its JSON path and a "did you mean", which
  recipes are used, beaten by a cheaper way, making something whose value is set (a base value, a pin) or waiting for
  a value, whether each `supersedes` entry matched, a worked example, and a file of what Vintage-E understood
  (`ModData/vintagee/methods-<modid>.json`). From the console it is plain text. A recipe whose missing code belongs to a
  mod that isn't loaded (one for an optional mod) is counted, not flagged.
- `/emc methods [<modid>|<method>]`: every recipe with what it uses and makes, its costs and their prices, and switches.
- `/emc dump hand` on something your machine makes: the recipe behind its value, line by line.
- `/emc diagnose draftmethods <modid>`: a starting document drafted from recipe data Vintage-E can't read (guessed from
  the field names, with notes to check).

## Server settings

`EnergyEmc` (unit → EMC per unit), `ChargeMethodEnergy`, `ChargeMethodHeat`, `ChargeMethodWear` (each on by default),
`MethodEmcPerSecond` (0 by default) and `DisabledMethods` (a mod id, a method, one recipe `method/recipe`, or *1.23.1*
some of its variants: `method/recipe?metal=iron`, with `&` between several names and `*` allowed), all in
`/emc configure`. To change a mod's numbers, `/emc methods copy <modid>` and edit the copy in
`ModConfig/vintagee-methods/` (above).
