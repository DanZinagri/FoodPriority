using RimWorld;
using UnityEngine;
using Verse;

namespace FoodPriority
{
    public enum FoodTier
    {
        None,
        FreshMeal,
        BabyFood,
        Preserved,
        Raw,
        RawTasty
    }

    // The tiers every food a colonist eats is sorted into, and the score each tier gets. They are rules
    // rather than values written once: they are worked out again at every startup for every food the
    // player has not set by hand, so a food mod added halfway through a playthrough lands in the right
    // tier without anyone touching it.
    //
    // The goal is the one the baseline mod set out: pawns eat what was cooked today and leave the rations
    // alone until they are actually needed, whoever added the food.
    //
    //   freshly cooked   5, plus whatever it is worth in flavour, so good meals still beat plain ones
    //   baby food        2
    //   rations         -50
    //   raw food       -100, for food that is unpleasant raw (potatoes, rice, meat)
    //   tasty raw food   off: berries and the like keep their vanilla 0, above the rations, because
    //                    they spoil and a tribal start lives on them
    //   emergency food   left alone, already -150 or lower in the game's own hands
    //
    // Every tier is a flat number apart from fresh meals, so no food can ever end up in the wrong band
    // because of what it happens to taste like.
    public class FoodPriorityRuleSet : IExposable
    {
        public const float DefaultFreshMeal = 5f;
        public const float DefaultBabyFood = 2f;
        public const float DefaultPreserved = -50f;
        public const float DefaultRaw = -100f;
        public const float DefaultRawTasty = 0f;
        public const float DefaultPreservedDays = 15f;

        public bool enabled = true;

        public bool freshMealOn = true;
        public float freshMeal = DefaultFreshMeal;
        public bool freshMealKeepsFlavour = true;

        public bool babyFoodOn = true;
        public float babyFood = DefaultBabyFood;

        public bool preservedOn = true;
        public float preserved = DefaultPreserved;
        // Food that keeps this long is a ration rather than something to eat off the shelf.
        public float preservedDays = DefaultPreservedDays;

        public bool rawOn = true;
        public float raw = DefaultRaw;

        // Off by default: vanilla already scores berries and milk at 0, which is where they belong.
        public bool rawTastyOn;
        public float rawTasty = DefaultRawTasty;

        public void ResetToDefaults()
        {
            enabled = true;
            freshMealOn = true;
            freshMeal = DefaultFreshMeal;
            freshMealKeepsFlavour = true;
            babyFoodOn = true;
            babyFood = DefaultBabyFood;
            preservedOn = true;
            preserved = DefaultPreserved;
            preservedDays = DefaultPreservedDays;
            rawOn = true;
            raw = DefaultRaw;
            rawTastyOn = false;
            rawTasty = DefaultRawTasty;
        }

        public FoodTier Classify(ThingDef def)
        {
            // Drugs are left alone: what a colony does with them is not what this is about.
            if (def.IsDrug)
            {
                return FoodTier.None;
            }
            FoodPreferability preferability = def.ingestible.preferability;

            // Baby food, and anything a mod added like it: made for babies, unpleasant for everyone else.
            // It sits just under the meals, so a colonist will take one rather than break into the
            // rations, but not in preference to actual food. Babies have no mood to hurt, so what a baby
            // sees is the priority alone - which this leaves high. Not babiesCanIngest alone: milk and
            // insect jelly carry it too.
            if (def.ingestible.babiesCanIngest && FoodPriorityScore.Taste(def) < 0f)
            {
                return FoodTier.BabyFood;
            }

            // Raw food, flat and well under the rations. Flavour is not carried down here: the game
            // already puts emergency-only food at -150, and letting a raw ingredient's flavour drag it
            // past that would have a starving pawn reaching for a corpse before a potato.
            if (preferability == FoodPreferability.RawBad)
            {
                return FoodTier.Raw;
            }
            // Food that is fine eaten raw - berries, milk, a mod's fruit - is its own tier. It spoils
            // like a fresh meal and carries no raw-food mood penalty, so burying it under the rations
            // would have a tribal colony sitting on rotting berries while it eats its pemmican.
            if (preferability == FoodPreferability.RawTasty)
            {
                return FoodTier.RawTasty;
            }

            // Emergency-only food, and anything a pawn will not eat for nutrition at all, is left exactly
            // as it shipped. The -150 the game already applies is below everything set here, so there is
            // nothing to fix and no reason to touch a cannibal colony's corpses.
            if ((int)preferability < (int)FoodPreferability.MealTerrible)
            {
                return FoodTier.None;
            }

            CompProperties_Rottable rottable = def.GetCompProperties<CompProperties_Rottable>();
            if (rottable == null || rottable.daysToRotStart >= preservedDays)
            {
                return FoodTier.Preserved;
            }
            return FoodTier.FreshMeal;
        }

        // The colonist score a tier gives this food, or false if its rule is off.
        public bool TryGetScore(ThingDef def, FoodTier tier, out float score)
        {
            score = 0f;
            if (!enabled)
            {
                return false;
            }
            switch (tier)
            {
                case FoodTier.FreshMeal:
                    if (!freshMealOn)
                    {
                        return false;
                    }
                    // The one tier ranked within itself, because a lavish meal being worth more than a
                    // simple one is the whole reason a colonist walks past the simple one. Bad flavour
                    // is dropped, so even an awful meal stays ahead of the rations: eat what will spoil,
                    // save what will not.
                    score = freshMeal + (freshMealKeepsFlavour ? Mathf.Max(0f, FoodPriorityScore.Taste(def)) : 0f);
                    return true;
                case FoodTier.BabyFood:
                    score = babyFood;
                    return babyFoodOn;
                case FoodTier.Preserved:
                    // Flat on purpose: the score already counts taste, so a preserved meal that happens
                    // to be delicious would climb straight back above a plain fresh one on flavour alone.
                    score = preserved;
                    return preservedOn;
                case FoodTier.Raw:
                    score = raw;
                    return rawOn;
                case FoodTier.RawTasty:
                    score = rawTasty;
                    return rawTastyOn;
                default:
                    return false;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", defaultValue: true);
            Scribe_Values.Look(ref freshMealOn, "freshMealOn", defaultValue: true);
            Scribe_Values.Look(ref freshMeal, "freshMeal", DefaultFreshMeal);
            Scribe_Values.Look(ref freshMealKeepsFlavour, "freshMealKeepsFlavour", defaultValue: true);
            Scribe_Values.Look(ref babyFoodOn, "babyFoodOn", defaultValue: true);
            Scribe_Values.Look(ref babyFood, "babyFood", DefaultBabyFood);
            Scribe_Values.Look(ref preservedOn, "preservedOn", defaultValue: true);
            Scribe_Values.Look(ref preserved, "preserved", DefaultPreserved);
            Scribe_Values.Look(ref preservedDays, "preservedDays", DefaultPreservedDays);
            Scribe_Values.Look(ref rawOn, "rawOn", defaultValue: true);
            Scribe_Values.Look(ref raw, "raw", DefaultRaw);
            Scribe_Values.Look(ref rawTastyOn, "rawTastyOn", defaultValue: false);
            Scribe_Values.Look(ref rawTasty, "rawTasty", DefaultRawTasty);
        }
    }
}
