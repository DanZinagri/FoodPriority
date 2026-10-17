using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FoodPriority
{
    // The whole mod, mechanically. Every configured value is written into the food's own def once, at
    // startup, into the fields vanilla already reads when it scores food. Apart from one value
    // RawFoodGenePatch pins at startup, the food-picking code is not patched and nothing of ours runs
    // while the game is playing.
    //
    // Each colonist value comes from the first of three layers that has one: set by hand in the editor,
    // the tier rules, or the def as its mod shipped it. Animal values have no rules, only the other two.
    [StaticConstructorOnStartup]
    public static class FoodPriorityApplier
    {
        private struct Shipped
        {
            public float offset;
            public float animalOffset;
            public bool caravanLow;
        }

        // What each def looked like before we touched it, so a value can always be put back and so the
        // editor can show what the food's author intended.
        private static readonly Dictionary<ThingDef, Shipped> shipped = new Dictionary<ThingDef, Shipped>();
        private static readonly List<ThingDef> allFoods = new List<ThingDef>();
        // What the editor lists: every food, except that each group - corpses and eggs, one per creature,
        // so dozens or hundreds of them in a modded game - is folded into a single row.
        private static readonly List<ThingDef> listedFoods = new List<ThingDef>();
        private static readonly Dictionary<ThingDef, FoodGroup> groupOfMember = new Dictionary<ThingDef, FoodGroup>();
        private static readonly Dictionary<ThingDef, FoodGroup> groupOfRow = new Dictionary<ThingDef, FoodGroup>();

        // Checked in order; a food joins the first group that takes it.
        private static readonly List<FoodGroup> groups = new List<FoodGroup>
        {
            new FoodGroup("FoodPriority:Corpses", "FoodPri.CorpsesLabel", "FoodPri.CorpsesTip",
                d => d.IsCorpse, iconPath: "UI/Issues/Corpses"),
            new FoodGroup("FoodPriority:EggsFertilized", "FoodPri.EggsFertilizedLabel", "FoodPri.EggsFertilizedTip",
                d => d.IsWithinCategory(ThingCategoryDefOf.EggsFertilized), preferredProxy: "EggChickenFertilized"),
            new FoodGroup("FoodPriority:EggsUnfertilized", "FoodPri.EggsUnfertilizedLabel", "FoodPri.EggsUnfertilizedTip",
                d => d.IsWithinCategory(ThingCategoryDefOf.EggsUnfertilized), preferredProxy: "EggChickenUnfertilized")
        };
        private static readonly Dictionary<ThingDef, FoodTier> tiers = new Dictionary<ThingDef, FoodTier>();
        // Only the foods whose tier rule is on, already converted from a score to the def's offset.
        private static readonly Dictionary<ThingDef, float> ruleOffsets = new Dictionary<ThingDef, float>();
        private static bool ready;

        public static List<ThingDef> AllFoods => allFoods;
        public static List<ThingDef> ListedFoods => listedFoods;

        // The settings key: the group's for a group member, else the defName.
        public static string KeyOf(ThingDef def)
        {
            return groupOfMember.TryGetValue(def, out var group) ? group.key : def.defName;
        }

        // Whether this listed row is a group's stand-in rather than a food of its own.
        public static bool TryGetGroupRow(ThingDef def, out FoodGroup group)
        {
            return groupOfRow.TryGetValue(def, out group);
        }

        // Bumped whenever the rules change, so an open editor knows its rows and buffers are stale.
        public static int Revision { get; private set; }

        static FoodPriorityApplier()
        {
            // The same set a pawn's food policy offers: something with nutrition that sits somewhere in
            // the item category tree. Plenty of things carry ingestible props without ever being food a
            // colony deals with - grass and trees have nutrition so that animals can graze on them, and
            // they have no item category at all, which is exactly why the food policy window never lists
            // them either.
            HashSet<ThingDef> inCategoryTree = new HashSet<ThingDef>(ThingCategoryNodeDatabase.RootNode.catDef.DescendantThingDefs);
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def.ingestible == null || !inCategoryTree.Contains(def)
                    || def.GetStatValueAbstract(StatDefOf.Nutrition) <= 0f)
                {
                    continue;
                }
                allFoods.Add(def);
                shipped[def] = new Shipped
                {
                    offset = def.ingestible.optimalityOffsetHumanlikes,
                    animalOffset = def.ingestible.optimalityOffsetFeedingAnimals,
                    caravanLow = def.ingestible.lowPriorityCaravanFood
                };
            }
            for (int i = 0; i < allFoods.Count; i++)
            {
                ThingDef def = allFoods[i];
                FoodGroup group = groups.Find(g => g.Includes(def));
                if (group != null)
                {
                    group.members.Add(def);
                    groupOfMember[def] = group;
                }
                else
                {
                    listedFoods.Add(def);
                }
            }
            for (int i = 0; i < groups.Count; i++)
            {
                FoodGroup group = groups[i];
                // For corpses, an animal's if there is one: that is what most of them are.
                group.PickProxy(d => d.thingCategories != null && d.thingCategories.Contains(ThingCategoryDefOf.CorpsesAnimal));
                if (group.proxy != null)
                {
                    listedFoods.Add(group.proxy);
                    groupOfRow[group.proxy] = group;
                }
            }
            ready = true;

            // Before any value of ours is written, so the gene bonus is pinned to what vanilla intended.
            ThingDef mealSimple = ThingDefOf.MealSimple;
            RawFoodGenePatch.Apply(shipped.TryGetValue(mealSimple, out var meal)
                ? meal.offset
                : mealSimple.ingestible.optimalityOffsetHumanlikes);

            FoodPrioritySettings settings = FoodPriorityMod.Settings;
            if (settings == null)
            {
                return;
            }
            settings.rules ??= new FoodPriorityRuleSet();
            FoldGroupEntries(settings.offsets);
            FoldGroupEntries(settings.animalOffsets);
            FoldGroupEntries(settings.caravanLow);
            RebuildRules();
            if (settings.NeedsMigration)
            {
                Migrate(settings);
                FoodPriorityMod.Save();
            }
            else
            {
                Apply();
            }
        }

        // Settings from before the rules wrote the old recommended defaults into the hand-set table, so
        // every food would count as set by hand and never follow a rule again. Entries that equal what the
        // old defaults gave are dropped. If none matched, the player had already thrown the defaults away
        // ("back to vanilla"), and the rules start off to respect that.
        private static void Migrate(FoodPrioritySettings settings)
        {
            if (settings.defaultsApplied)
            {
                int matched = 0;
                for (int i = 0; i < allFoods.Count; i++)
                {
                    ThingDef def = allFoods[i];
                    if (settings.offsets.TryGetValue(def.defName, out var stored)
                        && TryGetLegacyDefault(def, out var legacy)
                        && Mathf.Abs(stored - legacy) < 0.05f)
                    {
                        settings.offsets.Remove(def.defName);
                        matched++;
                    }
                }
                if (matched == 0)
                {
                    settings.rules.enabled = false;
                    RebuildRules();
                }
            }
            settings.MarkMigrated();
        }

        // Group members used to be listed one by one, so an older config can hold a value per corpse or
        // egg. The first one found becomes the group's value (unless one is already set), and the rest
        // are dropped.
        private static void FoldGroupEntries<T>(Dictionary<string, T> values)
        {
            for (int g = 0; g < groups.Count; g++)
            {
                FoodGroup group = groups[g];
                for (int i = 0; i < group.members.Count; i++)
                {
                    string defName = group.members[i].defName;
                    if (values.TryGetValue(defName, out var value))
                    {
                        if (!values.ContainsKey(group.key))
                        {
                            values[group.key] = value;
                        }
                        values.Remove(defName);
                    }
                }
            }
        }

        // What the old one-off defaults wrote. Identical to the rules at their defaults, except that tasty
        // raw food (berries, milk) used to be lumped in with the rest of the raw food at -100.
        private static bool TryGetLegacyDefault(ThingDef def, out float offset)
        {
            if (TierOf(def) == FoodTier.RawTasty)
            {
                offset = FoodPriorityRuleSet.DefaultRaw - FoodPriorityScore.Fixed(def, Eater.Colonists);
                return true;
            }
            return ruleOffsets.TryGetValue(def, out offset);
        }

        public static FoodTier TierOf(ThingDef def)
        {
            return tiers.TryGetValue(def, out var tier) ? tier : FoodTier.None;
        }

        public static bool TryGetRuleOffset(ThingDef def, out float offset)
        {
            return ruleOffsets.TryGetValue(def, out offset);
        }

        public static float ShippedOffset(ThingDef def, Eater eater)
        {
            if (!shipped.TryGetValue(def, out var value))
            {
                return 0f;
            }
            return eater == Eater.Animals ? value.animalOffset : value.offset;
        }

        public static bool ShippedCaravanLow(ThingDef def)
        {
            return shipped.TryGetValue(def, out var value) && value.caravanLow;
        }

        // After any change to the rule set: re-sort every food into its tier and write the result.
        public static void RulesChanged()
        {
            RebuildRules();
            Apply();
            Revision++;
        }

        private static void RebuildRules()
        {
            tiers.Clear();
            ruleOffsets.Clear();
            FoodPriorityRuleSet rules = FoodPriorityMod.Settings?.rules;
            if (rules == null)
            {
                return;
            }
            for (int i = 0; i < allFoods.Count; i++)
            {
                ThingDef def = allFoods[i];
                FoodTier tier = rules.Classify(def);
                tiers[def] = tier;
                if (rules.TryGetScore(def, tier, out var score))
                {
                    ruleOffsets[def] = score - FoodPriorityScore.Fixed(def, Eater.Colonists);
                }
            }
        }

        // Cheap enough to run on every keystroke in the editor: a few hundred field writes, no allocation.
        public static void Apply()
        {
            if (!ready)
            {
                return;
            }
            FoodPrioritySettings settings = FoodPriorityMod.Settings;
            if (settings == null)
            {
                return;
            }
            foreach (KeyValuePair<ThingDef, Shipped> pair in shipped)
            {
                ThingDef def = pair.Key;
                IngestibleProperties ingestible = def.ingestible;
                string key = KeyOf(def);
                if (!settings.offsets.TryGetValue(key, out var offset) && !ruleOffsets.TryGetValue(def, out offset))
                {
                    offset = pair.Value.offset;
                }
                ingestible.optimalityOffsetHumanlikes = offset;
                ingestible.optimalityOffsetFeedingAnimals = settings.animalOffsets.TryGetValue(key, out var animal)
                    ? animal
                    : pair.Value.animalOffset;
                ingestible.lowPriorityCaravanFood = settings.caravanLow.TryGetValue(key, out var caravan)
                    ? caravan
                    : pair.Value.caravanLow;
            }
        }
    }
}
