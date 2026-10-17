namespace FoodPriority
{
    // The editor's two bulk buttons. Neither writes a value per food any more: they decide which layer a
    // food's score comes from (set by hand, then the tier rules, then what its mod shipped).
    public static class FoodPriorityPresets
    {
        // Every colonist score back to what the tier rules give, with the rules at their defaults. Caravan
        // ticks and animal scores are a separate question and are left as they are.
        public static void ApplyRecommended()
        {
            FoodPrioritySettings settings = FoodPriorityMod.Settings;
            settings.offsets.Clear();
            settings.rules.ResetToDefaults();
            FoodPriorityApplier.RulesChanged();
        }

        // Nothing of the mod's left: no hand-set values and the rules off, so every food is exactly as its
        // own mod shipped it.
        public static void BackToVanilla()
        {
            FoodPrioritySettings settings = FoodPriorityMod.Settings;
            settings.ResetAll();
            settings.rules.enabled = false;
            FoodPriorityApplier.RulesChanged();
        }
    }
}
