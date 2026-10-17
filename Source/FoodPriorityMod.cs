using UnityEngine;
using Verse;

namespace FoodPriority
{
    public class FoodPriorityMod : Mod
    {
        public static FoodPrioritySettings Settings;

        public FoodPriorityMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FoodPrioritySettings>();
        }

        public override string SettingsCategory()
        {
            return "FoodPri.ModTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            // No title of our own: the mod settings window already heads the page with the mod's name.
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            listing.Label("FoodPri.SettingsBlurb".Translate());
            GUI.color = Color.white;
            listing.Gap(10f);

            if (listing.ButtonText("FoodPri.OpenEditor".Translate()))
            {
                Dialog_FoodPriority.Toggle();
            }
            listing.Gap(10f);

            listing.CheckboxLabeled("FoodPri.ShowButton".Translate(), ref Settings.showButton, "FoodPri.ShowButtonTip".Translate());
            listing.CheckboxLabeled("FoodPri.ShowModSource".Translate(), ref Settings.showModSource, "FoodPri.ShowModSourceTip".Translate());

            listing.Gap(10f);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            listing.Label("FoodPri.ChangedCount".Translate(Settings.ChangedCount));
            GUI.color = Color.white;

            listing.End();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            FoodPriorityApplier.Apply();
        }

        // Called by the editor window so edits survive a crash-free exit even if mod settings are never opened.
        public static void Save()
        {
            LoadedModManager.GetMod<FoodPriorityMod>()?.WriteSettings();
        }
    }
}
