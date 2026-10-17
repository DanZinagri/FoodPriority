using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace FoodPriority
{
    // Whose score is being looked at. Colonists and animals read two different def fields, and only the
    // colonist one has tier rules and a flavour term.
    public enum Eater
    {
        Colonists,
        Animals
    }

    // What a food is worth to a colonist, in the same units vanilla works in.
    //
    // FoodUtility.FoodOptimality starts every food at 300, takes off one point per tile of walking, and
    // then adds a handful of things. Two of them depend only on the food itself: how it tastes, run
    // through a curve, and a flat penalty if it is emergency-only food. Those are as much a part of why
    // one food beats another as the priority number is, so the editor shows the three added together and
    // lets the player set that total. The 300 and the distance are the same for everything, so they are
    // left out. Animals get the same treatment from their own field, minus flavour.
    public static class FoodPriorityScore
    {
        // A food a pawn will only touch when desperate carries this in FoodOptimality. Corpses and the like
        // (DesperateOnlyForHumanlikes) carry it for colonists only.
        private const float EmergencyPenalty = -150f;

        private static readonly Dictionary<ThingDef, float> tasteCache = new Dictionary<ThingDef, float>();
        private static SimpleCurve moodCurve;

        // Vanilla's own curve, read off FoodUtility so a retune by Ludeon carries over. The fallback is
        // the curve as it stands in 1.7.
        private static SimpleCurve MoodCurve
        {
            get
            {
                if (moodCurve == null)
                {
                    moodCurve = typeof(FoodUtility)
                        .GetField("FoodOptimalityEffectFromMoodCurve", BindingFlags.NonPublic | BindingFlags.Static)
                        ?.GetValue(null) as SimpleCurve;
                }
                if (moodCurve == null)
                {
                    moodCurve = new SimpleCurve
                    {
                        new CurvePoint(-100f, -600f),
                        new CurvePoint(-10f, -100f),
                        new CurvePoint(-5f, -70f),
                        new CurvePoint(-1f, -50f),
                        new CurvePoint(0f, 0f),
                        new CurvePoint(100f, 800f)
                    };
                }
                return moodCurve;
            }
        }

        public static float Of(ThingDef def, Eater eater)
        {
            return FoodPriorityMod.Settings.Offset(def, eater) + Fixed(def, eater);
        }

        public static float Shipped(ThingDef def, Eater eater)
        {
            return FoodPriorityApplier.ShippedOffset(def, eater) + Fixed(def, eater);
        }

        // What the food scores once its hand-set value is cleared: the tier rule, or as shipped.
        public static float Fallback(ThingDef def, Eater eater)
        {
            return FoodPriorityMod.Settings.FallbackOffset(def, eater) + Fixed(def, eater);
        }

        public static void Set(ThingDef def, Eater eater, float score)
        {
            FoodPriorityMod.Settings.SetOffset(def, eater, score - Fixed(def, eater));
        }

        // Everything in the score that is not the priority number itself. Animals have no mood, so
        // FoodOptimality never adds flavour for them.
        public static float Fixed(ThingDef def, Eater eater)
        {
            return eater == Eater.Animals ? Emergency(def, eater) : Taste(def) + Emergency(def, eater);
        }

        public static float Emergency(ThingDef def, Eater eater)
        {
            FoodPreferability preferability = def.ingestible.preferability;
            if (preferability == FoodPreferability.DesperateOnly
                || (preferability == FoodPreferability.DesperateOnlyForHumanlikes && eater == Eater.Colonists))
            {
                return EmergencyPenalty;
            }
            return 0f;
        }

        // What vanilla adds for how the food tastes, as far as it can be known without a pawn to ask.
        // Ideology precepts, traits, rot and what went into the specific meal are all per-pawn or
        // per-item, and deliberately not counted.
        public static float Taste(ThingDef def)
        {
            if (!tasteCache.TryGetValue(def, out var cached))
            {
                cached = MoodEffect(def.ingestible.tasteThought) + MoodEffect(def.ingestible.specialThoughtDirect);
                tasteCache[def] = cached;
            }
            return cached;
        }

        private static float MoodEffect(ThoughtDef thought)
        {
            if (thought?.stages == null || thought.stages.Count == 0 || thought.stages[0] == null)
            {
                return 0f;
            }
            float mood = thought.stages[0].baseMoodEffect;
            if (mood == 0f)
            {
                return 0f;
            }
            return MoodCurve.Evaluate(mood);
        }
    }
}
