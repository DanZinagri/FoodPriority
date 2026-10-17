using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FoodPriority
{
    // The tier rules: which score every colonist food gets unless the player has set it by hand. Rules
    // are worked out again for every food at startup, so foods from a mod added later follow them too.
    public class Dialog_FoodPriorityRules : Window
    {
        private const float RowHeight = 30f;
        private const float NumberWidth = 64f;
        private const float Indent = 28f;
        private const float Limit = 999f;

        private string freshBuffer;
        private string babyBuffer;
        private string preservedBuffer;
        private string preservedDaysBuffer;
        private string rawBuffer;
        private string rawTastyBuffer;

        private readonly int[] tierCounts = new int[Enum.GetValues(typeof(FoodTier)).Length];
        private int countedRevision = -1;

        private static FoodPriorityRuleSet Rules => FoodPriorityMod.Settings.rules;

        public Dialog_FoodPriorityRules()
        {
            doCloseX = true;
            draggable = true;
            preventCameraMotion = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            forcePause = false;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 530f);

        public static void Toggle()
        {
            if (Find.WindowStack == null)
            {
                return;
            }
            if (Find.WindowStack.IsOpen<Dialog_FoodPriorityRules>())
            {
                Find.WindowStack.TryRemove(typeof(Dialog_FoodPriorityRules));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_FoodPriorityRules());
            }
        }

        public override void PreOpen()
        {
            // Same as the editor: not calling base, so opening it does not drop the player's selection.
            SetInitialSizeAndPosition();
        }

        public override void PostClose()
        {
            base.PostClose();
            FoodPriorityMod.Save();
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (countedRevision != FoodPriorityApplier.Revision)
            {
                CountTiers();
            }
            FoodPriorityRuleSet rules = Rules;
            bool changed = false;

            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            Text.Font = GameFont.Medium;
            listing.Label("FoodPri.RulesTitle".Translate());
            Text.Font = GameFont.Small;
            listing.Gap(4f);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            listing.Label("FoodPri.RulesBlurb".Translate());
            GUI.color = Color.white;
            listing.Gap(8f);

            bool enabled = rules.enabled;
            listing.CheckboxLabeled("FoodPri.RulesEnabled".Translate(), ref rules.enabled, "FoodPri.RulesEnabledTip".Translate());
            changed |= enabled != rules.enabled;
            listing.GapLine(10f);

            if (!rules.enabled)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
            }
            changed |= TierRow(listing, FoodTier.FreshMeal, ref rules.freshMealOn, ref rules.freshMeal, ref freshBuffer);
            changed |= IndentedCheckbox(listing, "FoodPri.RuleKeepsFlavour".Translate(), "FoodPri.RuleKeepsFlavourTip".Translate(),
                ref rules.freshMealKeepsFlavour);
            changed |= TierRow(listing, FoodTier.BabyFood, ref rules.babyFoodOn, ref rules.babyFood, ref babyBuffer);
            changed |= TierRow(listing, FoodTier.Preserved, ref rules.preservedOn, ref rules.preserved, ref preservedBuffer);
            changed |= IndentedNumber(listing, "FoodPri.RulePreservedDays".Translate(), "FoodPri.RulePreservedDaysTip".Translate(),
                ref rules.preservedDays, ref preservedDaysBuffer, 0f, 1000f);
            changed |= TierRow(listing, FoodTier.Raw, ref rules.rawOn, ref rules.raw, ref rawBuffer);
            changed |= TierRow(listing, FoodTier.RawTasty, ref rules.rawTastyOn, ref rules.rawTasty, ref rawTastyBuffer);
            GUI.color = Color.white;

            listing.Gap(6f);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Text.Font = GameFont.Tiny;
            listing.Label("FoodPri.RulesUntouched".Translate());
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            listing.Gap(8f);

            if (listing.ButtonText("FoodPri.RulesRestoreDefaults".Translate()))
            {
                rules.ResetToDefaults();
                freshBuffer = babyBuffer = preservedBuffer = preservedDaysBuffer = rawBuffer = rawTastyBuffer = null;
                changed = true;
            }
            listing.End();

            if (changed)
            {
                FoodPriorityApplier.RulesChanged();
            }
        }

        private bool TierRow(Listing_Standard listing, FoodTier tier, ref bool on, ref float score, ref string buffer)
        {
            Rect rect = listing.GetRect(RowHeight);
            bool wasOn = on;
            float wasScore = score;

            Widgets.Checkbox(rect.x, rect.y + 3f, ref on, 24f);
            Rect numberRect = new Rect(rect.xMax - NumberWidth, rect.y + 3f, NumberWidth, 24f);
            Widgets.TextFieldNumeric(numberRect, ref score, ref buffer, -Limit, Limit);

            Rect labelRect = new Rect(rect.x + Indent + 4f, rect.y, numberRect.x - rect.x - Indent - 12f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, "FoodPri.RuleTier".Translate(Dialog_FoodPriority.TierLabel(tier), tierCounts[(int)tier]));
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(labelRect, ("FoodPri.Tier" + tier + "Tip").Translate());

            return on != wasOn || Mathf.Abs(score - wasScore) > 0.001f;
        }

        private static bool IndentedCheckbox(Listing_Standard listing, string label, string tip, ref bool value)
        {
            Rect rect = listing.GetRect(RowHeight);
            rect.xMin += Indent;
            bool before = value;
            Widgets.CheckboxLabeled(rect, label, ref value, placeCheckboxNearText: true);
            TooltipHandler.TipRegion(rect, tip);
            return value != before;
        }

        private static bool IndentedNumber(Listing_Standard listing, string label, string tip, ref float value, ref string buffer, float min, float max)
        {
            Rect rect = listing.GetRect(RowHeight);
            rect.xMin += Indent;
            float before = value;
            Rect numberRect = new Rect(rect.xMax - NumberWidth, rect.y + 3f, NumberWidth, 24f);
            Widgets.TextFieldNumeric(numberRect, ref value, ref buffer, min, max);
            Rect labelRect = new Rect(rect.x, rect.y, numberRect.x - rect.x - 8f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(labelRect, tip);
            return Mathf.Abs(value - before) > 0.001f;
        }

        private void CountTiers()
        {
            countedRevision = FoodPriorityApplier.Revision;
            Array.Clear(tierCounts, 0, tierCounts.Length);
            List<ThingDef> all = FoodPriorityApplier.AllFoods;
            for (int i = 0; i < all.Count; i++)
            {
                tierCounts[(int)FoodPriorityApplier.TierOf(all[i])]++;
            }
        }
    }
}
