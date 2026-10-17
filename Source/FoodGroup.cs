using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FoodPriority
{
    // Foods that come in one variant per creature - corpses, eggs - and that nobody needs to rank one by
    // one. The editor shows a group as a single row; whatever is set on it is stored under the group's
    // key and written into every member. Left unset, each member keeps its own value.
    public class FoodGroup
    {
        // Not a defName, so it can never collide with one.
        public readonly string key;
        private readonly string labelKey;
        private readonly string tipKey;
        private readonly Func<ThingDef, bool> includes;
        private readonly string preferredProxy;
        private readonly string iconPath;

        public readonly List<ThingDef> members = new List<ThingDef>();
        // The member the row is drawn and scored from.
        public ThingDef proxy;

        private Texture2D icon;
        private bool iconLooked;

        public FoodGroup(string key, string labelKey, string tipKey, Func<ThingDef, bool> includes,
            string preferredProxy = null, string iconPath = null)
        {
            this.key = key;
            this.labelKey = labelKey;
            this.tipKey = tipKey;
            this.includes = includes;
            this.preferredProxy = preferredProxy;
            this.iconPath = iconPath;
        }

        public string Label => labelKey.Translate();

        public string Tip => tipKey.Translate(members.Count);

        public bool Includes(ThingDef def)
        {
            return includes(def);
        }

        // Called once every member is in: the preferred member if it is loaded, else the first.
        public void PickProxy(Func<ThingDef, bool> preferred = null)
        {
            if (members.Count == 0)
            {
                return;
            }
            if (preferredProxy != null)
            {
                proxy = members.Find(d => d.defName == preferredProxy);
            }
            if (proxy == null && preferred != null)
            {
                proxy = members.Find(d => preferred(d));
            }
            proxy ??= members[0];
        }

        public void DrawIcon(Rect rect)
        {
            if (iconPath != null && !iconLooked)
            {
                iconLooked = true;
                icon = ContentFinder<Texture2D>.Get(iconPath, reportFailure: false);
            }
            if (icon != null)
            {
                Widgets.DrawTextureFitted(rect, icon, 0.85f);
            }
            else
            {
                Widgets.ThingIcon(rect, proxy);
            }
        }
    }
}
