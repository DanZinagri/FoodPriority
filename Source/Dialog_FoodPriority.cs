using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace FoodPriority
{
    public class Dialog_FoodPriority : Window
    {
        private enum FilterMode
        {
            All,
            Meals,
            Raw,
            Drugs,
            Other,
            Changed
        }

        private enum SortMode
        {
            Score,
            Name,
            Category,
            Source
        }

        private const float RowHeight = 34f;
        private const float IconWidth = 30f;
        private const float ArrowWidth = 18f;
        private const float SliderWidth = 220f;
        private const float NumberWidth = 52f;
        private const float ResetWidth = 24f;
        private const float CaravanWidth = 64f;
        private const float Limit = 999f;
        private const float SliderRange = 200f;

        // Hand-set values are green, values the tier rules gave are blue, shipped values plain.
        private static readonly Color HandSetColor = new Color(0.68f, 0.9f, 0.68f);
        private static readonly Color RuleColor = new Color(0.7f, 0.82f, 1f);
        private static readonly Color SourceColor = new Color(1f, 1f, 1f, 0.45f);


        private Eater eater = Eater.Colonists;
        private FilterMode filter = FilterMode.All;
        private SortMode sort = SortMode.Score;
        private string search = "";

        private Vector2 scrollPosition;
        private readonly List<ThingDef> rows = new List<ThingDef>();
        private readonly Dictionary<string, string> buffers = new Dictionary<string, string>();
        private readonly Dictionary<ThingDef, Func<string>> tipGetters = new Dictionary<ThingDef, Func<string>>();
        private bool rowsDirty = true;
        // A value edit changes the sort order. Reordering under the cursor mid-drag is horrible, so the
        // list is left alone until the mouse is up and nothing is being typed into.
        private bool resortPending;
        private int seenRevision = -1;
        private List<TabRecord> eaterTabs;

        private static FoodPrioritySettings Settings => FoodPriorityMod.Settings;

        public Dialog_FoodPriority()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            forcePause = false;
            onlyOneOfTypeAllowed = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                Vector2 size = Settings.editorSize;
                return new Vector2(Mathf.Clamp(size.x, 780f, UI.screenWidth), Mathf.Clamp(size.y, 400f, UI.screenHeight));
            }
        }

        public static void Toggle()
        {
            if (Find.WindowStack == null)
            {
                return;
            }
            if (Find.WindowStack.IsOpen<Dialog_FoodPriority>())
            {
                Find.WindowStack.TryRemove(typeof(Dialog_FoodPriority));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_FoodPriority());
            }
        }

        public override void PreOpen()
        {
            // Deliberately not calling base: for a Dialog-layer window it drops the player's selection and
            // cancels any drag. This one sits alongside the game rather than interrupting it.
            SetInitialSizeAndPosition();
        }

        protected override void SetInitialSizeAndPosition()
        {
            Vector2 size = InitialSize;
            Vector2 saved = Settings.editorPos;
            Vector2 position = (saved.x < 0f || saved.y < 0f)
                ? new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f)
                : saved;
            position.x = Mathf.Clamp(position.x, 0f, UI.screenWidth - size.x);
            position.y = Mathf.Clamp(position.y, 0f, UI.screenHeight - size.y);
            windowRect = new Rect(position, size);
        }

        public override void PostClose()
        {
            base.PostClose();
            Settings.editorPos = windowRect.position;
            Settings.editorSize = windowRect.size;
            FoodPriorityMod.Save();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // The rules window changed which tier or value a food falls back to.
            if (seenRevision != FoodPriorityApplier.Revision)
            {
                seenRevision = FoodPriorityApplier.Revision;
                buffers.Clear();
                rowsDirty = true;
            }
            // Sorting by mod is only offered while the mod is shown.
            if (sort == SortMode.Source && !Settings.showModSource)
            {
                sort = SortMode.Score;
                rowsDirty = true;
            }
            if (rowsDirty)
            {
                RebuildRows();
                rowsDirty = false;
            }

            // Colonists and animals are two tabs over one section holding everything else.
            Rect section = new Rect(inRect.x, inRect.y + TabDrawer.TabHeight, inRect.width, inRect.height - TabDrawer.TabHeight);
            Widgets.DrawMenuSection(section);
            DrawEaterTabs(section);
            inRect = section.ContractedBy(10f);

            float y = inRect.y;
            DrawToolbar(new Rect(inRect.x, y, inRect.width, 30f));
            y += 36f;
            DrawActions(new Rect(inRect.x, y, inRect.width, 28f));
            y += 34f;
            // Less the scrollbar the list always reserves, so the headers sit over their own controls.
            DrawColumnHeaders(new Rect(inRect.x, y, inRect.width - 18f, 20f));
            y += 21f;
            Widgets.DrawLineHorizontal(inRect.x, y, inRect.width);
            y += 4f;

            float footerHeight = 44f;
            DrawList(new Rect(inRect.x, y, inRect.width, Mathf.Max(60f, inRect.yMax - footerHeight - y)));
            DrawFooter(new Rect(inRect.x, inRect.yMax - footerHeight + 6f, inRect.width, footerHeight - 6f));

            if (resortPending && Event.current.type == EventType.Repaint && !Input.GetMouseButton(0)
                && !GUI.GetNameOfFocusedControl().StartsWith("TextField"))
            {
                resortPending = false;
                RebuildRows();
            }
        }

        private void DrawToolbar(Rect rect)
        {
            Rect sortRect = new Rect(rect.xMax - 170f, rect.y, 170f, rect.height);
            if (Widgets.ButtonText(sortRect, "FoodPri.SortBy".Translate(SortLabel(sort))))
            {
                OpenSortMenu();
            }
            Rect filterRect = new Rect(sortRect.x - 156f, rect.y, 150f, rect.height);
            if (Widgets.ButtonText(filterRect, "FoodPri.Show".Translate(FilterLabel(filter))))
            {
                OpenFilterMenu();
            }

            Rect searchRect = new Rect(rect.x, rect.y + 2f, Mathf.Max(60f, filterRect.x - 8f - rect.x), rect.height - 4f);
            string typed = Widgets.TextField(new Rect(searchRect.x, searchRect.y, searchRect.width - 24f, searchRect.height), search);
            if (typed != search)
            {
                search = typed;
                rowsDirty = true;
            }
            if (search.NullOrEmpty())
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(searchRect.x + 6f, searchRect.y, searchRect.width - 30f, searchRect.height), "FoodPri.Search".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonImage(new Rect(searchRect.xMax - 21f, searchRect.y + 3f, 18f, 18f), TexButton.CloseXSmall))
            {
                search = "";
                rowsDirty = true;
                UI.UnfocusCurrentControl();
            }
        }

        private void DrawActions(Rect rect)
        {
            Rect recommendedRect = new Rect(rect.x, rect.y, 200f, rect.height);
            if (Widgets.ButtonText(recommendedRect, "FoodPri.RecommendedDefaults".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("FoodPri.RecommendedDefaultsConfirm".Translate(), delegate
                {
                    FoodPriorityPresets.ApplyRecommended();
                    AfterBulkChange();
                }));
            }
            TooltipHandler.TipRegion(recommendedRect, "FoodPri.RecommendedDefaultsTip".Translate());

            Rect resetRect = new Rect(recommendedRect.xMax + 6f, rect.y, 160f, rect.height);
            if (Widgets.ButtonText(resetRect, "FoodPri.ResetEverything".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("FoodPri.ResetEverythingConfirm".Translate(), delegate
                {
                    FoodPriorityPresets.BackToVanilla();
                    AfterBulkChange();
                }));
            }
            TooltipHandler.TipRegion(resetRect, "FoodPri.ResetEverythingTip".Translate());

            Rect rulesRect = new Rect(resetRect.xMax + 6f, rect.y, 140f, rect.height);
            if (Widgets.ButtonText(rulesRect, "FoodPri.Rules".Translate()))
            {
                Dialog_FoodPriorityRules.Toggle();
            }
            TooltipHandler.TipRegion(rulesRect, "FoodPri.RulesTip".Translate());

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(rulesRect.xMax + 10f, rect.y, rect.xMax - rulesRect.xMax - 10f, rect.height),
                "FoodPri.ChangedCount".Translate(Settings.ChangedCount));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawColumnHeaders(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.5f);

            float right = rect.xMax - 4f;
            right -= ResetWidth + 4f;

            Rect caravanRect = new Rect(right - CaravanWidth, rect.y, CaravanWidth, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            if (eater == Eater.Colonists)
            {
                Widgets.Label(caravanRect, "FoodPri.CaravanHeader".Translate());
                TooltipHandler.TipRegion(caravanRect, "FoodPri.CaravanTip".Translate());
            }
            right -= CaravanWidth + 8f;

            Rect priorityRect = new Rect(right - SliderWidth - NumberWidth - 6f, rect.y, SliderWidth + NumberWidth + 6f, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(priorityRect, (eater == Eater.Animals ? "FoodPri.HeaderAnimalScore" : "FoodPri.HeaderScore").Translate());

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, Mathf.Max(40f, priorityRect.x - rect.x - 8f), rect.height), "FoodPri.HeaderFood".Translate());

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawList(Rect rect)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 18f, rows.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref scrollPosition, viewRect);
            if (rows.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(new Rect(4f, 4f, viewRect.width - 8f, 30f), "FoodPri.NothingMatches".Translate());
                GUI.color = Color.white;
            }
            else
            {
                // Only the rows actually on screen are built, so a thousand-food modlist costs the same as ten.
                int first = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / RowHeight) - 1);
                int last = Mathf.Min(rows.Count, first + Mathf.CeilToInt(rect.height / RowHeight) + 2);
                for (int i = first; i < last; i++)
                {
                    DrawRow(new Rect(0f, i * RowHeight, viewRect.width, RowHeight), rows[i], i);
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect rect, ThingDef def, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }
            Widgets.DrawHighlightIfMouseover(rect);

            Rect row = new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, rect.height - 6f);
            float controlY = row.y + (row.height - 24f) / 2f;
            float x = row.x;

            if (sort == SortMode.Score)
            {
                float arrowHeight = row.height / 2f - 1f;
                if (index > 0 && Widgets.ButtonImage(new Rect(x, row.y, ArrowWidth - 2f, arrowHeight), TexButton.ReorderUp))
                {
                    SwapRanks(index, index - 1);
                }
                if (index < rows.Count - 1 && Widgets.ButtonImage(new Rect(x, row.yMax - arrowHeight, ArrowWidth - 2f, arrowHeight), TexButton.ReorderDown))
                {
                    SwapRanks(index, index + 1);
                }
                x += ArrowWidth;
            }

            DrawIcon(new Rect(x, row.y, row.height, row.height), def);
            x += IconWidth;

            float value = FoodPriorityScore.Of(def, eater);
            bool handSet = Settings.IsHandSet(def, eater);
            bool fromRule = !handSet && eater == Eater.Colonists && FoodPriorityApplier.TryGetRuleOffset(def, out _);

            float right = row.xMax;
            Rect resetRect = new Rect(right - ResetWidth, controlY, ResetWidth, 24f);
            right -= ResetWidth + 4f;
            if (handSet)
            {
                float fallback = FoodPriorityScore.Fallback(def, eater);
                if (Widgets.ButtonImage(new Rect(resetRect.x + 3f, resetRect.y + 3f, 18f, 18f), TexButton.Delete))
                {
                    Settings.ClearOffset(def, eater);
                    buffers.Remove(def.defName);
                    ValueChanged();
                    value = fallback;
                }
                TooltipHandler.TipRegion(resetRect, "FoodPri.ResetToDefault".Translate(fallback.ToString("0.#")));
            }

            // Caravans pick food for colonists only, and not by score, so the tick has no place in the
            // animal view.
            if (eater == Eater.Colonists)
            {
                Rect caravanRect = new Rect(right - CaravanWidth, controlY, CaravanWidth, 24f);
                bool caravanLow = Settings.CaravanLow(def);
                bool before = caravanLow;
                Widgets.Checkbox(caravanRect.center.x - 11f, caravanRect.y + 1f, ref caravanLow, 22f);
                if (caravanLow != before)
                {
                    Settings.SetCaravanLow(def, caravanLow);
                    FoodPriorityApplier.Apply();
                }
                TooltipHandler.TipRegion(caravanRect, "FoodPri.CaravanTip".Translate());
            }
            right -= CaravanWidth + 8f;

            Rect numberRect = new Rect(right - NumberWidth, controlY, NumberWidth, 24f);
            right -= NumberWidth + 6f;
            Rect sliderRect = new Rect(right - SliderWidth, controlY, SliderWidth, 24f);
            right -= SliderWidth + 8f;

            // The slider rounds and clamps whatever it is handed, so an untouched one hands back the
            // rounded value rather than the stored one. Compare against that, or a food sitting at -35.7
            // or at 400 would quietly rewrite itself every frame.
            float shown = Mathf.Clamp(value, -SliderRange, SliderRange);
            float echo = Mathf.Round(shown);
            float slid = Widgets.HorizontalSlider(sliderRect, shown, -SliderRange, SliderRange, true, null, null, null, 1f);
            if (Mathf.Abs(slid - echo) > 0.01f)
            {
                FoodPriorityScore.Set(def, eater, slid);
                buffers.Remove(def.defName);
                ValueChanged();
                value = slid;
            }

            string buffer = buffers.TryGetValue(def.defName, out var existing) ? existing : null;
            float typed = value;
            Widgets.TextFieldNumeric(numberRect, ref typed, ref buffer, -Limit, Limit);
            buffers[def.defName] = buffer;
            if (Mathf.Abs(typed - value) > 0.01f)
            {
                FoodPriorityScore.Set(def, eater, typed);
                ValueChanged();
            }

            Rect labelRect = new Rect(x, row.y, Mathf.Max(60f, right - x), row.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            if (handSet)
            {
                GUI.color = HandSetColor;
            }
            else if (fromRule)
            {
                GUI.color = RuleColor;
            }
            string name = RowLabel(def).Truncate(labelRect.width - 4f);
            Widgets.Label(labelRect, name);
            string source = Settings.showModSource ? SourceOf(def) : "";
            if (source.Length > 0)
            {
                // The mod, greyed out after the name, in whatever room the name leaves.
                float nameWidth = Text.CalcSize(name).x + 8f;
                Rect sourceRect = new Rect(labelRect.x + nameWidth, labelRect.y, labelRect.width - nameWidth - 4f, labelRect.height);
                if (sourceRect.width > 30f)
                {
                    GUI.color = SourceColor;
                    Widgets.Label(sourceRect, source.Truncate(sourceRect.width));
                }
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            TooltipHandler.TipRegion(labelRect, TipGetterFor(def), def.shortHash);
            // A group row stands in for many foods, so there is no one info card to open.
            if (!FoodPriorityApplier.TryGetGroupRow(def, out _) && Widgets.ButtonInvisible(labelRect))
            {
                Find.WindowStack.Add(new Dialog_InfoCard(def));
            }
        }

        private static void DrawIcon(Rect rect, ThingDef def)
        {
            if (FoodPriorityApplier.TryGetGroupRow(def, out var group))
            {
                group.DrawIcon(rect);
                return;
            }
            Widgets.ThingIcon(rect, def);
        }

        private static string RowLabel(ThingDef def)
        {
            return FoodPriorityApplier.TryGetGroupRow(def, out var group) ? group.Label : def.LabelCap.Resolve();
        }

        private void DrawFooter(Rect rect)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(rect, "FoodPri.Footer".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void ValueChanged()
        {
            FoodPriorityApplier.Apply();
            resortPending = true;
        }

        private void AfterBulkChange()
        {
            buffers.Clear();
            FoodPriorityApplier.Apply();
            rowsDirty = true;
        }

        // Moving a food past its neighbour is just the two of them trading places in the ranking.
        private void SwapRanks(int index, int otherIndex)
        {
            ThingDef mine = rows[index];
            ThingDef theirs = rows[otherIndex];
            float mineValue = FoodPriorityScore.Of(mine, eater);
            float theirValue = FoodPriorityScore.Of(theirs, eater);
            if (Mathf.Abs(mineValue - theirValue) < 0.01f)
            {
                // Tied, so there is nothing to trade: step over the neighbour instead. The list runs
                // highest first, so a smaller index means a higher value.
                FoodPriorityScore.Set(mine, eater, index < otherIndex ? theirValue - 1f : theirValue + 1f);
            }
            else
            {
                FoodPriorityScore.Set(mine, eater, theirValue);
                FoodPriorityScore.Set(theirs, eater, mineValue);
            }
            buffers.Remove(mine.defName);
            buffers.Remove(theirs.defName);
            FoodPriorityApplier.Apply();
            rows[index] = theirs;
            rows[otherIndex] = mine;
        }

        // Built only when the tooltip is actually shown, so it can quote the score as it stands rather
        // than a cached one. The delegate per food is cached instead.
        private Func<string> TipGetterFor(ThingDef def)
        {
            if (!tipGetters.TryGetValue(def, out var getter))
            {
                getter = () => TooltipFor(def);
                tipGetters[def] = getter;
            }
            return getter;
        }

        private string TooltipFor(ThingDef def)
        {
            if (FoodPriorityApplier.TryGetGroupRow(def, out var group))
            {
                return GroupTooltip(group);
            }
            string text = def.LabelCap.Resolve() + "\n" + def.defName;
            if (def.modContentPack != null)
            {
                text += "\n" + "FoodPri.TipSource".Translate(def.modContentPack.Name);
            }

            // The sum, then what it is made of, so it is clear why one food beats another.
            text += "\n\n" + (eater == Eater.Animals ? "FoodPri.TipAnimalScore" : "FoodPri.TipScore")
                .Translate(FoodPriorityScore.Of(def, eater).ToString("0.#"));
            text += "\n" + "FoodPri.TipScorePriority".Translate(Signed(Settings.Offset(def, eater)));
            if (eater == Eater.Colonists)
            {
                float taste = FoodPriorityScore.Taste(def);
                if (Mathf.Abs(taste) > 0.01f)
                {
                    text += "\n" + "FoodPri.TipScoreFlavour".Translate(Signed(taste));
                }
            }
            float emergency = FoodPriorityScore.Emergency(def, eater);
            if (Mathf.Abs(emergency) > 0.01f)
            {
                text += "\n" + "FoodPri.TipScoreEmergency".Translate(Signed(emergency));
            }
            text += "\n" + SourceLine(def);
            text += "\n" + "FoodPri.TipShipped".Translate(FoodPriorityScore.Shipped(def, eater).ToString("0.#"));

            text += "\n\n" + "FoodPri.TipPreferability".Translate(def.ingestible.preferability.ToString());
            text += "\n" + "FoodPri.TipNutrition".Translate(def.ingestible.CachedNutrition.ToString("0.##"));
            CompProperties_Rottable rottable = def.GetCompProperties<CompProperties_Rottable>();
            text += "\n" + (rottable != null
                ? "FoodPri.TipShelfLife".Translate(rottable.daysToRotStart.ToString("0.#")).ToString()
                : "FoodPri.TipNeverRots".Translate().ToString());
            if (def.ingestible.preferability == FoodPreferability.NeverForNutrition)
            {
                text += "\n\n" + "FoodPri.TipNeverEaten".Translate();
            }
            return text;
        }

        // One value for the whole group, but what it adds up to can still differ between members - eating
        // a person carries its own mood hit on top of eating a corpse. So the tooltip gives the range.
        private string GroupTooltip(FoodGroup group)
        {
            List<ThingDef> members = group.members;
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < members.Count; i++)
            {
                float score = FoodPriorityScore.Of(members[i], eater);
                min = Mathf.Min(min, score);
                max = Mathf.Max(max, score);
            }
            ThingDef proxy = group.proxy;
            string text = group.Label + "\n" + group.Tip;
            text += "\n\n" + (Mathf.Abs(max - min) < 0.05f
                ? "FoodPri.TipScore".Translate(min.ToString("0.#")).ToString()
                : "FoodPri.GroupScoreRange".Translate(min.ToString("0.#"), max.ToString("0.#")).ToString());
            text += "\n" + "FoodPri.TipScorePriority".Translate(Signed(Settings.Offset(proxy, eater)));
            text += "\n" + SourceLine(proxy);
            return text;
        }

        private string SourceLine(ThingDef def)
        {
            if (Settings.IsHandSet(def, eater))
            {
                return "FoodPri.TipSetByHand".Translate();
            }
            if (eater == Eater.Colonists && FoodPriorityApplier.TryGetRuleOffset(def, out _))
            {
                return "FoodPri.TipSetByRule".Translate(TierLabel(FoodPriorityApplier.TierOf(def)));
            }
            return "FoodPri.TipAsShipped".Translate();
        }

        public static string TierLabel(FoodTier tier)
        {
            return ("FoodPri.Tier" + tier).Translate();
        }

        private static string Signed(float value)
        {
            return value.ToString("+0.#;-0.#;0");
        }

        private void RebuildRows()
        {
            rows.Clear();
            string query = search.Trim();
            List<ThingDef> all = FoodPriorityApplier.ListedFoods;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (filter == FilterMode.Changed)
                {
                    if (!Settings.IsChanged(def))
                    {
                        continue;
                    }
                }
                else if (filter != FilterMode.All && CategoryOf(def) != filter)
                {
                    continue;
                }
                if (query.Length > 0 && !Matches(def, query))
                {
                    continue;
                }
                rows.Add(def);
            }
            rows.Sort(CompareRows);
        }

        private bool Matches(ThingDef def, string query)
        {
            if (FoodPriorityApplier.TryGetGroupRow(def, out _))
            {
                return RowLabel(def).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            return (def.label != null && def.label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                || def.defName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (Settings.showModSource && SourceOf(def).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private int CompareRows(ThingDef a, ThingDef b)
        {
            switch (sort)
            {
                case SortMode.Name:
                    return string.Compare(LabelOf(a), LabelOf(b), StringComparison.CurrentCultureIgnoreCase);
                case SortMode.Category:
                {
                    int byCategory = CategoryOf(a).CompareTo(CategoryOf(b));
                    if (byCategory != 0)
                    {
                        return byCategory;
                    }
                    break;
                }
                case SortMode.Source:
                {
                    int bySource = string.Compare(SourceOf(a), SourceOf(b), StringComparison.CurrentCultureIgnoreCase);
                    if (bySource != 0)
                    {
                        return bySource;
                    }
                    break;
                }
            }
            int byValue = FoodPriorityScore.Of(b, eater).CompareTo(FoodPriorityScore.Of(a, eater));
            if (byValue != 0)
            {
                return byValue;
            }
            return string.Compare(LabelOf(a), LabelOf(b), StringComparison.CurrentCultureIgnoreCase);
        }

        private static string LabelOf(ThingDef def)
        {
            if (FoodPriorityApplier.TryGetGroupRow(def, out _))
            {
                return RowLabel(def);
            }
            return def.label.NullOrEmpty() ? def.defName : def.label;
        }

        // A group's members come from every mod that adds a creature, so a group row has no single source.
        private static string SourceOf(ThingDef def)
        {
            return !FoodPriorityApplier.TryGetGroupRow(def, out _) && def.modContentPack != null ? def.modContentPack.Name : "";
        }

        private static FilterMode CategoryOf(ThingDef def)
        {
            if (def.IsCorpse)
            {
                return FilterMode.Other;
            }
            if (def.IsDrug)
            {
                return FilterMode.Drugs;
            }
            FoodPreferability preferability = def.ingestible.preferability;
            if ((int)preferability >= (int)FoodPreferability.MealTerrible)
            {
                return FilterMode.Meals;
            }
            if (preferability == FoodPreferability.RawBad || preferability == FoodPreferability.RawTasty)
            {
                return FilterMode.Raw;
            }
            return FilterMode.Other;
        }

        private void OpenFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (FilterMode mode in (FilterMode[])Enum.GetValues(typeof(FilterMode)))
            {
                FilterMode local = mode;
                options.Add(new FloatMenuOption(FilterLabel(local), delegate
                {
                    filter = local;
                    rowsDirty = true;
                    scrollPosition = Vector2.zero;
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenSortMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (SortMode mode in (SortMode[])Enum.GetValues(typeof(SortMode)))
            {
                if (mode == SortMode.Source && !Settings.showModSource)
                {
                    continue;
                }
                SortMode local = mode;
                options.Add(new FloatMenuOption(SortLabel(local), delegate
                {
                    sort = local;
                    rowsDirty = true;
                    scrollPosition = Vector2.zero;
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void DrawEaterTabs(Rect section)
        {
            if (eaterTabs == null)
            {
                eaterTabs = new List<TabRecord>();
                foreach (Eater mode in (Eater[])Enum.GetValues(typeof(Eater)))
                {
                    Eater local = mode;
                    eaterTabs.Add(new TabRecord(EaterLabel(local), () => SetEater(local), () => eater == local));
                }
            }
            TabDrawer.DrawTabs(section, eaterTabs);

            // TabDrawer has no tooltips of its own; these rects are the ones it lays the tabs out in.
            float tabWidth = Mathf.Min(200f, (section.width + (eaterTabs.Count - 1) * TabDrawer.TabHoriztonalOverlap) / eaterTabs.Count);
            for (int i = 0; i < eaterTabs.Count; i++)
            {
                Rect tabRect = new Rect(section.x + i * (tabWidth - TabDrawer.TabHoriztonalOverlap), section.y - TabDrawer.TabHeight, tabWidth, TabDrawer.TabHeight);
                TooltipHandler.TipRegion(tabRect, ("FoodPri.Eater" + (Eater)i + "Tip").Translate());
            }
        }

        private void SetEater(Eater mode)
        {
            if (eater == mode)
            {
                return;
            }
            eater = mode;
            // Every buffered number is the other tab's value.
            buffers.Clear();
            rowsDirty = true;
        }

        private static string EaterLabel(Eater mode)
        {
            return ("FoodPri.Eater" + mode).Translate();
        }

        private static string FilterLabel(FilterMode mode)
        {
            return ("FoodPri.Filter" + mode).Translate();
        }

        private static string SortLabel(SortMode mode)
        {
            return ("FoodPri.Sort" + mode).Translate();
        }
    }
}
