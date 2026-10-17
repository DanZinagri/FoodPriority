using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FoodPriority
{
    public class FoodPrioritySettings : ModSettings
    {
        // Settings written before the tier rules existed have no version and are migrated once at startup.
        private const int CurrentVersion = 2;

        // Food key -> a value the player set by hand. The key is the defName, except that the members of
        // a group - corpses, eggs - share the group's (FoodPriorityApplier.KeyOf). Absent means "fall through": to the tier rules for
        // colonists, then to the def as its author wrote it, so a modlist change never leaves a stale
        // value behind and the config file stays small.
        public Dictionary<string, float> offsets = new Dictionary<string, float>();
        public Dictionary<string, float> animalOffsets = new Dictionary<string, float>();
        public Dictionary<string, bool> caravanLow = new Dictionary<string, bool>();
        public FoodPriorityRuleSet rules = new FoodPriorityRuleSet();

        public bool showButton = true;
        // Show which mod each food comes from in the editor, and allow sorting by it.
        public bool showModSource;
        // Only read to migrate old settings: true there means the old one-off defaults had been written.
        public bool defaultsApplied;
        public int version = CurrentVersion;
        public Vector2 editorPos = new Vector2(-1f, -1f);
        public Vector2 editorSize = new Vector2(1020f, 720f);

        public bool NeedsMigration => version < CurrentVersion;

        public void MarkMigrated()
        {
            version = CurrentVersion;
        }

        public Dictionary<string, float> OffsetsFor(Eater eater)
        {
            return eater == Eater.Animals ? animalOffsets : offsets;
        }

        // The value written into the def: set by hand, else the tier rule, else as shipped.
        public float Offset(ThingDef def, Eater eater)
        {
            return OffsetsFor(eater).TryGetValue(FoodPriorityApplier.KeyOf(def), out var value) ? value : FallbackOffset(def, eater);
        }

        // What the food goes back to when its hand-set value is cleared.
        public float FallbackOffset(ThingDef def, Eater eater)
        {
            if (eater == Eater.Colonists && FoodPriorityApplier.TryGetRuleOffset(def, out var rule))
            {
                return rule;
            }
            return FoodPriorityApplier.ShippedOffset(def, eater);
        }

        public bool IsHandSet(ThingDef def, Eater eater)
        {
            return OffsetsFor(eater).ContainsKey(FoodPriorityApplier.KeyOf(def));
        }

        public bool CaravanLow(ThingDef def)
        {
            return caravanLow.TryGetValue(FoodPriorityApplier.KeyOf(def), out var value) ? value : FoodPriorityApplier.ShippedCaravanLow(def);
        }

        public void SetOffset(ThingDef def, Eater eater, float value)
        {
            // Setting a food to the value it would fall back to anyway is the same as not setting it, and
            // leaving it unset keeps it following the rules if they change later.
            if (Mathf.Abs(value - FallbackOffset(def, eater)) < 0.01f)
            {
                OffsetsFor(eater).Remove(FoodPriorityApplier.KeyOf(def));
            }
            else
            {
                OffsetsFor(eater)[FoodPriorityApplier.KeyOf(def)] = value;
            }
        }

        public void ClearOffset(ThingDef def, Eater eater)
        {
            OffsetsFor(eater).Remove(FoodPriorityApplier.KeyOf(def));
        }

        public void SetCaravanLow(ThingDef def, bool value)
        {
            if (value == FoodPriorityApplier.ShippedCaravanLow(def))
            {
                caravanLow.Remove(FoodPriorityApplier.KeyOf(def));
            }
            else
            {
                caravanLow[FoodPriorityApplier.KeyOf(def)] = value;
            }
        }

        public bool IsChanged(ThingDef def)
        {
            return offsets.ContainsKey(FoodPriorityApplier.KeyOf(def)) || animalOffsets.ContainsKey(FoodPriorityApplier.KeyOf(def))
                || caravanLow.ContainsKey(FoodPriorityApplier.KeyOf(def));
        }

        // Counted over the loaded foods rather than the dictionaries: a food with several things changed
        // is one change, and entries left behind by a mod that has since been removed are kept (so they
        // come back with the mod) but are not shown as changes.
        public int ChangedCount
        {
            get
            {
                int count = 0;
                List<ThingDef> all = FoodPriorityApplier.ListedFoods;
                for (int i = 0; i < all.Count; i++)
                {
                    if (IsChanged(all[i]))
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public void ResetAll()
        {
            offsets.Clear();
            animalOffsets.Clear();
            caravanLow.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref offsets, "offsets", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref animalOffsets, "animalOffsets", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref caravanLow, "caravanLow", LookMode.Value, LookMode.Value);
            Scribe_Deep.Look(ref rules, "rules");
            Scribe_Values.Look(ref showButton, "showButton", defaultValue: true);
            Scribe_Values.Look(ref showModSource, "showModSource", defaultValue: false);
            Scribe_Values.Look(ref defaultsApplied, "defaultsApplied", defaultValue: false);
            // An old file has no version, which loads as 0 and triggers the migration.
            Scribe_Values.Look(ref version, "version", 0);
            Scribe_Values.Look(ref editorPos, "editorPos", new Vector2(-1f, -1f));
            Scribe_Values.Look(ref editorSize, "editorSize", new Vector2(1020f, 720f));
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                offsets ??= new Dictionary<string, float>();
                animalOffsets ??= new Dictionary<string, float>();
                caravanLow ??= new Dictionary<string, bool>();
                rules ??= new FoodPriorityRuleSet();
            }
        }
    }
}
