using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FoodPriority
{
    // The one place vanilla reuses a food's priority for something else. For a pawn whose genes let them
    // eat raw food without minding (Robust Digestion, and every mod xenotype built on it), FoodOptimality
    // adds simple meal's optimalityOffsetHumanlikes to raw food, so that raw scores like a simple meal.
    // Setting simple meal's priority would silently move raw food for those pawns too.
    //
    // This swaps that one read for simple meal's shipped value. It is a transpiler, applied once at
    // startup: the game compiles the rewritten method and calls that from then on, so no code of ours
    // runs while playing - the bonus is one static field load instead of three chained ones.
    public static class RawFoodGenePatch
    {
        // Simple meal's priority as its mod shipped it, captured before any of our values are written.
        public static float shippedMealSimpleOffset;

        private static readonly FieldInfo mealSimple = AccessTools.Field(typeof(ThingDefOf), nameof(ThingDefOf.MealSimple));
        private static readonly FieldInfo ingestible = AccessTools.Field(typeof(ThingDef), nameof(ThingDef.ingestible));
        private static readonly FieldInfo offset = AccessTools.Field(typeof(IngestibleProperties), nameof(IngestibleProperties.optimalityOffsetHumanlikes));
        private static readonly FieldInfo replacement = AccessTools.Field(typeof(RawFoodGenePatch), nameof(shippedMealSimpleOffset));

        public static void Apply(float shippedOffset)
        {
            shippedMealSimpleOffset = shippedOffset;

            MethodInfo target = AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.FoodOptimality));
            if (target == null)
            {
                Log.Warning("[Configurable Food Priority] FoodUtility.FoodOptimality not found; pawns with raw-food genes will use simple meal's current priority.");
                return;
            }
            // Checked up front, so a method another mod has already rewritten is left untouched rather
            // than re-emitted unchanged.
            if (CountMatches(PatchProcessor.GetCurrentInstructions(target)) != 1)
            {
                Log.Warning("[Configurable Food Priority] Could not find the raw-food gene bonus in FoodUtility.FoodOptimality (another mod may have changed it); skipping. Pawns with raw-food genes will use simple meal's current priority.");
                return;
            }
            new Harmony("DanZinagri.FoodPriority").Patch(target,
                transpiler: new HarmonyMethod(typeof(RawFoodGenePatch), nameof(Transpiler)));
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i + 2 < codes.Count; i++)
            {
                if (IsMatch(codes, i))
                {
                    CodeInstruction load = new CodeInstruction(OpCodes.Ldsfld, replacement);
                    load.labels.AddRange(codes[i].labels);
                    load.blocks.AddRange(codes[i].blocks);
                    codes.RemoveRange(i, 3);
                    codes.Insert(i, load);
                    break;
                }
            }
            return codes;
        }

        private static int CountMatches(List<CodeInstruction> codes)
        {
            int count = 0;
            for (int i = 0; i + 2 < codes.Count; i++)
            {
                if (IsMatch(codes, i))
                {
                    count++;
                }
            }
            return count;
        }

        // ThingDefOf.MealSimple.ingestible.optimalityOffsetHumanlikes
        private static bool IsMatch(List<CodeInstruction> codes, int i)
        {
            return codes[i].LoadsField(mealSimple)
                && codes[i + 1].LoadsField(ingestible)
                && codes[i + 2].LoadsField(offset);
        }
    }
}
