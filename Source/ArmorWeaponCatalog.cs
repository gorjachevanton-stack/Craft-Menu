using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace BlocksMenu
{
    /// <summary>
    /// Dynamic armor and weapon catalogs. Categories are discovered from loaded game Defs,
    /// so content added by other mods is included without hard-coded item lists.
    /// </summary>
    internal static class ArmorWeaponCatalog
    {
        private static readonly Color Panel = new Color(0.105f, 0.095f, 0.080f, 0.99f);
        private static readonly Color Raised = new Color(0.155f, 0.137f, 0.108f, 0.99f);
        private static readonly Color Border = new Color(0.38f, 0.315f, 0.205f, 0.96f);
        private static readonly Color Accent = new Color(0.88f, 0.66f, 0.30f, 1f);
        private static readonly Color AccentSoft = new Color(0.43f, 0.29f, 0.12f, 0.62f);

        private static string armorGroup = "Все";
        private static ThingDef selectedArmor;
        private static Vector2 armorScroll;
        private static string weaponGroup = "Ближний бой";
        private static ThingDef selectedWeapon;
        private static Vector2 weaponScroll;
        private static List<ThingDef> cachedArmor;
        private static List<string> cachedArmorGroups;
        private static int cachedArmorDefCount = -1;
        private static Map cachedArmorMap;
        private static int cachedArmorTick = -1;
        private static Dictionary<string, List<ThingDef>> armorByGroup;
        private static Dictionary<ThingDef, List<string>> coveredGroupsByDef;
        private static List<ThingDef> cachedMelee;
        private static List<ThingDef> cachedRanged;
        private static List<ThingDef> cachedTurrets;
        private static int cachedWeaponDefCount = -1;
        private static Map cachedWeaponMap;
        private static int cachedWeaponTick = -1;

        public static void DrawArmor(Rect rect, Action<ThingDef> produce)
        {
            List<ThingDef> items = GetArmorForGroup(armorGroup);
            if (selectedArmor == null || !items.Contains(selectedArmor))
                selectedArmor = items.Count > 0 ? items[0] : null;
            DrawThreeColumns(rect, out Rect left, out Rect center, out Rect details);
            DrawPanel(left); DrawPanel(center); DrawPanel(details);
            DrawArmorGroups(left);
            DrawItemCards(center, items, ref armorScroll, selectedArmor, delegate(ThingDef item) { selectedArmor = item; }, "БРОНЯ И ЗАЩИТА");
            DrawArmorDetails(details, selectedArmor, produce);
        }

        public static void DrawWeapons(Rect rect, Action<ThingDef> produce)
        {
            List<ThingDef> items = GetWeaponsForGroup(weaponGroup);
            if (selectedWeapon == null || !items.Contains(selectedWeapon))
                selectedWeapon = items.Count > 0 ? items[0] : null;
            DrawThreeColumns(rect, out Rect left, out Rect center, out Rect details);
            DrawPanel(left); DrawPanel(center); DrawPanel(details);
            DrawWeaponGroups(left);
            DrawItemCards(center, items, ref weaponScroll, selectedWeapon, delegate(ThingDef item) { selectedWeapon = item; }, "ОРУЖЕЙНЫЙ АРСЕНАЛ");
            DrawWeaponDetails(details, selectedWeapon, produce);
        }

        private static void DrawThreeColumns(Rect rect, out Rect left, out Rect center, out Rect details)
        {
            const float gap = 8f;
            float leftWidth = Mathf.Clamp(rect.width * 0.235f, 190f, 270f);
            float detailsWidth = Mathf.Clamp(rect.width * 0.285f, 270f, 350f);
            float centerWidth = rect.width - leftWidth - detailsWidth - gap * 2f;
            left = new Rect(rect.x, rect.y, leftWidth, rect.height);
            center = new Rect(left.xMax + gap, rect.y, centerWidth, rect.height);
            details = new Rect(center.xMax + gap, rect.y, detailsWidth, rect.height);
        }

        private static void DrawArmorGroups(Rect rect)
        {
            DrawHeading(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), "ЧАСТИ ТЕЛА");
            List<string> groups = GetArmorGroups();
            float y = rect.y + 42f;
            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                Rect row = new Rect(rect.x + 7f, y, rect.width - 14f, 36f);
                if (armorGroup == group) Widgets.DrawBoxSolid(row, AccentSoft);
                if (Widgets.ButtonText(row, group))
                {
                    armorGroup = group;
                    armorScroll = Vector2.zero;
                    List<ThingDef> found = GetArmorForGroup(group);
                    selectedArmor = found.Count > 0 ? found[0] : null;
                }
                int count = GetArmorForGroup(group).Count;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.xMax - 34f, row.y, 26f, row.height), count.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                y += 40f;
            }
        }

        private static void DrawWeaponGroups(Rect rect)
        {
            DrawHeading(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), "ТИП ОРУЖИЯ");
            string[] groups = { "Ближний бой", "Дальний бой", "Турели" };
            float y = rect.y + 42f;
            for (int i = 0; i < groups.Length; i++)
            {
                string group = groups[i];
                Rect row = new Rect(rect.x + 7f, y, rect.width - 14f, 40f);
                if (weaponGroup == group) Widgets.DrawBoxSolid(row, AccentSoft);
                if (Widgets.ButtonText(row, group))
                {
                    weaponGroup = group;
                    weaponScroll = Vector2.zero;
                    List<ThingDef> found = GetWeaponsForGroup(group);
                    selectedWeapon = found.Count > 0 ? found[0] : null;
                }
                int count = GetWeaponsForGroup(group).Count;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.xMax - 34f, row.y, 26f, row.height), count.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                y += 44f;
            }
        }

        private static void DrawItemCards(Rect rect, List<ThingDef> items, ref Vector2 scroll, ThingDef selected,
            Action<ThingDef> select, string heading)
        {
            DrawHeading(new Rect(rect.x + 10f, rect.y + 7f, rect.width - 20f, 24f), heading + "  ·  " + items.Count);
            const float cardGap = 7f;
            int columns = rect.width < 410f ? 2 : rect.width < 650f ? 3 : 4;
            float cardWidth = (rect.width - 16f - cardGap * (columns - 1)) / columns;
            float cardHeight = Mathf.Clamp(cardWidth * 1.28f, 164f, 190f);
            int rows = (items.Count + columns - 1) / columns;
            Rect listRect = new Rect(rect.x + 4f, rect.y + 34f, rect.width - 8f, rect.height - 39f);
            float viewHeight = Mathf.Max(listRect.height, rows * (cardHeight + cardGap) + 8f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            if (items.Count == 0)
                Widgets.Label(new Rect(8f, 8f, viewRect.width - 16f, 48f), "В этой категории предметы не найдены.");
            else
            {
                int firstRow = Mathf.Max(0, Mathf.FloorToInt(scroll.y / (cardHeight + cardGap)));
                int lastRow = Mathf.Min(rows, Mathf.CeilToInt((scroll.y + listRect.height) / (cardHeight + cardGap)) + 1);
                for (int row = firstRow; row < lastRow; row++)
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= items.Count) break;
                    ThingDef item = items[index];
                    Rect card = new Rect(5f + col * (cardWidth + cardGap), 4f + row * (cardHeight + cardGap), cardWidth, cardHeight);
                    bool active = selected == item;
                    Widgets.DrawBoxSolid(card, active ? new Color(0.20f, 0.15f, 0.08f, 0.98f) : Raised);
                    Color old = GUI.color;
                    GUI.color = active ? Accent : Border;
                    Widgets.DrawBox(card, 1);
                    GUI.color = old;
                    float iconSize = Mathf.Min(card.width * 0.55f, card.height * 0.36f);
                    Widgets.ThingIcon(new Rect(card.center.x - iconSize / 2f, card.y + 8f, iconSize, iconSize), item);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(card.x + 5f, card.y + iconSize + 15f, card.width - 10f, 42f), item.LabelCap.ToString());
                    Text.Anchor = TextAnchor.UpperLeft;
                    string meta = item.BaseMass > 0f ? "Вес: " + item.BaseMass.ToString("0.##") + " кг" : "";
                    Widgets.Label(new Rect(card.x + 8f, card.yMax - 28f, card.width - 16f, 20f), meta);
                    if (Widgets.ButtonInvisible(card)) select(item);
                }
            }
            Widgets.EndScrollView();
        }

        private static void DrawArmorDetails(Rect rect, ThingDef item, Action<ThingDef> produce)
        {
            if (item == null)
            {
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 60f), "Выбери предмет в каталоге.");
                return;
            }
            float y = DrawItemHeader(rect, item, item.apparel != null && IsShield(item) ? "Щит" : "Броня / защитная экипировка");
            DrawHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 23f), "ЗАКРЫВАЕТ ЧАСТИ ТЕЛА");
            y += 26f;
            List<string> covered = GetCoveredBodyGroups(item);
            if (covered.Count == 0)
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 42f), "Игровое определение не содержит групп покрытия.");
            else
                for (int i = 0; i < covered.Count && y + 22f < rect.yMax - 100f; i++, y += 22f)
                    Widgets.Label(new Rect(rect.x + 16f, y, rect.width - 32f, 21f), "• " + covered[i]);
            y += 6f;
            DrawHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 23f), "ЗАЩИТНЫЕ ХАРАКТЕРИСТИКИ");
            y += 26f;
            DrawRelevantStats(rect, item, ref y, true);
            if (item.BaseMass > 0f)
                Widgets.Label(new Rect(rect.x + 14f, Mathf.Min(y, rect.yMax - 88f), rect.width - 28f, 22f), "Вес: " + item.BaseMass.ToString("0.##") + " кг");
            DrawProduceButton(rect, item, produce);
        }

        private static void DrawWeaponDetails(Rect rect, ThingDef item, Action<ThingDef> produce)
        {
            if (item == null)
            {
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 60f), "Выбери предмет в каталоге.");
                return;
            }
            float y = DrawItemHeader(rect, item, weaponGroup);
            DrawHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 23f), weaponGroup == "Турели" ? "ХАРАКТЕРИСТИКИ ТУРЕЛИ" : "БОЕВЫЕ ХАРАКТЕРИСТИКИ");
            y += 27f;
            DrawRelevantStats(rect, item, ref y, false);
            if (item.BaseMass > 0f)
                Widgets.Label(new Rect(rect.x + 14f, Mathf.Min(y, rect.yMax - 88f), rect.width - 28f, 22f), "Вес: " + item.BaseMass.ToString("0.##") + " кг");
            DrawProduceButton(rect, item, produce);
        }

        private static float DrawItemHeader(Rect rect, ThingDef item, string subtitle)
        {
            float y = rect.y + 10f;
            float icon = Mathf.Min(92f, rect.width * 0.30f);
            Widgets.ThingIcon(new Rect(rect.x + 12f, y, icon, icon), item);
            Widgets.Label(new Rect(rect.x + icon + 22f, y + 4f, rect.width - icon - 36f, 56f), item.LabelCap.ToString());
            Widgets.Label(new Rect(rect.x + icon + 22f, y + 59f, rect.width - icon - 36f, 24f), subtitle);
            y += icon + 12f;
            Widgets.DrawLineHorizontal(rect.x + 10f, y, rect.width - 20f);
            return y + 10f;
        }

        private static void DrawRelevantStats(Rect rect, ThingDef item, ref float y, bool armor)
        {
            if (item.statBases == null) return;
            int shown = 0;
            for (int i = 0; i < item.statBases.Count && shown < 12 && y + 22f < rect.yMax - 82f; i++)
            {
                StatModifier modifier = item.statBases[i];
                if (modifier == null || modifier.stat == null) continue;
                string key = (modifier.stat.defName + " " + modifier.stat.label).ToLowerInvariant();
                bool relevant = armor
                    ? ContainsAny(key, "armor", "sharp", "blunt", "heat", "cold", "заш", "брон", "удар", "порез", "температур")
                    : ContainsAny(key, "damage", "dps", "cooldown", "range", "accuracy", "armorpenetration", "заш", "урон", "дальность", "точност", "скоростр", "перезаряд");
                if (!relevant) continue;
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 21f), modifier.stat.LabelCap + "   " + modifier.value.ToString("0.##"));
                y += 23f;
                shown++;
            }
            if (shown == 0)
            {
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 38f), "В ThingDef не найдены подходящие базовые StatDef.");
                y += 40f;
            }
        }

        private static void DrawProduceButton(Rect rect, ThingDef item, Action<ThingDef> produce)
        {
            Rect button = new Rect(rect.x + 12f, rect.yMax - 58f, rect.width - 24f, 42f);
            if (Widgets.ButtonText(button, "⚒  ПРОИЗВЕСТИ") && produce != null) produce(item);
        }

        private static void EnsureArmorCache()
        {
            int count = DefDatabase<ThingDef>.AllDefsListForReading.Count;
            Map map = Find.CurrentMap;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (cachedArmor != null && cachedArmorDefCount == count && cachedArmorMap == map && armorByGroup != null &&
                cachedArmorTick >= 0 && tick - cachedArmorTick < 300)
                return;

            cachedArmorDefCount = count;
            cachedArmorMap = map;
            cachedArmorTick = tick;
            cachedArmor = new List<ThingDef>();
            cachedArmorGroups = new List<string> { "Все", "Щиты" };
            armorByGroup = new Dictionary<string, List<ThingDef>>(StringComparer.OrdinalIgnoreCase);
            coveredGroupsByDef = new Dictionary<ThingDef, List<string>>();
            armorByGroup["Все"] = new List<ThingDef>();
            armorByGroup["Щиты"] = new List<ThingDef>();

            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            HashSet<string> discoveredGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (map == null || def == null || def.apparel == null || !RecipeBenchFinder.CanProduceOnMap(map, def))
                    continue;

                bool shield = IsShield(def);
                List<string> groups = GetCoveredBodyGroupsUncached(def);
                coveredGroupsByDef[def] = groups;
                if (!shield && groups.Count == 0)
                    continue;

                cachedArmor.Add(def);
                armorByGroup["Все"].Add(def);
                if (shield)
                    armorByGroup["Щиты"].Add(def);
                else
                {
                    for (int g = 0; g < groups.Count; g++)
                    {
                        string group = groups[g];
                        List<ThingDef> groupItems;
                        if (!armorByGroup.TryGetValue(group, out groupItems))
                        {
                            groupItems = new List<ThingDef>();
                            armorByGroup.Add(group, groupItems);
                            discoveredGroups.Add(group);
                        }
                        groupItems.Add(def);
                    }
                }
            }

            foreach (string group in discoveredGroups)
                cachedArmorGroups.Add(group);
            if (cachedArmorGroups.Count > 2)
            {
                List<string> tail = cachedArmorGroups.GetRange(2, cachedArmorGroups.Count - 2);
                tail.Sort(StringComparer.CurrentCultureIgnoreCase);
                cachedArmorGroups.RemoveRange(2, cachedArmorGroups.Count - 2);
                cachedArmorGroups.AddRange(tail);
            }

            foreach (List<ThingDef> groupItems in armorByGroup.Values)
                SortByLabel(groupItems);
        }

        private static List<ThingDef> GetArmorItems()
        {
            EnsureArmorCache();
            return cachedArmor;
        }

        private static List<string> GetArmorGroups()
        {
            EnsureArmorCache();
            return cachedArmorGroups;
        }

        private static List<ThingDef> GetArmorForGroup(string group)
        {
            EnsureArmorCache();
            List<ThingDef> result;
            return armorByGroup.TryGetValue(group ?? "Все", out result) ? result : armorByGroup["Все"];
        }

        private static List<string> GetCoveredBodyGroups(ThingDef item)
        {
            if (item == null)
                return new List<string>();
            EnsureArmorCache();
            List<string> groups;
            if (coveredGroupsByDef != null && coveredGroupsByDef.TryGetValue(item, out groups))
                return groups;
            return GetCoveredBodyGroupsUncached(item);
        }

        private static List<string> GetCoveredBodyGroupsUncached(ThingDef item)
        {
            List<string> result = new List<string>();
            if (item == null || item.apparel == null || item.apparel.bodyPartGroups == null)
                return result;
            for (int i = 0; i < item.apparel.bodyPartGroups.Count; i++)
            {
                object group = item.apparel.bodyPartGroups[i];
                if (group == null) continue;
                string label = ReadString(group, "LabelCap") ?? ReadString(group, "label") ?? ReadString(group, "defName");
                if (!string.IsNullOrEmpty(label) && !result.Contains(label)) result.Add(label);
            }
            result.Sort(StringComparer.CurrentCultureIgnoreCase);
            return result;
        }

        private static bool IsShield(ThingDef def)
        {
            if (def == null) return false;
            string id = (def.defName + " " + def.label).ToLowerInvariant();
            if (ContainsAny(id, "shield", "buckler", "щит")) return true;
            object comps = ReadMember(def, "comps");
            IEnumerable enumerable = comps as IEnumerable;
            if (enumerable != null)
                foreach (object comp in enumerable)
                {
                    object compClass = ReadMember(comp, "compClass");
                    if (compClass != null && compClass.ToString().IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    if (comp != null && comp.GetType().Name.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            return false;
        }

        private static List<ThingDef> GetWeaponsForGroup(string group)
        {
            EnsureWeaponCache();
            if (group == "Ближний бой") return cachedMelee;
            if (group == "Дальний бой") return cachedRanged;
            return cachedTurrets;
        }

        private static void EnsureWeaponCache()
        {
            int count = DefDatabase<ThingDef>.AllDefsListForReading.Count;
            Map map = Find.CurrentMap;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (cachedMelee != null && cachedRanged != null && cachedTurrets != null && cachedWeaponDefCount == count &&
                cachedWeaponMap == map && cachedWeaponTick >= 0 && tick - cachedWeaponTick < 300) return;
            cachedWeaponDefCount = count;
            cachedWeaponMap = map;
            cachedWeaponTick = tick;
            cachedMelee = new List<ThingDef>();
            cachedRanged = new List<ThingDef>();
            cachedTurrets = new List<ThingDef>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (map == null || def == null || def.apparel != null || !RecipeBenchFinder.CanProduceOnMap(map, def)) continue;
                if (IsTurret(def)) cachedTurrets.Add(def);
                else if (IsWeaponKind(def, "IsMeleeWeapon", "melee")) cachedMelee.Add(def);
                else if (IsWeaponKind(def, "IsRangedWeapon", "ranged")) cachedRanged.Add(def);
            }
            SortByLabel(cachedMelee); SortByLabel(cachedRanged); SortByLabel(cachedTurrets);
        }

        private static bool IsTurret(ThingDef def)
        {
            object building = ReadMember(def, "building");
            if (building == null) return false;
            object gun = ReadMember(building, "turretGunDef") ?? ReadMember(building, "turretGunDefName");
            if (gun != null) return true;
            string id = (def.defName + " " + def.label).ToLowerInvariant();
            return ContainsAny(id, "turret", "турель") && ReadMember(building, "turretGunDef") != null;
        }

        private static bool IsWeaponKind(ThingDef def, string boolMember, string fallback)
        {
            object flag = ReadMember(def, boolMember);
            if (flag is bool && (bool)flag) return true;
            object tags = ReadMember(def, "weaponTags");
            object tools = ReadMember(def, "tools");
            object verbs = ReadMember(def, "verbs");
            object weaponFlag = ReadMember(def, "IsWeapon");
            bool explicitlyWeapon = weaponFlag is bool && (bool)weaponFlag;
            bool hasWeaponData = explicitlyWeapon || HasItems(tags) || HasItems(tools) || HasItems(verbs);
            if (!hasWeaponData) return false;
            string id = (def.defName + " " + def.label).ToLowerInvariant();
            bool hasLongRangeVerb = HasRangedVerb(verbs);
            if (fallback == "melee")
            {
                if (hasLongRangeVerb) return false;
                return explicitlyWeapon || (HasItems(tools) && (HasItems(tags) || HasItems(verbs) || ContainsAny(id, "melee", "sword", "knife", "axe", "club", "spear", "меч", "топор", "копь", "дубин", "нож")));
            }
            if (hasLongRangeVerb) return true;
            if (ReadMember(def, "IsMeleeWeapon") is bool && (bool)ReadMember(def, "IsMeleeWeapon")) return false;
            return HasItems(verbs) && ContainsAny(id, "gun", "rifle", "pistol", "shotgun", "bow", "launcher", "винтов", "пистолет", "руж", "лук", "огнемет");
        }

        private static bool HasRangedVerb(object verbs)
        {
            IEnumerable enumerable = verbs as IEnumerable;
            if (enumerable == null) return false;
            foreach (object verb in enumerable)
            {
                object range = ReadMember(verb, "range");
                if (range == null) continue;
                try { if (Convert.ToSingle(range) > 1.5f) return true; }
                catch { }
            }
            return false;
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(instance, null);
                FieldInfo field = type.GetField(name, flags);
                if (field != null) return field.GetValue(instance);
            }
            catch { }
            return null;
        }

        private static string ReadString(object instance, string name)
        {
            object value = ReadMember(instance, name);
            return value == null ? null : value.ToString();
        }

        private static bool HasItems(object value)
        {
            ICollection collection = value as ICollection;
            if (collection != null) return collection.Count > 0;
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null) return false;
            IEnumerator iterator = enumerable.GetEnumerator();
            try { return iterator.MoveNext(); }
            finally { IDisposable disposable = iterator as IDisposable; if (disposable != null) disposable.Dispose(); }
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < terms.Length; i++) if (value.IndexOf(terms[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void SortByLabel(List<ThingDef> items)
        {
            items.Sort(delegate(ThingDef a, ThingDef b) { return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase); });
        }

        private static void DrawPanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, Panel);
            Color old = GUI.color;
            GUI.color = Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = old;
        }

        private static void DrawHeading(Rect rect, string text)
        {
            Color old = GUI.color;
            GUI.color = Accent;
            Widgets.Label(rect, text);
            GUI.color = old;
        }
    }
}
