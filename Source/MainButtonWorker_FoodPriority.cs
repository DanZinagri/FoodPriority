using RimWorld;

namespace FoodPriority
{
    // The editor's way in, sitting with Architect and Animals along the bottom. Vanilla draws it as part
    // of a row it was already drawing, so there is no per-frame code of ours behind it at all - which is
    // the whole reason it lives here rather than in a button the mod has to paint for itself.
    public class MainButtonWorker_FoodPriority : MainButtonWorker
    {
        public override bool Visible => base.Visible
            && FoodPriorityMod.Settings != null
            && FoodPriorityMod.Settings.showButton;

        public override void Activate()
        {
            Dialog_FoodPriority.Toggle();
        }
    }
}
