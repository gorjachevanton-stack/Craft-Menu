using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace BlocksMenu
{
    public class MainTabWindow_CraftingMenu : MainTabWindow
    {
        // Built-in production folders mirror the existing catalog tabs. These keys are
        // intentionally not user-editable folder names and are never serialized.
        private const string SpecialWearFolderKey = "__BlocksMenu_SpecialWear";
        private const string ClothingFolderKey = "__BlocksMenu_Clothing";

        // BlocksMenu visual theme: graphite, worn bronze and restrained amber.
        // These values affect drawing only; production and Bill logic remain untouched.
        private static readonly Color DashboardBackground = new Color(0.055f, 0.050f, 0.043f, 0.99f);
        private static readonly Color DashboardPanel = new Color(0.105f, 0.095f, 0.080f, 0.99f);
        private static readonly Color DashboardPanelRaised = new Color(0.155f, 0.137f, 0.108f, 0.99f);
        private static readonly Color DashboardBorder = new Color(0.38f, 0.315f, 0.205f, 0.96f);
        private static readonly Color DashboardAccent = new Color(0.88f, 0.66f, 0.30f, 1f);
        private static readonly Color DashboardAccentSoft = new Color(0.43f, 0.29f, 0.12f, 0.62f);
        private static readonly Color DashboardGreen = new Color(0.42f, 0.76f, 0.38f, 1f);
        private static readonly Color DashboardPaused = new Color(0.62f, 0.16f, 0.12f, 0.42f);

        private string currentFolder = string.Empty; // пустая строка = «Все»
        private string activeMainCategory = "Production";
        private string selectedProfession = "All Professions";
        private string selectedClothingGroup = "All";
        private string selectedClothingFilter = "CarryBulk";
        private ThingDef selectedClothingItem;
        private Vector2 clothingScrollPosition = Vector2.zero;
        private static List<ThingDef> cachedClothingItems;
        private static int cachedClothingDefsCount = -1;
        private static Map cachedClothingMap;
        private static int cachedClothingTick = -1;
        private ThingDef selectedWorkwearItem;
        private Vector2 workwearScrollPosition = Vector2.zero;
        private static List<ThingDef> cachedWorkwearItems;
        private static int cachedWorkwearDefsCount = -1;
        private static Map cachedWorkwearMap;
        private static int cachedWorkwearTick = -1;
        private bool showHiddenRecipes = false;
        private string newFolderName = string.Empty;
        private Vector2 recipeScrollPosition = Vector2.zero;
        private Vector2 queueScrollPosition = Vector2.zero;
        private readonly Dictionary<int, Vector2> nativeBillScrollPositions = new Dictionary<int, Vector2>();
        private RecipeDef selectedRecipe;
        private string searchText = string.Empty;
        private string countText = "1";
        private ProductionOrderMode selectedOrderMode = ProductionOrderMode.MakeCount;
        private string targetText = "200";
        private string resumeAtText = "50";

        // Performance cache: expensive catalog and bench scans must not run from every GUI repaint.
        private List<RecipeDef> cachedRecipes = new List<RecipeDef>();
        private string cachedSearchText = null;
        private string cachedFolderName = null;
        private bool cachedShowHiddenRecipes;
        private Map cachedRecipesMap;
        private int cachedRecipesTick = -1;
        private RecipeDef cachedBenchRecipe;
        private Map cachedBenchMap;
        private List<Building> cachedSelectedBenches = new List<Building>();
        private int cachedBenchesTick = -1;
        private List<Building> cachedFolderBenches = new List<Building>();
        private Map cachedFolderBenchesMap;
        private string cachedFolderBenchesName;
        private int cachedFolderBenchesRecipeTick = -1;
        private static readonly Dictionary<RecipeDef, string> IngredientTooltipCache = new Dictionary<RecipeDef, string>();

        private List<BlocksMenuProductionOrder> ProductionQueue
        {
            get
            {
                GameComponent_BlocksMenuQueue component = Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
                return component != null ? component.Orders : new List<BlocksMenuProductionOrder>();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            const float gap = 8f;
            const float headerHeight = 66f;
            const float folderHeight = 64f;
            DrawPanel(inRect, DashboardBackground);
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, headerHeight);
            DrawPanel(header, DashboardPanelRaised);
            Widgets.DrawBoxSolid(new Rect(header.x, header.y, 5f, header.height), DashboardAccent);

            Color oldHeaderColor = GUI.color;
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.96f, 0.88f, 0.72f, 1f);
            Widgets.Label(new Rect(header.x + 16f, header.y + 5f, 260f, 31f), "⚒  PRODUCTION CATALOG");
            Text.Font = GameFont.Small;
            GUI.color = DashboardAccent;
            Widgets.Label(new Rect(header.x + 18f, header.y + 34f, 250f, 23f), activeMainCategory);
            Text.Font = GameFont.Small;
            GUI.color = oldHeaderColor;

            DrawMainCategoryTabs(header);

            if (activeMainCategory == "Workwear" || activeMainCategory == "Clothing")
            {
                Rect specialRect = new Rect(inRect.x, header.yMax + gap, inRect.width,
                    Mathf.Max(220f, inRect.yMax - header.yMax - gap));
                if (activeMainCategory == "Workwear") DrawSpecialWorkwearPage(specialRect);
                else DrawClothingPage(specialRect);
                return;
            }

            Rect foldersRect = new Rect(inRect.x, header.yMax + gap, inRect.width, folderHeight);
            DrawPanel(foldersRect, DashboardPanel);
            DrawFolderBar(foldersRect);

            float workspaceY = foldersRect.yMax + gap;
            float workspaceHeight = Mathf.Max(220f, inRect.yMax - workspaceY);
            float columnGap = 8f;
            float columnWidth = (inRect.width - columnGap) * 0.5f;
            Rect queueRect = new Rect(inRect.x, workspaceY, columnWidth, workspaceHeight);
            Rect recipeRect = new Rect(queueRect.xMax + columnGap, workspaceY, columnWidth, workspaceHeight);

            // Classic RimWorld-style split workspace: active bills on the left, recipe rows on the right.
            DrawPanel(queueRect, DashboardPanel);
            DrawProductionQueue(queueRect);

            DrawPanel(recipeRect, DashboardPanel);
            Rect searchRect = new Rect(recipeRect.x + 7f, recipeRect.y + 27f, recipeRect.width - 14f, 27f);
            Widgets.Label(new Rect(searchRect.x + 2f, searchRect.y, 48f, searchRect.height), "Search");
            searchText = Widgets.TextField(new Rect(searchRect.x + 51f, searchRect.y + 1f, searchRect.width - 53f, searchRect.height - 2f), searchText ?? string.Empty);
            DrawRecipeList(new Rect(recipeRect.x, searchRect.yMax + 2f, recipeRect.width, recipeRect.height - (searchRect.yMax - recipeRect.y) - 2f));
        }

        private void DrawMainCategoryTabs(Rect rect)
        {
            string[] labels = { "Production", "Workwear", "Clothing" };
            float gap = 5f;
            float left = rect.x + Mathf.Max(270f, rect.width * 0.275f);
            float available = rect.xMax - left - 10f;
            float width = (available - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                Rect button = new Rect(left + i * (width + gap), rect.y + 8f, width, rect.height - 16f);
                bool active = activeMainCategory == labels[i];
                Color old = GUI.color;
                Widgets.DrawBoxSolid(button, active
                    ? new Color(0.34f, 0.235f, 0.115f, 0.98f)
                    : new Color(0.075f, 0.068f, 0.057f, 0.96f));
                GUI.color = active ? DashboardAccent : new Color(0.30f, 0.27f, 0.22f, 1f);
                Widgets.DrawBox(button, 1);
                GUI.color = active ? new Color(1f, 0.86f, 0.62f, 1f) : new Color(0.82f, 0.78f, 0.70f, 1f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = active ? GameFont.Medium : GameFont.Small;
                if (Widgets.ButtonText(button, labels[i]))
                {
                    activeMainCategory = labels[i];
                    if (activeMainCategory == "Workwear" && selectedWorkwearItem == null)
                        selectedWorkwearItem = GetWorkwearItems().Count > 0 ? GetWorkwearItems()[0] : null;
                }
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = old;
            }
        }

        private void DrawSpecialWorkwearPage(Rect rect)
        {
            if (selectedWorkwearItem == null)
            {
                List<ThingDef> initialItems = GetWorkwearItemsForProfession();
                if (initialItems.Count > 0) selectedWorkwearItem = initialItems[0];
            }
            const float gap = 8f;
            float leftWidth = Mathf.Clamp(rect.width * 0.235f, 190f, 270f);
            float detailsWidth = Mathf.Clamp(rect.width * 0.285f, 270f, 350f);
            float centerWidth = rect.width - leftWidth - detailsWidth - gap * 2f;
            Rect left = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect center = new Rect(left.xMax + gap, rect.y, centerWidth, rect.height);
            Rect details = new Rect(center.xMax + gap, rect.y, detailsWidth, rect.height);
            DrawPanel(left, DashboardPanel);
            DrawPanel(center, DashboardPanel);
            DrawPanel(details, DashboardPanel);
            DrawProfessionList(left);
            DrawWorkwearCards(center);
            DrawWorkwearDetails(details);
        }

        private static readonly string[] ClothingGroups =
        {
            "All", "Head", "Body", "Pants", "Outerwear", "Gloves", "Footwear", "Vests & Backpacks"
        };

        private static readonly string[] ClothingFilterIds = { "CarryBulk", "CarryWeight", "MoveSpeed", "WorkSpeedGlobal" };
        private static readonly string[] ClothingFilterLabels = { "Carrying Capacity", "Carrying Weight", "Movement Speed", "Global Work Speed" };

        private void DrawClothingPage(Rect rect)
        {
            List<ThingDef> items = GetClothingItemsForGroup();
            if (selectedClothingItem == null || !items.Contains(selectedClothingItem))
                selectedClothingItem = items.Count > 0 ? items[0] : null;

            const float gap = 8f;
            float leftWidth = Mathf.Clamp(rect.width * 0.235f, 190f, 270f);
            float detailsWidth = Mathf.Clamp(rect.width * 0.285f, 270f, 350f);
            float centerWidth = rect.width - leftWidth - detailsWidth - gap * 2f;
            Rect left = new Rect(rect.x, rect.y, leftWidth, rect.height);
            Rect center = new Rect(left.xMax + gap, rect.y, centerWidth, rect.height);
            Rect details = new Rect(center.xMax + gap, rect.y, detailsWidth, rect.height);
            DrawPanel(left, DashboardPanel);
            DrawPanel(center, DashboardPanel);
            DrawPanel(details, DashboardPanel);
            DrawClothingGroupList(left);
            DrawClothingCatalog(center);
            DrawClothingDetails(details);
        }

        private void DrawClothingGroupList(Rect rect)
        {
            DrawSectionHeading(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), "CLOTHING CATEGORIES");
            float y = rect.y + 42f;
            for (int i = 0; i < ClothingGroups.Length; i++)
            {
                string group = ClothingGroups[i];
                Rect row = new Rect(rect.x + 7f, y, rect.width - 14f, 38f);
                if (selectedClothingGroup == group) Widgets.DrawBoxSolid(row, DashboardAccentSoft);
                if (Widgets.ButtonText(row, group))
                {
                    selectedClothingGroup = group;
                    clothingScrollPosition = Vector2.zero;
                    List<ThingDef> found = GetClothingItemsForGroup();
                    selectedClothingItem = found.Count > 0 ? found[0] : null;
                }
                int count = group == "All" ? GetClothingItems().Count : CountClothingGroup(group);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.xMax - 34f, row.y, 26f, row.height), count.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                y += 42f;
            }
        }

        private void DrawClothingCatalog(Rect rect)
        {
            // Four agreed characteristics replace search and descriptive header text.
            float x = rect.x + 7f;
            float y = rect.y + 7f;
            float gap = 4f;
            float buttonWidth = (rect.width - 14f - gap * 3f) / 4f;
            for (int i = 0; i < ClothingFilterIds.Length; i++)
            {
                Rect filter = new Rect(x + i * (buttonWidth + gap), y, buttonWidth, 42f);
                bool active = selectedClothingFilter == ClothingFilterIds[i];
                if (active) Widgets.DrawBoxSolid(filter, DashboardAccentSoft);
                Color old = GUI.color;
                GUI.color = active ? new Color(1f, 0.76f, 0.30f, 1f) : Color.white;
                Widgets.DrawBox(filter, 1);
                Text.Anchor = TextAnchor.MiddleCenter;
                if (Widgets.ButtonText(filter, ClothingFilterLabels[i]))
                {
                    selectedClothingFilter = ClothingFilterIds[i];
                    clothingScrollPosition = Vector2.zero;
                }
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = old;
            }

            List<ThingDef> items = GetClothingItemsForGroup();
            SortClothingItems(items);
            float top = y + 50f;
            const float cardGap = 7f;
            int columns = centerColumns(rect.width);
            float cardWidth = (rect.width - 16f - cardGap * (columns - 1)) / columns;
            float cardHeight = Mathf.Clamp(cardWidth * 1.28f, 164f, 190f);
            int rows = (items.Count + columns - 1) / columns;
            Rect listRect = new Rect(rect.x + 4f, top, rect.width - 8f, rect.height - (top - rect.y) - 5f);
            float viewHeight = Mathf.Max(listRect.height, rows * (cardHeight + cardGap) + 8f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref clothingScrollPosition, viewRect);
            if (items.Count == 0)
                Widgets.Label(new Rect(8f, 8f, viewRect.width - 16f, 44f), "No clothing items found in this category yet.");
            else
            {
                int firstRow = Mathf.Max(0, Mathf.FloorToInt(clothingScrollPosition.y / (cardHeight + cardGap)));
                int lastRow = Mathf.Min(rows, Mathf.CeilToInt((clothingScrollPosition.y + listRect.height) / (cardHeight + cardGap)) + 1);
                for (int row = firstRow; row < lastRow; row++)
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= items.Count) break;
                    ThingDef item = items[index];
                    Rect card = new Rect(5f + col * (cardWidth + cardGap), 4f + row * (cardHeight + cardGap), cardWidth, cardHeight);
                    bool active = selectedClothingItem == item;
                    Widgets.DrawBoxSolid(card, active ? new Color(0.20f, 0.15f, 0.08f, 0.98f) : DashboardPanelRaised);
                    Color old = GUI.color;
                    GUI.color = active ? new Color(1f, 0.72f, 0.22f, 1f) : DashboardBorder;
                    Widgets.DrawBox(card, 1);
                    GUI.color = old;
                    float iconSize = Mathf.Min(card.width * 0.52f, card.height * 0.32f);
                    Widgets.ThingIcon(new Rect(card.center.x - iconSize / 2f, card.y + 5f, iconSize, iconSize), item);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(card.x + 4f, card.y + iconSize + 7f, card.width - 8f, 30f), item.LabelCap.ToString());
                    Text.Anchor = TextAnchor.UpperLeft;
                    float statY = card.yMax - 70f;
                    for (int statIndex = 0; statIndex < ClothingFilterIds.Length; statIndex++)
                    {
                        float statValue;
                        string statLabel = statIndex == 0 ? "Weight" : statIndex == 1 ? "Bulk" : statIndex == 2 ? "Movement" : "Work";
                        Rect statRow = new Rect(card.x + 8f, statY + statIndex * 16f, card.width - 16f, 15f);
                        Color statColor = GUI.color;
                        GUI.color = new Color(0.72f, 0.69f, 0.62f, 1f);
                        Widgets.Label(new Rect(statRow.x, statRow.y, statRow.width * 0.55f, statRow.height), statLabel);
                        if (TryGetClothingStat(item, ClothingFilterIds[statIndex], out statValue))
                        {
                            GUI.color = statValue > 0f ? DashboardGreen : statValue < 0f ? new Color(0.88f, 0.32f, 0.24f, 1f) : new Color(0.72f, 0.69f, 0.62f, 1f);
                            Text.Anchor = TextAnchor.MiddleRight;
                            Widgets.Label(new Rect(statRow.x + statRow.width * 0.48f, statRow.y, statRow.width * 0.52f, statRow.height), FormatEquipmentStatValue(ClothingFilterIds[statIndex], statValue));
                            Text.Anchor = TextAnchor.UpperLeft;
                        }
                        GUI.color = statColor;
                    }
                    if (Widgets.ButtonInvisible(card)) selectedClothingItem = item;
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawClothingDetails(Rect rect)
        {
            ThingDef item = selectedClothingItem;
            if (item == null)
            {
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 60f), "No clothing items in this category.");
                return;
            }
            float y = rect.y + 10f;
            float icon = Mathf.Min(92f, rect.width * 0.30f);
            Widgets.ThingIcon(new Rect(rect.x + 12f, y, icon, icon), item);
            Widgets.Label(new Rect(rect.x + icon + 22f, y + 4f, rect.width - icon - 36f, 58f), item.LabelCap.ToString());
            y += icon + 12f;
            Widgets.DrawLineHorizontal(rect.x + 10f, y, rect.width - 20f);
            y += 10f;
            DrawSectionHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 24f), "EQUIPMENT EFFECTS");
            y += 28f;
            for (int i = 0; i < ClothingFilterIds.Length; i++)
            {
                float value;
                if (TryGetClothingStat(item, ClothingFilterIds[i], out value))
                {
                    Rect statRow = new Rect(rect.x + 14f, y, rect.width - 28f, 24f);
                    Widgets.Label(new Rect(statRow.x, statRow.y, statRow.width * 0.70f, statRow.height), ClothingFilterLabels[i]);
                    Color oldStatColor = GUI.color;
                    GUI.color = value > 0f ? DashboardGreen : value < 0f
                        ? new Color(0.90f, 0.34f, 0.25f, 1f)
                        : new Color(0.78f, 0.75f, 0.68f, 1f);
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(new Rect(statRow.x + statRow.width * 0.70f, statRow.y, statRow.width * 0.30f, statRow.height),
                        FormatEquipmentStatValue(ClothingFilterIds[i], value));
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = oldStatColor;
                    Widgets.DrawLineHorizontal(statRow.x, statRow.yMax, statRow.width);
                }
                y += 27f;
            }
            if (item.BaseMass > 0f)
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 22f), "Item weight: " + item.BaseMass.ToString("0.##") + " kg");
            float buttonY = rect.yMax - 58f;
            if (Widgets.ButtonText(new Rect(rect.x + 12f, buttonY, rect.width - 24f, 42f), "⚒  CRAFT"))
                OpenProductionForWorkwear(item);
        }

        private static List<ThingDef> GetClothingItems()
        {
            int count = DefDatabase<ThingDef>.AllDefsListForReading.Count;
            Map map = Find.CurrentMap;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (cachedClothingItems != null && cachedClothingDefsCount == count && cachedClothingMap == map &&
                cachedClothingTick >= 0 && tick - cachedClothingTick < 300) return cachedClothingItems;
            cachedClothingItems = new List<ThingDef>();
            cachedClothingDefsCount = count;
            cachedClothingMap = map;
            cachedClothingTick = tick;
            if (map == null) return cachedClothingItems;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def != null && def.apparel != null && RecipeBenchFinder.CanProduceOnMap(map, def))
                    cachedClothingItems.Add(def);
            }
            return cachedClothingItems;
        }

        private List<ThingDef> GetClothingItemsForGroup()
        {
            List<ThingDef> all = GetClothingItems();
            List<ThingDef> result = new List<ThingDef>();
            for (int i = 0; i < all.Count; i++)
                if (selectedClothingGroup == "All" || IsInClothingGroup(all[i], selectedClothingGroup)) result.Add(all[i]);
            SortClothingItems(result);
            return result;
        }

        private static int CountClothingGroup(string group)
        {
            int count = 0;
            List<ThingDef> all = GetClothingItems();
            for (int i = 0; i < all.Count; i++) if (IsInClothingGroup(all[i], group)) count++;
            return count;
        }

        private static bool IsInClothingGroup(ThingDef def, string group)
        {
            string id = (def.defName ?? string.Empty).ToLowerInvariant();
            string label = def.label ?? string.Empty;
            string key = (id + " " + label).ToLowerInvariant();
            if (group == "Head") return key.Contains("hat") || key.Contains("helmet") || key.Contains("hood") || key.Contains("mask") || key.Contains("шап") || key.Contains("шлем") || key.Contains("маск") || key.Contains("капюш");
            if (group == "Gloves") return key.Contains("glove") || key.Contains("митен") || key.Contains("перчат");
            if (group == "Footwear") return key.Contains("boot") || key.Contains("shoe") || key.Contains("sandal") || key.Contains("сапог") || key.Contains("ботин") || key.Contains("обув");
            if (group == "Pants") return key.Contains("pants") || key.Contains("trousers") || key.Contains("bottom") || key.Contains("брюк") || key.Contains("штаны");
            if (group == "Vests & Backpacks") return key.Contains("backpack") || key.Contains("bandolier") || key.Contains("vest") || key.Contains("belt") || key.Contains("рюкзак") || key.Contains("разгруз") || key.Contains("подсум") || key.Contains("пояс");
            if (group == "Outerwear") return key.Contains("coat") || key.Contains("parka") || key.Contains("jacket") || key.Contains("duster") || key.Contains("cloak") || key.Contains("poncho") || key.Contains("пальто") || key.Contains("куртк") || key.Contains("плащ");
            if (group == "Body")
                return !IsInClothingGroup(def, "Head") && !IsInClothingGroup(def, "Pants") &&
                       !IsInClothingGroup(def, "Outerwear") && !IsInClothingGroup(def, "Gloves") &&
                       !IsInClothingGroup(def, "Footwear") && !IsInClothingGroup(def, "Vests & Backpacks");
            return false;
        }

        private static string FormatEquipmentStatValue(string statName, float value)
        {
            // Carry capacity offsets are absolute units; movement/work offsets are percentages.
            if (statName == "CarryBulk" || statName == "CarryWeight")
                return value.ToString("+0.##;-0.##;0");
            return (value * 100f).ToString("+0.##;-0.##;0") + "%";
        }

        private static bool TryGetClothingStat(ThingDef item, string statName, out float value)
        {
            value = 0f;
            if (item == null || item.equippedStatOffsets == null) return false;
            for (int i = 0; i < item.equippedStatOffsets.Count; i++)
            {
                StatModifier modifier = item.equippedStatOffsets[i];
                if (modifier != null && modifier.stat != null && modifier.stat.defName == statName)
                { value = modifier.value; return true; }
            }
            return false;
        }

        private void SortClothingItems(List<ThingDef> items)
        {
            if (items == null) return;
            items.Sort(delegate(ThingDef a, ThingDef b)
            {
                float av, bv;
                bool ah = TryGetClothingStat(a, selectedClothingFilter, out av);
                bool bh = TryGetClothingStat(b, selectedClothingFilter, out bv);
                if (ah != bh) return ah ? -1 : 1;
                if (ah)
                {
                    int compare = bv.CompareTo(av); // Always descending: highest value first.
                    if (compare != 0) return compare;
                }
                return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase);
            });
        }

        private static readonly string[] ProfessionNames =
        {
            "All Professions", "Mining", "Construction & Repair", "Manufacturing & Crafting",
            "Agriculture", "Cooking & Butchery", "Medicine & Surgery", "Research & Science"
        };

        private void DrawProfessionList(Rect rect)
        {
            DrawSectionHeading(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 28f), "PROFESSION");
            float y = rect.y + 42f;
            for (int i = 0; i < ProfessionNames.Length; i++)
            {
                string profession = ProfessionNames[i];
                Rect row = new Rect(rect.x + 7f, y, rect.width - 14f, 38f);
                bool active = selectedProfession == profession;
                if (active) Widgets.DrawBoxSolid(row, DashboardAccentSoft);
                if (Widgets.ButtonText(row, profession))
                {
                    selectedProfession = profession;
                    workwearScrollPosition = Vector2.zero;
                    List<ThingDef> professionItems = GetWorkwearItemsForProfession();
                    selectedWorkwearItem = professionItems.Count > 0 ? professionItems[0] : null;
                }
                int itemCount = profession == "All Professions" ? GetWorkwearItems().Count : CountWorkwearForProfession(profession);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.xMax - 34f, row.y, 26f, row.height), itemCount.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                y += 42f;
            }
        }

        private static int CountWorkwearForProfession(string profession)
        {
            int count = 0;
            List<ThingDef> all = GetWorkwearItems();
            for (int i = 0; i < all.Count; i++) if (BelongsToProfession(all[i], profession)) count++;
            return count;
        }

        private void DrawWorkwearCards(Rect rect)
        {
            DrawSectionHeading(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 26f), "WORKWEAR & TOOLS");
            List<ThingDef> items = GetWorkwearItemsForProfession();
            float top = rect.y + 40f;
            const float gap = 7f;
            int columns = centerColumns(rect.width);
            float cardWidth = (rect.width - 16f - gap * (columns - 1)) / columns;
            float cardHeight = Mathf.Clamp(cardWidth * 1.28f, 164f, 190f);
            int rows = (items.Count + columns - 1) / columns;
            Rect listRect = new Rect(rect.x + 4f, top, rect.width - 8f, rect.height - 44f);
            float viewHeight = Mathf.Max(listRect.height, rows * (cardHeight + gap) + 6f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref workwearScrollPosition, viewRect);
            if (items.Count == 0)
                Widgets.Label(new Rect(8f, 8f, viewRect.width - 16f, 44f), "No items with relevant bonuses found for this profession yet.");
            else
            {
                int firstRow = Mathf.Max(0, Mathf.FloorToInt(workwearScrollPosition.y / (cardHeight + gap)));
                int lastRow = Mathf.Min(rows, Mathf.CeilToInt((workwearScrollPosition.y + listRect.height) / (cardHeight + gap)) + 1);
                for (int row = firstRow; row < lastRow; row++)
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= items.Count) break;
                    ThingDef item = items[index];
                    Rect card = new Rect(5f + col * (cardWidth + gap), 4f + row * (cardHeight + gap), cardWidth, cardHeight);
                    bool active = selectedWorkwearItem == item;
                    Widgets.DrawBoxSolid(card, active ? new Color(0.20f, 0.15f, 0.08f, 0.98f) : DashboardPanelRaised);
                    Color old = GUI.color;
                    GUI.color = active ? new Color(1f, 0.72f, 0.22f, 1f) : DashboardBorder;
                    Widgets.DrawBox(card, 1);
                    GUI.color = old;
                    float iconSize = Mathf.Min(card.width * 0.58f, card.height * 0.52f);
                    Widgets.ThingIcon(new Rect(card.center.x - iconSize / 2f, card.y + 7f, iconSize, iconSize), item);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(card.x + 4f, card.y + card.height - 43f, card.width - 8f, 38f), item.LabelCap.ToString());
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (Widgets.ButtonInvisible(card)) selectedWorkwearItem = item;
                    TooltipHandler.TipRegion(card, GetWorkwearBonusTooltip(item));
                }
            }
            Widgets.EndScrollView();
        }

        private static int centerColumns(float width)
        {
            if (width < 410f) return 2;
            if (width < 650f) return 3;
            return 4;
        }

        private void DrawWorkwearDetails(Rect rect)
        {
            if (selectedWorkwearItem == null)
            {
                Widgets.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, 60f), "Select an item from the catalog.");
                return;
            }
            ThingDef item = selectedWorkwearItem;
            float y = rect.y + 10f;
            float icon = Mathf.Min(92f, rect.width * 0.30f);
            Widgets.ThingIcon(new Rect(rect.x + 12f, y, icon, icon), item);
            Widgets.Label(new Rect(rect.x + icon + 22f, y + 4f, rect.width - icon - 36f, 52f), item.LabelCap.ToString());
            Widgets.Label(new Rect(rect.x + icon + 22f, y + 56f, rect.width - icon - 36f, 24f), item.apparel != null ? "Workwear" : "Tool / Equipment");
            y += icon + 12f;
            Widgets.DrawLineHorizontal(rect.x + 10f, y, rect.width - 20f);
            y += 10f;
            DrawSectionHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 24f), "EQUIPMENT / USAGE EFFECTS");
            y += 28f;
            List<StatModifier> offsets = item.equippedStatOffsets;
            bool any = false;
            if (offsets != null)
            {
                for (int i = 0; i < offsets.Count && y + 24f < rect.yMax - 90f; i++)
                {
                    StatModifier modifier = offsets[i];
                    if (modifier == null || modifier.stat == null || !IsProfessionalStat(modifier.stat.defName)) continue;
                    any = true;
                    string value = FormatEquipmentStatValue(modifier.stat.defName, modifier.value);
                    Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 22f), modifier.stat.LabelCap + "   " + value);
                    y += 24f;
                }
            }
            List<WorkStatFactorEntry> workFactors = GetBaseWorkStatFactors(item);
            if (workFactors.Count > 0)
            {
                if (any) y += 6f;
                DrawSectionHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 22f), "WORK EFFICIENCY (MODS)");
                y += 24f;
                for (int i = 0; i < workFactors.Count && y + 22f < rect.yMax - 90f; i++)
                {
                    WorkStatFactorEntry factor = workFactors[i];
                    Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 22f),
                        GetWorkFactorLabel(factor.Name) + "   ×" + factor.Value.ToString("0.##") + " (" + (factor.Value * 100f).ToString("0.##") + "%)");
                    y += 23f;
                }
                any = true;
            }
            if (!any)
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 42f), "No special bonuses configured.");
            y += 8f;
            Widgets.DrawLineHorizontal(rect.x + 10f, y, rect.width - 20f);
            y += 10f;
            DrawSectionHeading(new Rect(rect.x + 12f, y, rect.width - 24f, 24f), "EQUIPMENT EFFECTS");
            y += 26f;
            if (item.BaseMass > 0f)
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 22f), "Weight: " + item.BaseMass.ToString("0.##") + " kg");
            y += 24f;
            if (item.apparel != null)
                Widgets.Label(new Rect(rect.x + 14f, y, rect.width - 28f, 22f), "Apparel layer: clothing");
            float buttonY = rect.yMax - 58f;
            if (Widgets.ButtonText(new Rect(rect.x + 12f, buttonY, rect.width - 24f, 42f), "⚒  CRAFT"))
                OpenProductionForWorkwear(item);
        }

        private void OpenProductionForWorkwear(ThingDef item)
        {
            List<RecipeDef> recipes = FindRecipesForProduct(item);
            if (recipes.Count == 0)
            {
                Messages.Message("No production recipe found for “" + item.LabelCap + "”.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (recipes.Count == 1)
            {
                selectedRecipe = recipes[0];
                OpenOrderBuilder();
                return;
            }
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                options.Add(new FloatMenuOption(recipe.LabelCap.ToString(), delegate
                {
                    selectedRecipe = recipe;
                    OpenOrderBuilder();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static List<RecipeDef> FindRecipesForProduct(ThingDef product)
        {
            List<RecipeDef> result = new List<RecipeDef>();
            Map map = Find.CurrentMap;
            if (map == null || product == null) return result;
            List<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef recipe = all[i];
                if (recipe == null || recipe.products == null || RecipeBenchFinder.Find(map, recipe).Count == 0) continue;
                for (int p = 0; p < recipe.products.Count; p++)
                    if (recipe.products[p] != null && recipe.products[p].thingDef == product)
                    { result.Add(recipe); break; }
            }
            return result;
        }

        private List<ThingDef> GetWorkwearItemsForProfession()
        {
            List<ThingDef> all = GetWorkwearItems();
            if (selectedProfession == "All Professions") return all;
            List<ThingDef> result = new List<ThingDef>();
            for (int i = 0; i < all.Count; i++)
                if (BelongsToProfession(all[i], selectedProfession)) result.Add(all[i]);
            result.Sort(delegate(ThingDef a, ThingDef b) { return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase); });
            return result;
        }

        private sealed class WorkStatFactorEntry
        {
            public string Name;
            public float Value;
            public WorkStatFactorEntry(string name, float value) { Name = name; Value = value; }
        }

        private static List<ThingDef> GetWorkwearItems()
        {
            int count = DefDatabase<ThingDef>.AllDefsListForReading.Count;
            Map map = Find.CurrentMap;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (cachedWorkwearItems != null && cachedWorkwearDefsCount == count && cachedWorkwearMap == map &&
                cachedWorkwearTick >= 0 && tick - cachedWorkwearTick < 300) return cachedWorkwearItems;
            cachedWorkwearItems = new List<ThingDef>();
            cachedWorkwearDefsCount = count;
            cachedWorkwearMap = map;
            cachedWorkwearTick = tick;
            if (map == null) return cachedWorkwearItems;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def == null) continue;
                bool relevant = false;
                if (def.equippedStatOffsets != null)
                    for (int j = 0; j < def.equippedStatOffsets.Count; j++)
                    {
                        StatModifier mod = def.equippedStatOffsets[j];
                        if (mod != null && mod.stat != null && IsProfessionalStat(mod.stat.defName)) { relevant = true; break; }
                    }
                if (!relevant && GetBaseWorkStatFactors(def).Count > 0) relevant = true;
                if ((relevant || IsKnownWorkTool(def.defName)) && RecipeBenchFinder.CanProduceOnMap(map, def))
                    cachedWorkwearItems.Add(def);
            }
            cachedWorkwearItems.Sort(delegate(ThingDef a, ThingDef b) { return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase); });
            return cachedWorkwearItems;
        }

        // Reads mod extension data without a compile-time dependency on the mod that defines it.
        // PEMC stores values such as <TreeFellingSpeed>2</TreeFellingSpeed> in
        // SurvivalToolsLite.SurvivalToolProperties.baseWorkStatFactors.
        private static List<WorkStatFactorEntry> GetBaseWorkStatFactors(ThingDef def)
        {
            List<WorkStatFactorEntry> result = new List<WorkStatFactorEntry>();
            if (def == null || def.modExtensions == null) return result;
            for (int i = 0; i < def.modExtensions.Count; i++)
            {
                object extension = def.modExtensions[i];
                if (extension == null) continue;
                object factors = GetMemberValue(extension, "baseWorkStatFactors");
                if (factors == null) continue;
                ReadWorkFactorObject(factors, result, 0);
            }
            // Avoid duplicate keys if a mod extension exposes the same data through multiple members.
            for (int i = result.Count - 1; i >= 0; i--)
                for (int j = 0; j < i; j++)
                    if (result[i].Name == result[j].Name) { result.RemoveAt(i); break; }
            return result;
        }

        private static object GetMemberValue(object source, string name)
        {
            if (source == null) return null;
            Type type = source.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(source);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(source, null);
            return null;
        }

        private static void ReadWorkFactorObject(object source, List<WorkStatFactorEntry> output, int depth)
        {
            if (source == null || output == null || depth > 3) return;
            IDictionary dictionary = source as IDictionary;
            if (dictionary != null)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    float numeric;
                    if (TryConvertFloat(entry.Value, out numeric)) output.Add(new WorkStatFactorEntry(GetWorkFactorKey(entry.Key), numeric));
                    else ReadWorkFactorObject(entry.Value, output, depth + 1);
                }
                return;
            }
            IEnumerable enumerable = source as IEnumerable;
            if (enumerable != null && !(source is string))
            {
                foreach (object item in enumerable)
                {
                    if (item == null) continue;
                    object key = GetMemberValue(item, "Key");
                    object value = GetMemberValue(item, "Value");
                    float numeric;
                    if (key != null && TryConvertFloat(value, out numeric)) output.Add(new WorkStatFactorEntry(GetWorkFactorKey(key), numeric));
                    else
                    {
                        object stat = GetMemberValue(item, "stat") ?? GetMemberValue(item, "Stat") ?? GetMemberValue(item, "defName");
                        object amount = GetMemberValue(item, "value") ?? GetMemberValue(item, "Value");
                        if (stat != null && TryConvertFloat(amount, out numeric)) output.Add(new WorkStatFactorEntry(GetWorkFactorKey(stat), numeric));
                        else ReadWorkFactorObject(item, output, depth + 1);
                    }
                }
                return;
            }
            Type type = source.GetType();
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return;
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                object value = fields[i].GetValue(source);
                float numeric;
                if (TryConvertFloat(value, out numeric)) output.Add(new WorkStatFactorEntry(fields[i].Name, numeric));
                else if (value != null && !(value is string)) ReadWorkFactorObject(value, output, depth + 1);
            }
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < properties.Length; i++)
            {
                if (properties[i].GetIndexParameters().Length != 0 || properties[i].GetGetMethod(true) == null) continue;
                try
                {
                    object value = properties[i].GetValue(source, null);
                    float numeric;
                    if (TryConvertFloat(value, out numeric)) output.Add(new WorkStatFactorEntry(properties[i].Name, numeric));
                    else if (value != null && !(value is string)) ReadWorkFactorObject(value, output, depth + 1);
                }
                catch { }
            }
        }

        private static bool TryConvertFloat(object value, out float result)
        {
            try { if (value == null) { result = 0f; return false; } result = Convert.ToSingle(value); return true; }
            catch { result = 0f; return false; }
        }

        private static string GetWorkFactorKey(object key)
        {
            if (key == null) return string.Empty;
            ThingDef thing = key as ThingDef;
            if (thing != null) return thing.defName;
            StatDef stat = key as StatDef;
            if (stat != null) return stat.defName;
            Def def = key as Def;
            if (def != null) return def.defName;
            return key.ToString();
        }

        private static string GetWorkFactorLabel(string key)
        {
            if (key == "TreeFellingSpeed") return "Tree Felling Speed";
            if (key == "PlantHarvestingSpeed") return "Plant Harvesting Speed";
            if (key == "PruningSpeed") return "Plant Pruning Speed";
            if (key == "MiningSpeed") return "Mining Speed";
            if (key == "ConstructionSpeed") return "Construction Speed";
            StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(key);
            return stat != null ? stat.LabelCap.ToString() : key;
        }

        private static bool IsKnownWorkTool(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return false;
            return defName.StartsWith("Tool_", StringComparison.Ordinal) || defName.StartsWith("PEMC_Tool_", StringComparison.Ordinal) ||
                   defName == "MeleeWeapon_Axe" || defName == "MeleeWeapon_BreachAxe" || defName.StartsWith("NorbalMeleeWeapon_Ax", StringComparison.Ordinal);
        }

        private static bool IsProfessionalStat(string stat)
        {
            return stat == "MiningSpeed" || stat == "MiningYield" || stat == "ConstructionSpeed" ||
                stat == "FixBrokenDownBuildingSuccessChance" || stat == "SmoothingSpeed" || stat == "SmithingSpeed" ||
                stat == "SmeltingSpeed" || stat == "TailoringSpeed" || stat == "AssemblySpeedFactor" ||
                stat == "WorkTableEfficiencyFactor" || stat == "WorkTableWorkSpeedFactor" || stat == "HSK_CraftQualityFactor" ||
                stat == "PlantWorkSpeed" || stat == "PruningSpeed" || stat == "ButcheryFleshSpeed" ||
                stat == "DrugCookingSpeed" || stat == "FoodPoisonChance" || stat == "MedicalOperationSpeed" ||
                stat == "MedicalSurgerySuccessChance" || stat == "MedicalTendQuality" || stat == "SurgerySuccessChanceFactor" ||
                stat == "ResearchSpeed" || stat == "GeneralLaborSpeed" || stat == "TreeFellingSpeed" ||
                stat == "PlantHarvestingSpeed";
        }

        private static bool BelongsToProfession(ThingDef def, string profession)
        {
            if (def == null || def.equippedStatOffsets == null) return false;
            for (int i = 0; i < def.equippedStatOffsets.Count; i++)
            {
                StatModifier mod = def.equippedStatOffsets[i];
                if (mod == null || mod.stat == null) continue;
                string stat = mod.stat.defName;
                if (profession == "Mining" && (stat == "MiningSpeed" || stat == "MiningYield")) return true;
                if (profession == "Construction & Repair" && (stat == "ConstructionSpeed" || stat == "FixBrokenDownBuildingSuccessChance" || stat == "SmoothingSpeed")) return true;
                if (profession == "Manufacturing & Crafting" && (stat == "SmithingSpeed" || stat == "SmeltingSpeed" || stat == "TailoringSpeed" || stat == "AssemblySpeedFactor" || stat == "WorkTableEfficiencyFactor" || stat == "WorkTableWorkSpeedFactor" || stat == "HSK_CraftQualityFactor" || stat == "GeneralLaborSpeed")) return true;
                if (profession == "Agriculture" && (stat == "PlantWorkSpeed" || stat == "PruningSpeed")) return true;
                if (profession == "Cooking & Butchery" && (stat == "ButcheryFleshSpeed" || stat == "DrugCookingSpeed" || stat == "FoodPoisonChance")) return true;
                if (profession == "Medicine & Surgery" && (stat == "MedicalOperationSpeed" || stat == "MedicalSurgerySuccessChance" || stat == "MedicalTendQuality" || stat == "SurgerySuccessChanceFactor")) return true;
                if (profession == "Research & Science" && stat == "ResearchSpeed") return true;
            }
            List<WorkStatFactorEntry> workFactors = GetBaseWorkStatFactors(def);
            for (int i = 0; i < workFactors.Count; i++)
            {
                string stat = workFactors[i].Name;
                if (profession == "Agriculture" && (stat == "TreeFellingSpeed" || stat == "PlantHarvestingSpeed" || stat == "PruningSpeed")) return true;
                if (profession == "Mining" && stat == "MiningSpeed") return true;
                if (profession == "Construction & Repair" && stat == "ConstructionSpeed") return true;
                if (profession == "Research & Science" && stat == "ResearchSpeed") return true;
            }
            string id = def.defName ?? string.Empty;
            if (IsKnownWorkTool(id))
            {
                if (profession == "Mining" && (id.IndexOf("Mining", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Pickaxe", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                if (profession == "Construction & Repair" && (id.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Hammer", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Trowel", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                if (profession == "Agriculture" && (id.IndexOf("Woodcut", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Agriculture", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Sickle", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Hoe", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Shovel", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                if (profession == "Medicine & Surgery" && (id.IndexOf("Medbag", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Scalpel", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
                if (profession == "Manufacturing & Crafting" && (id.IndexOf("Grinder", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Soldering", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("ToolBox", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("Multitool", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            }
            return false;
        }

        private static string GetWorkwearBonusTooltip(ThingDef def)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder(def.LabelCap.ToString());
            if (def.equippedStatOffsets != null)
                for (int i = 0; i < def.equippedStatOffsets.Count; i++)
                {
                    StatModifier mod = def.equippedStatOffsets[i];
                    if (mod != null && mod.stat != null && IsProfessionalStat(mod.stat.defName))
                        text.Append("\n").Append(mod.stat.LabelCap).Append(": ").Append(mod.value.ToString("+0.##;-0.##;0"));
                }
            List<WorkStatFactorEntry> factors = GetBaseWorkStatFactors(def);
            for (int i = 0; i < factors.Count; i++)
                text.Append("\n").Append(GetWorkFactorLabel(factors[i].Name)).Append(": ×").Append(factors[i].Value.ToString("0.##"));
            return text.ToString();
        }

        private static void DrawPanel(Rect rect, Color fill)
        {
            Widgets.DrawBoxSolid(rect, fill);
            Color previous = GUI.color;
            GUI.color = DashboardBorder;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;
        }

        private static void DrawSectionHeading(Rect rect, string label)
        {
            Color previous = GUI.color;
            GUI.color = DashboardAccent;
            Widgets.Label(rect, label);
            GUI.color = previous;
        }

        private void DrawFolderBar(Rect container)
        {
            GameComponent_BlocksMenuQueue component = Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
            const float buttonHeight = 25f;
            const float gap = 5f;
            float x = container.x + 6f;
            float y = container.y + 5f;
            float rightLimit = container.xMax - 226f;
            Rect allRect = new Rect(x, y, 104f, buttonHeight);
            if (string.IsNullOrEmpty(currentFolder)) Widgets.DrawBoxSolid(allRect, DashboardAccentSoft);
            if (Widgets.ButtonText(allRect, "All Recipes")) { currentFolder = string.Empty; showHiddenRecipes = false; cachedRecipesTick = -1; }
            x = allRect.xMax + gap;

            // These two folders are live views of the existing top-level catalog tabs.
            // Their recipes, orders and compatible benches are derived from those tabs;
            // they are not copied into or mixed with user-created folders.
            DrawBuiltInFolderButton(container, ref x, ref y, rightLimit, SpecialWearFolderKey,
                "Workwear", GetBuiltInFolderBenchTooltip(SpecialWearFolderKey));
            DrawBuiltInFolderButton(container, ref x, ref y, rightLimit, ClothingFolderKey,
                "Clothing", GetBuiltInFolderBenchTooltip(ClothingFolderKey));

            if (component != null && component.CustomFolders != null)
            {
                for (int i = 0; i < component.CustomFolders.Count; i++)
                {
                    string folder = component.CustomFolders[i];
                    float width = Mathf.Clamp(Text.CalcSize(folder).x + 28f, 82f, 150f);
                    const float editWidth = 24f;
                    float groupWidth = width + 3f + editWidth;
                    if (x + groupWidth > rightLimit) { x = container.x + 6f; y += buttonHeight + 5f; }
                    if (y + buttonHeight > container.yMax - 3f) break;
                    Rect folderRect = new Rect(x, y, width, buttonHeight);
                    Rect editRect = new Rect(folderRect.xMax + 3f, y, editWidth, buttonHeight);
                    if (currentFolder == folder) Widgets.DrawBoxSolid(folderRect, DashboardAccentSoft);
                    List<string> configuredBenches = component.GetFolderBenchDefs(folder);
                    string tabLabel = folder + (configuredBenches.Count > 0 ? " · " + configuredBenches.Count : string.Empty);
                    if (Widgets.ButtonText(folderRect, tabLabel)) { currentFolder = folder; showHiddenRecipes = false; cachedRecipesTick = -1; }
                    TooltipHandler.TipRegion(folderRect, GetFolderBenchTooltip(folder, configuredBenches));
                    if (Widgets.ButtonText(editRect, "✎"))
                        Find.WindowStack.Add(new Dialog_BlocksMenuFolderEditor(component, folder));
                    TooltipHandler.TipRegion(editRect, "Edit folder: name, workbenches, and recipes");
                    if (Event.current.type == EventType.ContextClick && folderRect.Contains(Event.current.mousePosition))
                    {
                        Find.WindowStack.Add(new Dialog_BlocksMenuFolderEditor(component, folder));
                        Event.current.Use();
                    }
                    x += groupWidth + gap;
                }
            }
            Rect manageRect = new Rect(container.xMax - 222f, container.y + 5f, 104f, buttonHeight);
            if (Widgets.ButtonText(manageRect, "⚙ Folders") && component != null)
                Find.WindowStack.Add(new Dialog_BlocksMenuFolderManager(component));
            TooltipHandler.TipRegion(manageRect, "Open the folder manager");
            Rect addRect = new Rect(container.xMax - 112f, container.y + 5f, 106f, buttonHeight);
            if (Widgets.ButtonText(addRect, "+ Folder") && component != null)
                Find.WindowStack.Add(new Dialog_BlocksMenuFolderEditor(component, null));
            TooltipHandler.TipRegion(addRect, "Create and configure a custom folder");
        }

        private void DrawBuiltInFolderButton(Rect container, ref float x, ref float y, float rightLimit,
            string key, string label, string tooltip)
        {
            const float buttonHeight = 25f;
            const float gap = 5f;
            float width = Mathf.Clamp(Text.CalcSize(label).x + 24f, 92f, 132f);
            if (x + width > rightLimit) { x = container.x + 6f; y += buttonHeight + gap; }
            if (y + buttonHeight > container.yMax - 3f) return;
            Rect button = new Rect(x, y, width, buttonHeight);
            if (currentFolder == key) Widgets.DrawBoxSolid(button, DashboardAccentSoft);
            if (Widgets.ButtonText(button, label))
            {
                activeMainCategory = "Production";
                currentFolder = key;
                showHiddenRecipes = false;
                cachedRecipesTick = -1;
                selectedRecipe = null;
                recipeScrollPosition = Vector2.zero;
                queueScrollPosition = Vector2.zero;
            }
            TooltipHandler.TipRegion(button, tooltip);
            x += width + gap;
        }

        private static bool IsBuiltInFolder(string folder)
        {
            return folder == SpecialWearFolderKey || folder == ClothingFolderKey;
        }

        private static string GetFolderDisplayName(string folder)
        {
            if (folder == SpecialWearFolderKey) return "Workwear";
            if (folder == ClothingFolderKey) return "Clothing";
            return folder;
        }

        private static bool RecipeProducesAny(RecipeDef recipe, List<ThingDef> targetItems)
        {
            if (recipe == null || recipe.products == null || targetItems == null || targetItems.Count == 0)
                return false;
            for (int p = 0; p < recipe.products.Count; p++)
            {
                ThingDef product = recipe.products[p] == null ? null : recipe.products[p].thingDef;
                if (product != null && targetItems.Contains(product)) return true;
            }
            return false;
        }

        private static bool RecipeBelongsToBuiltInFolder(RecipeDef recipe, string folder)
        {
            if (recipe == null) return false;

            // SpecialWear has priority over Clothing. Many workwear items are also
            // apparel ThingDefs, but their recipes must belong only to SpecialWear.
            if (folder == SpecialWearFolderKey)
                return RecipeProducesAny(recipe, GetWorkwearItems());

            if (folder == ClothingFolderKey)
            {
                if (RecipeProducesAny(recipe, GetWorkwearItems())) return false;
                return RecipeProducesAny(recipe, GetClothingItems());
            }

            return false;
        }

        private string GetBuiltInFolderBenchTooltip(string folder)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("Folder workbenches: “").Append(GetFolderDisplayName(folder)).Append("»:");

            HashSet<string> labels = new HashSet<string>();
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def == null || def.AllRecipes == null) continue;
                bool matches = false;
                for (int r = 0; r < def.AllRecipes.Count; r++)
                {
                    if (RecipeBelongsToBuiltInFolder(def.AllRecipes[r], folder))
                    {
                        matches = true;
                        break;
                    }
                }
                if (matches) labels.Add(def.LabelCap.ToString());
            }

            if (labels.Count == 0)
            {
                text.Append("\\nNo workbenches with recipes for this folder were found.");
                return text.ToString();
            }

            List<string> sorted = new List<string>(labels);
            sorted.Sort(StringComparer.CurrentCultureIgnoreCase);
            for (int i = 0; i < sorted.Count; i++)
                text.Append("\\n• ").Append(sorted[i]);
            return text.ToString();
        }

        private static string GetFolderBenchTooltip(string folder, List<string> benchDefs)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append("Folder workbenches: “").Append(folder).Append("»:");
            if (benchDefs == null || benchDefs.Count == 0)
            {
                text.Append("\nNone selected. The folder may contain individually added recipes only.");
                return text.ToString();
            }
            for (int i = 0; i < benchDefs.Count; i++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(benchDefs[i]);
                text.Append("\n• ").Append(def == null ? benchDefs[i] : def.LabelCap.ToString());
            }
            return text.ToString();
        }

        private void DrawSelectedRecipe(Rect rect)
        {
            DrawSectionHeading(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 24f), "SELECTED RECIPE");
            if (selectedRecipe == null)
            {
                Widgets.Label(new Rect(rect.x + 6f, rect.y + 34f, rect.width - 12f, 60f), "Select a recipe in the catalog to view its ingredients and order settings.");
                return;
            }

            float y = rect.y + 30f;
            Widgets.Label(new Rect(rect.x + 6f, y, rect.width - 12f, 42f), selectedRecipe.LabelCap.ToString());
            y += 44f;
            DrawSectionHeading(new Rect(rect.x + 6f, y, rect.width - 12f, 22f), "INGREDIENTS");
            y += 24f;
            if (selectedRecipe.ingredients != null)
            {
                for (int i = 0; i < selectedRecipe.ingredients.Count && y + 24f < rect.yMax - 150f; i++)
                {
                    IngredientCount ingredient = selectedRecipe.ingredients[i];
                    if (ingredient == null || ingredient.filter == null) continue;
                    string resourceName = GetIngredientDisplayName(ingredient.filter);
                    Widgets.Label(new Rect(rect.x + 8f, y, rect.width - 16f, 22f), ingredient.GetBaseCount().ToString("0.##") + " × " + resourceName);
                    y += 23f;
                }
            }
            y += 6f;
            DrawSectionHeading(new Rect(rect.x + 6f, y, rect.width - 12f, 22f), "COMPATIBLE WORKBENCHES");
            y += 24f;
            List<Building> benches = GetCachedSelectedBenches();
            if (benches.Count == 0)
                Widgets.Label(new Rect(rect.x + 8f, y, rect.width - 16f, 22f), "No compatible workbench");
            else
                Widgets.Label(new Rect(rect.x + 8f, y, rect.width - 16f, 38f), benches[0].LabelCap.ToString() + (benches.Count > 1 ? " + " + (benches.Count - 1) : string.Empty));

            float controlsY = rect.yMax - 132f;
            Widgets.DrawBoxSolid(new Rect(rect.x + 5f, controlsY - 4f, rect.width - 10f, 82f), DashboardPanelRaised);
            DrawSectionHeading(new Rect(rect.x + 8f, controlsY, rect.width - 16f, 22f), "ORDER SETTINGS");
            countText = Widgets.TextField(new Rect(rect.x + 8f, controlsY + 24f, 76f, 26f), countText ?? "1");
            if (Widgets.ButtonText(new Rect(rect.x + 92f, controlsY + 24f, rect.width - 100f, 26f), "Mode: " + GetOrderModeLabelForRecipe() + " ▼"))
                OpenRecipeModeMenu();
            if (selectedOrderMode == ProductionOrderMode.MakeUntilTarget)
            {
                Widgets.Label(new Rect(rect.x + 8f, controlsY + 54f, 48f, 22f), "Target:");
                targetText = Widgets.TextField(new Rect(rect.x + 58f, controlsY + 54f, 70f, 24f), targetText ?? "200");
            }
            float buttonY = rect.yMax - 54f;
            if (Widgets.ButtonText(new Rect(rect.x + 6f, buttonY, rect.width - 12f, 26f), "Recipe Details"))
                Find.WindowStack.Add(new Dialog_BlocksMenuRecipeDetails(selectedRecipe));
            if (Widgets.ButtonText(new Rect(rect.x + 6f, buttonY + 29f, rect.width - 12f, 26f), "Configure Bills & Create"))
                OpenOrderBuilder();
        }

        private void OpenRecipeModeMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("Make N", delegate { selectedOrderMode = ProductionOrderMode.MakeCount; }));
            options.Add(new FloatMenuOption("Repeat Until X", delegate { selectedOrderMode = ProductionOrderMode.MakeUntilTarget; }));
            options.Add(new FloatMenuOption("Make Forever", delegate { selectedOrderMode = ProductionOrderMode.Forever; }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string GetOrderModeLabelForRecipe()
        {
            if (selectedOrderMode == ProductionOrderMode.MakeUntilTarget) return "Repeat Until X";
            if (selectedOrderMode == ProductionOrderMode.Forever) return "Forever";
            return "N items";
        }

        private void DrawRecipeList(Rect rect)
        {
            DrawSectionHeading(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 150f, 22f),
                showHiddenRecipes ? "HIDDEN RECIPES" : "RECIPES");
            Rect eyeRect = new Rect(rect.xMax - 34f, rect.y + 1f, 26f, 24f);
            bool canUseFolderVisibility = !string.IsNullOrEmpty(currentFolder);
            GUI.color = canUseFolderVisibility ? Color.white : Color.gray;
            if (Widgets.ButtonText(eyeRect, showHiddenRecipes ? "◉" : "◎") && canUseFolderVisibility)
            {
                showHiddenRecipes = !showHiddenRecipes;
                cachedRecipesTick = -1;
                recipeScrollPosition = Vector2.zero;
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(eyeRect, canUseFolderVisibility ? (showHiddenRecipes ? "Show visible recipes" : "Show hidden recipes in this folder") : "Hiding is unavailable in the “All” folder");

            Rect listRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
            List<RecipeDef> recipes = GetRecipesForCurrentTab();
            const float rowHeight = 48f;
            const float rowGap = 2f;
            float contentWidth = listRect.width - 16f;
            int rows = recipes.Count;
            float viewHeight = Mathf.Max(listRect.height, 5f + rows * (rowHeight + rowGap));
            Rect viewRect = new Rect(0f, 0f, contentWidth, viewHeight);
            Widgets.BeginScrollView(listRect, ref recipeScrollPosition, viewRect);
            if (recipes.Count == 0)
                Widgets.Label(new Rect(10f, 8f, viewRect.width - 20f, 28f), "BM_NoRecipes".Translate());
            else
            {
                GameComponent_BlocksMenuQueue folderState = Current.Game == null ? null : Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
                int first = Mathf.Max(0, Mathf.FloorToInt(recipeScrollPosition.y / (rowHeight + rowGap)));
                int last = Mathf.Min(rows, Mathf.CeilToInt((recipeScrollPosition.y + listRect.height) / (rowHeight + rowGap)) + 1);
                for (int i = first; i < last; i++)
                {
                    RecipeDef recipe = recipes[i];
                    float y = 4f + i * (rowHeight + rowGap);
                    Rect row = new Rect(4f, y, contentWidth - 8f, rowHeight);
                    bool selected = selectedRecipe == recipe;
                    Widgets.DrawBoxSolid(row, selected ? new Color(0.10f, 0.22f, 0.18f, 0.98f) : DashboardPanelRaised);
                    if (selected) Widgets.DrawBox(row, 1);
                    ThingDef product = GameComponent_BlocksMenuQueue.GetPrimaryProduct(recipe);
                    if (product != null) Widgets.ThingIcon(new Rect(row.x + 5f, row.y + 5f, 38f, 38f), product);
                    // Keep the recipe row close to the vanilla workbench recipe picker:
                    // product icon + title + ingredient icons/counts + compact actions.
                    float titleX = row.x + 50f;
                    float actionWidth = string.IsNullOrEmpty(currentFolder) ? 28f : 54f;
                    float ingredientWidth = Mathf.Min(114f, Mathf.Max(0f, row.width - 50f - actionWidth - 118f));
                    Rect ingredientRect = new Rect(row.xMax - actionWidth - ingredientWidth - 4f, row.y + 2f, ingredientWidth, row.height - 4f);
                    DrawRecipeIngredientIcons(recipe, ingredientRect);

                    float titleWidth = Mathf.Max(48f, ingredientRect.x - titleX - 5f);
                    Rect titleRect = new Rect(titleX, row.y + 5f, titleWidth, 22f);
                    Widgets.Label(titleRect, GetRecipeProductLabel(recipe));

                    Rect addRect = new Rect(row.xMax - actionWidth, row.y + 10f, 25f, 27f);
                    Rect selectRect = new Rect(row.x, row.y, row.width - actionWidth - ingredientWidth - 4f, row.height);
                    if (Widgets.ButtonInvisible(selectRect)) selectedRecipe = recipe;
                    if (Widgets.ButtonText(addRect, "+"))
                    {
                        selectedRecipe = recipe;
                        OpenOrderBuilder();
                    }
                    TooltipHandler.TipRegion(addRect, "Quickly add order");
                    if (!string.IsNullOrEmpty(currentFolder))
                    {
                        Rect hide = new Rect(row.xMax - 27f, row.y + 10f, 22f, 27f);
                        if (Widgets.ButtonText(hide, showHiddenRecipes ? "◉" : "⊘") && folderState != null)
                        {
                            folderState.SetRecipeHidden(recipe, currentFolder, !showHiddenRecipes);
                            if (!showHiddenRecipes && selectedRecipe == recipe) selectedRecipe = null;
                            cachedRecipesTick = -1;
                        }
                        TooltipHandler.TipRegion(hide, showHiddenRecipes ? "Restore recipe in this folder" : "Hide recipe in this folder only");
                    }
                    TooltipHandler.TipRegion(row, GetCachedIngredientTooltip(recipe));
                    Widgets.DrawLineHorizontal(row.x + 2f, row.yMax, row.width - 4f);
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawRecipeIngredientIcons(RecipeDef recipe, Rect rect)
        {
            if (recipe == null || recipe.ingredients == null || recipe.ingredients.Count == 0 || rect.width < 30f)
                return;

            const float slotWidth = 36f;
            const float iconSize = 23f;
            int maxSlots = Mathf.Max(1, Mathf.FloorToInt(rect.width / slotWidth));
            int drawn = 0;
            for (int i = 0; i < recipe.ingredients.Count && drawn < maxSlots; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                if (ingredient == null || ingredient.filter == null) continue;
                List<ThingDef> allowed = new List<ThingDef>(ingredient.filter.AllowedThingDefs);
                if (allowed.Count == 0) continue;

                // Vanilla recipe lists represent each ingredient group with one icon.
                // Use the first allowed definition as the representative, and expose
                // the full filter in the tooltip for ingredients accepting alternatives.
                ThingDef representative = allowed[0];
                float x = rect.x + drawn * slotWidth;
                Rect iconRect = new Rect(x + (slotWidth - iconSize) * 0.5f, rect.y + 1f, iconSize, iconSize);
                Widgets.ThingIcon(iconRect, representative);

                TextAnchor oldAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(x, rect.y + 24f, slotWidth, 15f), ingredient.GetBaseCount().ToString("0.##"));
                Text.Anchor = oldAnchor;

                string tooltip = GetIngredientDisplayName(ingredient.filter) + " × " + ingredient.GetBaseCount().ToString("0.##") + "\n" + GetCachedIngredientTooltip(recipe);
                TooltipHandler.TipRegion(iconRect, tooltip);
                drawn++;
            }

            int remaining = recipe.ingredients.Count - drawn;
            if (remaining > 0 && drawn < maxSlots)
            {
                float x = rect.x + drawn * slotWidth;
                Widgets.Label(new Rect(x, rect.y + 9f, slotWidth, 22f), "+" + remaining);
                TooltipHandler.TipRegion(new Rect(x, rect.y, slotWidth, rect.height), "Additional ingredient groups: " + remaining);
            }
        }

        private void DrawProductionQueue(Rect rect)
        {
            List<Building> allBenches = GetBenchesForCurrentFolder();
            List<Bill> activeOrders = new List<Bill>();
            for (int i = 0; i < allBenches.Count; i++)
            {
                Building bench = allBenches[i];
                if (bench == null || bench.Destroyed || !bench.Spawned) continue;
                BillStack stack = BlocksMenuBenchUtility.GetBillStack(bench);
                if (stack == null || stack.Bills == null) continue;
                List<Bill> visible = GetBillsForCurrentFolder(stack.Bills);
                for (int b = 0; b < visible.Count; b++)
                {
                    Bill bill = visible[b];
                    if (bill != null && !bill.DeletedOrDereferenced)
                        activeOrders.Add(bill);
                }
            }

            DrawSectionHeading(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, 22f),
                (string.IsNullOrEmpty(currentFolder) ? "ACTIVE ORDERS" : "ORDERS: " + GetFolderDisplayName(currentFolder)) + " · " + activeOrders.Count);

            Rect listRect = new Rect(rect.x + 5f, rect.y + 27f, rect.width - 10f, rect.height - 32f);
            if (activeOrders.Count == 0)
            {
                Widgets.Label(new Rect(listRect.x + 10f, listRect.y + 10f, listRect.width - 20f, 40f),
                    "No active orders in this folder yet.");
                return;
            }

            // Native Bill rows may include a status line, so reserve a safe scroll height.
            float viewWidth = Mathf.Max(100f, listRect.width - 16f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(listRect.height, activeOrders.Count * 80f + 4f));
            Widgets.BeginScrollView(listRect, ref queueScrollPosition, viewRect, true);
            try
            {
                float y = 2f;
                for (int i = 0; i < activeOrders.Count; i++)
                {
                    Bill bill = activeOrders[i];
                    if (bill == null || bill.DeletedOrDereferenced) continue;
                    Rect billRect;
                    // Reserve a dedicated 68 px action column on the right for
                    // "Go to workbench". Passing the reduced width to the native card also
                    // shortens its title area and shifts its native controls left,
                    // preventing any collision with the added button. This is scoped
                    // to this Blocks Menu list; workstation bill UI is never resized.
                    float nativeBillWidth = Mathf.Max(120f, viewWidth - 68f);
                    BillInterfaceHarmonyPatch.IsDrawingBlocksMenuQueue = true;
                    try
                    {
                        billRect = bill.DoInterface(0f, y, nativeBillWidth, i);
                    }
                    finally
                    {
                        BillInterfaceHarmonyPatch.IsDrawingBlocksMenuQueue = false;
                    }
                    // No additional gap: use the exact height returned by the native row.
                    y += billRect.height;
                }
            }
            finally
            {
                Widgets.EndScrollView();
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
        }

        private static string GetNativeBillDisplayLabel(Bill bill)
        {
            if (bill == null) return "Order";
            try
            {
                Type type = bill.GetType();
                string[] propertyNames = { "LabelCap", "Label" };
                for (int i = 0; i < propertyNames.Length; i++)
                {
                    PropertyInfo property = type.GetProperty(propertyNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        object value = property.GetValue(bill, null);
                        if (value != null && !string.IsNullOrEmpty(value.ToString())) return value.ToString();
                    }
                }
                FieldInfo labelField = type.GetField("label", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (labelField != null)
                {
                    object value = labelField.GetValue(bill);
                    if (value != null && !string.IsNullOrEmpty(value.ToString())) return value.ToString();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Blocks Menu] Failed to get the native Bill label: " + ex.Message);
            }
            return bill.recipe != null ? bill.recipe.LabelCap.ToString() : "Order";
        }

        private static void JumpToBench(Building bench)
        {
            if (bench == null || bench.Destroyed || !bench.Spawned) return;
            if (Find.CameraDriver != null)
                Find.CameraDriver.JumpToCurrentMapLoc(bench.Position);

            // Select the workstation when the current RimWorld build exposes a compatible Selector overload.
            try
            {
                object selector = Find.Selector;
                if (selector == null) return;
                MethodInfo[] methods = selector.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != "Select") continue;
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    if (parameters.Length < 1 || !parameters[0].ParameterType.IsAssignableFrom(bench.GetType())) continue;
                    object[] args = new object[parameters.Length];
                    args[0] = bench;
                    for (int p = 1; p < parameters.Length; p++)
                        args[p] = parameters[p].HasDefaultValue ? parameters[p].DefaultValue : (parameters[p].ParameterType.IsValueType ? Activator.CreateInstance(parameters[p].ParameterType) : null);
                    methods[i].Invoke(selector, args);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Blocks Menu] Failed to select the workbench after navigation: " + ex.Message);
            }
        }

        private List<Building> GetBenchesForCurrentFolder()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return new List<Building>();

            List<RecipeDef> recipes = GetRecipesForCurrentTab();
            // This method is called from GUI repaint. The recipe cache already has a
            // 300-tick lifetime, so reuse the matching bench union instead of allocating
            // a HashSet/list and sorting the same buildings every rendered frame.
            if (cachedFolderBenchesMap == map && cachedFolderBenchesName == currentFolder &&
                cachedFolderBenchesRecipeTick == cachedRecipesTick && cachedFolderBenches != null)
                return cachedFolderBenches;

            List<Building> result = new List<Building>();
            HashSet<Building> seen = new HashSet<Building>();
            for (int i = 0; i < recipes.Count; i++)
            {
                List<Building> found = RecipeBenchFinder.Find(map, recipes[i]);
                for (int b = 0; b < found.Count; b++)
                {
                    Building bench = found[b];
                    if (bench != null && !bench.Destroyed && bench.Spawned && seen.Add(bench))
                        result.Add(bench);
                }
            }

            result.Sort(delegate(Building a, Building b)
            {
                return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase);
            });
            cachedFolderBenches = result;
            cachedFolderBenchesMap = map;
            cachedFolderBenchesName = currentFolder;
            cachedFolderBenchesRecipeTick = cachedRecipesTick;
            return cachedFolderBenches;
        }

        private bool BenchHasRecipesForCurrentFolder(Building bench)
        {
            if (bench == null || bench.def == null || bench.def.AllRecipes == null) return false;
            List<RecipeDef> recipes = GetRecipesForCurrentTab();
            for (int i = 0; i < recipes.Count; i++)
                if (bench.def.AllRecipes.Contains(recipes[i])) return true;
            return false;
        }

        private List<Bill> GetBillsForCurrentFolder(List<Bill> source)
        {
            List<Bill> result = new List<Bill>();
            if (source == null) return result;
            GameComponent_BlocksMenuQueue component = Current.Game == null ? null : Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
            Map map = Find.CurrentMap;
            for (int i = 0; i < source.Count; i++)
            {
                Bill bill = source[i];
                Bill_Production production = bill as Bill_Production;
                if (production == null || production.recipe == null) continue;
                if (string.IsNullOrEmpty(currentFolder) || RecipeBelongsToCurrentFolder(map, component, production.recipe))
                    result.Add(bill);
            }
            return result;
        }

        private bool RecipeBelongsToCurrentFolder(Map map, GameComponent_BlocksMenuQueue component, RecipeDef recipe)
        {
            if (string.IsNullOrEmpty(currentFolder)) return true;
            bool belongs = IsBuiltInFolder(currentFolder)
                ? RecipeBelongsToBuiltInFolder(recipe, currentFolder)
                : component != null && component.RecipeBelongsToFolder(map, recipe, currentFolder);
            return belongs && !RecipeBelongsToOtherFolder(map, component, recipe, currentFolder);
        }

        private int CountBillsForCurrentFolder(List<Bill> source)
        {
            return GetBillsForCurrentFolder(source).Count;
        }



        private static void ChangeOrderAmount(BlocksMenuProductionOrder order, int delta)
        {
            if (order == null || order.Mode == ProductionOrderMode.Forever) return;
            if (order.Mode == ProductionOrderMode.MakeUntilTarget)
                order.TargetCount = Mathf.Clamp(order.TargetCount + delta, 1, 999999);
            else
                order.Count = Mathf.Clamp(order.Count + delta, 1, 999999);

            GameComponent_BlocksMenuQueue component = Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
            if (component != null) component.SynchronizeOrder(order);
        }

        private List<BlocksMenuProductionOrder> GetOrdersForCurrentTab()
        {
            List<BlocksMenuProductionOrder> result = new List<BlocksMenuProductionOrder>();
            GameComponent_BlocksMenuQueue component = Current.Game == null
                ? null : Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
            Map map = Find.CurrentMap;
            List<BlocksMenuProductionOrder> all = ProductionQueue;
            for (int i = 0; i < all.Count; i++)
            {
                BlocksMenuProductionOrder order = all[i];
                if (order == null || order.Recipe == null) continue;
                if (string.IsNullOrEmpty(currentFolder))
                {
                    result.Add(order);
                }
                else
                {
                    bool belongsToCurrentFolder = IsBuiltInFolder(currentFolder)
                        ? RecipeBelongsToBuiltInFolder(order.Recipe, currentFolder)
                        : component != null && component.RecipeBelongsToFolder(map, order.Recipe, currentFolder);

                    // A recipe/order is shown only in its owning production folder.
                    // If another built-in or custom folder also claims this recipe,
                    // hide it here to prevent duplicate active-order cards.
                    if (belongsToCurrentFolder && !RecipeBelongsToOtherFolder(map, component, order.Recipe, currentFolder))
                        result.Add(order);
                }
            }
            result.Sort(delegate(BlocksMenuProductionOrder a, BlocksMenuProductionOrder b)
            {
                if (a.IsFavorite != b.IsFavorite) return a.IsFavorite ? -1 : 1;
                return 0;
            });
            return result;
        }

        private static bool RecipeBelongsToOtherFolder(Map map, GameComponent_BlocksMenuQueue component,
            RecipeDef recipe, string currentFolderName)
        {
            if (recipe == null) return false;

            // Ownership priority: SpecialWear > Clothing. Clothing recipes that
            // overlap with workwear are owned by SpecialWear and are hidden everywhere else.
            if (currentFolderName != SpecialWearFolderKey && RecipeBelongsToBuiltInFolder(recipe, SpecialWearFolderKey))
                return true;
            if (currentFolderName != ClothingFolderKey && currentFolderName != SpecialWearFolderKey &&
                RecipeBelongsToBuiltInFolder(recipe, ClothingFolderKey))
                return true;

            if (component == null || component.CustomFolders == null) return false;
            for (int i = 0; i < component.CustomFolders.Count; i++)
            {
                string otherFolder = component.CustomFolders[i];
                if (string.IsNullOrEmpty(otherFolder) || otherFolder == currentFolderName) continue;
                if (component.RecipeBelongsToFolder(map, recipe, otherFolder)) return true;
            }
            return false;
        }

        private void DrawRecipeProducts(RecipeDef recipe, Rect rect)
        {
            if (recipe == null || recipe.ingredients == null || recipe.ingredients.Count == 0)
            {
                Widgets.Label(rect, "No ingredients");
                return;
            }

            float x = rect.x;
            const float iconSize = 22f;
            const float slotWidth = 42f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                if (ingredient == null || ingredient.filter == null || x + slotWidth > rect.xMax) continue;
                List<ThingDef> allowed = new List<ThingDef>(ingredient.filter.AllowedThingDefs);
                if (allowed.Count == 0) continue;

                // One representative icon per ingredient group, not one icon per allowed ThingDef.
                ThingDef representative = allowed[0];
                Rect iconRect = new Rect(x, rect.y + 1f, iconSize, iconSize);
                Widgets.ThingIcon(iconRect, representative);
                int available = 0;
                Map map = Find.CurrentMap;
                GameComponent_BlocksMenuQueue queue = Current.Game == null ? null : Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
                if (map != null && queue != null)
                {
                    for (int a = 0; a < allowed.Count; a++)
                        available += queue.CountInStockpiles(map, allowed[a]);
                }
                Widgets.Label(new Rect(x + iconSize + 1f, rect.y + 1f, slotWidth - iconSize - 1f, 22f),
                    ingredient.GetBaseCount().ToString("0.#") + "/" + available);
                TooltipHandler.TipRegion(iconRect, GetIngredientDisplayName(ingredient.filter) + "\n" + GetCachedIngredientTooltip(recipe) + "\nTotal allowed resources in stock: " + available);
                x += slotWidth;
            }
        }

        // Mirrors the compact grouping used by the vanilla resource filter: show the
        // deepest shared ThingCategoryDef instead of enumerating every accepted material.
        public static string GetIngredientDisplayName(ThingFilter filter)
        {
            if (filter == null) return "Resource not specified";
            List<ThingDef> allowed = new List<ThingDef>(filter.AllowedThingDefs);
            if (allowed.Count == 0) return "Resource not specified";
            if (allowed.Count == 1) return allowed[0].LabelCap.ToString();

            List<ThingCategoryDef> common = GetCategoryAncestors(allowed[0]);
            for (int i = 1; i < allowed.Count && common.Count > 0; i++)
            {
                List<ThingCategoryDef> ancestors = GetCategoryAncestors(allowed[i]);
                common.RemoveAll(delegate(ThingCategoryDef category) { return !ancestors.Contains(category); });
            }
            if (common.Count > 0)
                return common[0].LabelCap.ToString();

            // Some modded filters intentionally combine unrelated defs. Keep the label compact.
            return allowed[0].LabelCap.ToString() + " or " + allowed[1].LabelCap.ToString();
        }

        private static List<ThingCategoryDef> GetCategoryAncestors(ThingDef def)
        {
            List<ThingCategoryDef> result = new List<ThingCategoryDef>();
            if (def == null || def.thingCategories == null) return result;
            for (int i = 0; i < def.thingCategories.Count; i++)
            {
                ThingCategoryDef category = def.thingCategories[i];
                while (category != null && !result.Contains(category))
                {
                    result.Add(category);
                    category = category.parent;
                }
            }
            return result;
        }

        private string GetCachedIngredientTooltip(RecipeDef recipe)
        {
            string value;
            if (!IngredientTooltipCache.TryGetValue(recipe, out value))
            {
                value = BuildIngredientTooltip(recipe);
                IngredientTooltipCache[recipe] = value;
            }
            return value;
        }

        private List<Building> GetCachedSelectedBenches()
        {
            Map map = Find.CurrentMap;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (cachedBenchRecipe != selectedRecipe || cachedBenchMap != map ||
                cachedBenchesTick < 0 || tick - cachedBenchesTick >= 300)
            {
                cachedBenchRecipe = selectedRecipe;
                cachedBenchMap = map;
                cachedBenchesTick = tick;
                cachedSelectedBenches = map == null || selectedRecipe == null
                    ? new List<Building>()
                    : RecipeBenchFinder.Find(map, selectedRecipe);
            }
            return cachedSelectedBenches;
        }

        private static string BuildIngredientTooltip(RecipeDef recipe)
        {
            System.Text.StringBuilder tip = new System.Text.StringBuilder();
            tip.Append("Required resources:");
            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
            {
                tip.Append("\nNot required");
                return tip.ToString();
            }

            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                if (ingredient == null || ingredient.filter == null) continue;
                string displayName = GetIngredientDisplayName(ingredient.filter);
                if (displayName == "Resource not specified") continue;
                tip.Append("\n").Append(ingredient.GetBaseCount().ToString("0.##")).Append(" × ").Append(displayName);
            }
            return tip.ToString();
        }

        private static void OpenNativeBillEditor(BlocksMenuProductionOrder order)
        {
            if (order == null)
                return;

            List<BlocksMenuManagedBill> available = new List<BlocksMenuManagedBill>();
            if (order.IsImported)
            {
                if (order.SourceBill != null && order.SourceBench != null && !order.SourceBench.Destroyed)
                    available.Add(new BlocksMenuManagedBill { Bench = order.SourceBench, Bill = order.SourceBill });
            }
            else if (order.ManagedBills != null)
            {
                for (int i = 0; i < order.ManagedBills.Count; i++)
                {
                    BlocksMenuManagedBill managed = order.ManagedBills[i];
                    if (managed == null || managed.Bench == null || managed.Bench.Destroyed || managed.Bill == null || BlocksMenuBenchUtility.GetBillStack(managed.Bench) == null)
                        continue;
                    if (BlocksMenuBenchUtility.GetBillStack(managed.Bench).Bills.Contains(managed.Bill))
                        available.Add(managed);
                }
            }

            if (available.Count == 0)
            {
                Messages.Message("This order does not have a Bill on a workbench yet. Assign it to a compatible workbench first.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (available.Count == 1)
            {
                BlocksMenuNativeBillEditor.Open(available[0].Bill, available[0].Bench);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < available.Count; i++)
            {
                BlocksMenuManagedBill managed = available[i];
                Building bench = managed.Bench;
                Bill bill = managed.Bill;
                string label = bench.LabelCap + " — " + bench.Position;
                options.Add(new FloatMenuOption(label, delegate { BlocksMenuNativeBillEditor.Open(bill, bench); }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void JumpToOrderBench(BlocksMenuProductionOrder order)
        {
            if (order == null) return;
            Building bench = null;
            if (order.IsImported && order.SourceBench != null && !order.SourceBench.Destroyed)
                bench = order.SourceBench;
            if (bench == null && order.ManagedBills != null)
            {
                for (int i = 0; i < order.ManagedBills.Count; i++)
                {
                    BlocksMenuManagedBill managed = order.ManagedBills[i];
                    if (managed != null && managed.Bench != null && !managed.Bench.Destroyed)
                    {
                        bench = managed.Bench;
                        break;
                    }
                }
            }
            if (bench == null && Find.CurrentMap != null && order.Recipe != null)
            {
                List<Building> benches = RecipeBenchFinder.Find(Find.CurrentMap, order.Recipe);
                if (benches.Count > 0) bench = benches[0];
            }
            if (bench != null && Find.CameraDriver != null)
                Find.CameraDriver.JumpToCurrentMapLoc(bench.Position);
        }

        private static string GetOrderModeLabel(BlocksMenuProductionOrder order)
        {
            if (order.Mode == ProductionOrderMode.MakeUntilTarget) return "Make Until X";
            if (order.Mode == ProductionOrderMode.Forever) return "Make Forever";
            return "Make N";
        }

        private static void OpenOrderModeMenu(BlocksMenuProductionOrder order)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("Make N", delegate { ChangeOrderMode(order, ProductionOrderMode.MakeCount); }));
            options.Add(new FloatMenuOption("Make Until X", delegate { ChangeOrderMode(order, ProductionOrderMode.MakeUntilTarget); }));
            options.Add(new FloatMenuOption("Make Forever", delegate { ChangeOrderMode(order, ProductionOrderMode.Forever); }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ChangeOrderMode(BlocksMenuProductionOrder order, ProductionOrderMode mode)
        {
            if (order == null) return;
            order.Mode = mode;
            GameComponent_BlocksMenuQueue component = Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
            if (component != null) component.SynchronizeOrder(order);
        }

        private void OpenOrderBuilder()
        {
            if (selectedRecipe == null) return;
            int count = ParsePositive(countText, 1);
            Find.WindowStack.Add(new Dialog_BlocksMenuOrderBuilder(
                selectedRecipe, count, selectedOrderMode,
                ParsePositive(targetText, 200), Math.Max(0, ParsePositive(resumeAtText, 50))));
        }

        private void AddSelectedRecipeToQueue()
        {
            if (selectedRecipe == null)
                return;

            int count;
            if (!int.TryParse(countText, out count) || count < 1)
                count = 1;
            count = Math.Min(count, 999999);
            countText = count.ToString();

            for (int i = 0; i < ProductionQueue.Count; i++)
            {
                if (ProductionQueue[i].Recipe == selectedRecipe)
                {
                    ProductionQueue[i].Mode = selectedOrderMode;
                    if (selectedOrderMode == ProductionOrderMode.MakeUntilTarget)
                    {
                        int target;
                        int resume;
                        if (!int.TryParse(targetText, out target)) target = 200;
                        if (!int.TryParse(resumeAtText, out resume)) resume = 50;
                        ProductionQueue[i].TargetCount = Math.Max(1, target);
                        ProductionQueue[i].ResumeAtCount = Math.Max(0, Math.Min(resume, ProductionQueue[i].TargetCount));
                    }
                    else
                    {
                        ProductionQueue[i].Count = Math.Min(999999, ProductionQueue[i].Count + count);
                    }
                    Current.Game.GetComponent<GameComponent_BlocksMenuQueue>().SynchronizeOrder(ProductionQueue[i]);
                    return;
                }
            }

            BlocksMenuProductionOrder newOrder = new BlocksMenuProductionOrder
            {
                Recipe = selectedRecipe,
                Count = count,
                Mode = selectedOrderMode,
                TargetCount = ParsePositive(targetText, 200),
                ResumeAtCount = Math.Max(0, ParsePositive(resumeAtText, 50))
            };
            ProductionQueue.Add(newOrder);
            Current.Game.GetComponent<GameComponent_BlocksMenuQueue>().SynchronizeOrder(newOrder);
        }

        private static int ParsePositive(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? Math.Max(1, Math.Min(999999, parsed)) : fallback;
        }

        private List<RecipeDef> GetRecipesForCurrentTab()
        {
            string query = (searchText ?? string.Empty).Trim();
            Map currentMap = Find.CurrentMap;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            bool keyChanged = cachedSearchText != query || cachedFolderName != currentFolder || cachedRecipesMap != currentMap ||
                cachedShowHiddenRecipes != showHiddenRecipes;
            bool refreshDue = cachedRecipesTick < 0 || tick - cachedRecipesTick >= 300;

            if (!keyChanged && !refreshDue)
                return cachedRecipes;

            cachedSearchText = query;
            cachedFolderName = currentFolder;
            cachedShowHiddenRecipes = showHiddenRecipes;
            cachedRecipesMap = currentMap;
            cachedRecipesTick = tick;
            cachedRecipes = new List<RecipeDef>();

            // Catalogue construction is deliberately outside per-frame repaint work.
            if (currentMap == null)
                return cachedRecipes;

            List<RecipeDef> allRecipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            GameComponent_BlocksMenuQueue folderComponent = Current.Game == null
                ? null : Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();

            for (int i = 0; i < allRecipes.Count; i++)
            {
                RecipeDef recipe = allRecipes[i];
                if (recipe == null)
                    continue;
                if (RecipeBenchFinder.Find(currentMap, recipe).Count == 0)
                    continue;
                bool inAllFolder = string.IsNullOrEmpty(currentFolder);
                bool builtInFolder = IsBuiltInFolder(currentFolder);
                bool isHidden = !inAllFolder && !builtInFolder && folderComponent != null && folderComponent.IsRecipeHidden(recipe, currentFolder);
                if (showHiddenRecipes != isHidden)
                    continue;
                if (builtInFolder)
                {
                    if (!RecipeBelongsToBuiltInFolder(recipe, currentFolder)) continue;
                    // Use the same ownership rule for the recipe catalogue and active
                    // orders. In particular, Clothing must not reclaim workwear recipes.
                    if (RecipeBelongsToOtherFolder(currentMap, folderComponent, recipe, currentFolder)) continue;
                }
                else if (!inAllFolder && (folderComponent == null || !folderComponent.RecipeBelongsToFolder(currentMap, recipe, currentFolder)))
                    continue;
                if (!MatchesSearch(recipe, query))
                    continue;
                cachedRecipes.Add(recipe);
            }

            cachedRecipes.Sort(delegate(RecipeDef a, RecipeDef b)
            {
                return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase);
            });
            return cachedRecipes;
        }

        public static string GetRecipeProductLabel(RecipeDef recipe)
        {
            // Keep the original RimWorld recipe name everywhere in the menu.
            return recipe == null ? string.Empty : recipe.LabelCap.ToString();
        }

        private bool MatchesSearch(RecipeDef recipe, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;

            return BuildSearchableText(recipe).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static string BuildSearchableText(RecipeDef recipe)
        {
            return (recipe.defName ?? string.Empty) + " " +
                   (recipe.label ?? string.Empty) + " " + GetProductSummary(recipe);
        }

        private static string GetProductSummary(RecipeDef recipe)
        {
            if (recipe == null || recipe.products == null || recipe.products.Count == 0)
                return string.Empty;

            List<string> names = new List<string>();
            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDefCountClass product = recipe.products[i];
                if (product == null || product.thingDef == null)
                    continue;

                string name = product.thingDef.LabelCap.ToString();
                if (product.count > 1)
                    name += " ×" + product.count;
                names.Add(name);
            }

            return string.Join(", ", names.ToArray());
        }

        private static bool ContainsAny(string value, params string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (value.Contains(keywords[i]))
                    return true;
            }
            return false;
        }
    }

    // Shared drawing helpers for auxiliary dashboard dialogs.
    // Kept separate from the main tab because these dialogs are independent Window instances.
    /// <summary>
    /// Opens RimWorld's own Bill configuration window. Using the native dialog keeps
    /// the complete game/HSk IngredientFilter UI and any extra Bill controls intact.
    /// Reflection avoids hard-coding constructor overloads that differ between game builds.
    /// </summary>
    internal static class BlocksMenuNativeBillEditor
    {
        public static void Open(Bill bill, Building bench)
        {
            if (bill == null || bench == null || bench.Destroyed)
                return;

            try
            {
                Type dialogType = typeof(Bill).Assembly.GetType("RimWorld.Dialog_BillConfig", false);
                if (dialogType == null)
                {
                    Log.Error("[Blocks Menu] Native RimWorld.Dialog_BillConfig type was not found.");
                    Messages.Message("Could not find the native Bill editor in this RimWorld build.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                // RimWorld 1.6 expects the production bill AND the position of its bill giver.
                // The position is used by the native dialog to draw the ingredient search radius.
                Bill_Production productionBill = bill as Bill_Production;
                if (productionBill == null || productionBill.billStack == null || productionBill.billStack.billGiver == null)
                {
                    Log.Error("[Blocks Menu] The native editor requires a Bill_Production linked to a workbench.");
                    Messages.Message("This Bill is not linked to a workbench or is not a production order.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                IntVec3 billGiverPosition = bench.Position;
                System.Reflection.ConstructorInfo[] constructors = dialogType.GetConstructors(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                for (int i = 0; i < constructors.Length; i++)
                {
                    System.Reflection.ParameterInfo[] parameters = constructors[i].GetParameters();
                    if (parameters.Length != 2 ||
                        !parameters[0].ParameterType.IsAssignableFrom(productionBill.GetType()) ||
                        parameters[1].ParameterType != typeof(IntVec3))
                        continue;

                    Window dialog = constructors[i].Invoke(new object[] { productionBill, billGiverPosition }) as Window;
                    if (dialog != null)
                    {
                        Find.WindowStack.Add(dialog);
                        return;
                    }
                }

                Log.Error("[Blocks Menu] Compatible Dialog_BillConfig(Bill_Production, IntVec3) constructor not found.");
                Messages.Message("The native Bill editor was found, but its constructor differs from the expected RimWorld 1.6 API.", MessageTypeDefOf.RejectInput, false);
            }
            catch (Exception ex)
            {
                Log.Error("[Blocks Menu] Failed to open the native Bill editor: " + ex);
                Messages.Message("Error opening the native Bill editor. See Player.log for details.", MessageTypeDefOf.RejectInput, false);
            }
        }
    }

    public class Dialog_BlocksMenuOrderRename : Window
    {
        private readonly BlocksMenuProductionOrder order;
        private string value;
        public Dialog_BlocksMenuOrderRename(BlocksMenuProductionOrder order, string initial)
        {
            this.order = order;
            value = initial ?? string.Empty;
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = true;
        }
        public override Vector2 InitialSize { get { return new Vector2(420f, 150f); } }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0f, 0f, rect.width, 28f), "Rename order card");
            value = Widgets.TextField(new Rect(0f, 38f, rect.width, 32f), value ?? string.Empty);
            if (Widgets.ButtonText(new Rect(rect.width - 190f, 82f, 90f, 30f), "Reset")) value = string.Empty;
            if (Widgets.ButtonText(new Rect(rect.width - 92f, 82f, 92f, 30f), "Save"))
            { if (order != null) order.CustomLabel = (value ?? string.Empty).Trim(); Close(); }
        }
    }

    public abstract class BlocksMenuDashboardDialog : Window
    {
        protected static readonly Color DashboardPanel = new Color(0.105f, 0.095f, 0.080f, 0.99f);
        protected static readonly Color DashboardPanelRaised = new Color(0.155f, 0.137f, 0.108f, 0.99f);
        protected static readonly Color DashboardBorder = new Color(0.38f, 0.315f, 0.205f, 0.96f);
        protected static readonly Color DashboardAccent = new Color(0.88f, 0.66f, 0.30f, 1f);
        protected static readonly Color DashboardGreen = new Color(0.42f, 0.76f, 0.38f, 1f);

        protected static void DrawPanel(Rect rect, Color fill)
        {
            Widgets.DrawBoxSolid(rect, fill);
            Color previous = GUI.color;
            GUI.color = DashboardBorder;
            Widgets.DrawBox(rect, 1);
            GUI.color = previous;
        }

        protected static void DrawSectionHeading(Rect rect, string label)
        {
            Color previous = GUI.color;
            GUI.color = DashboardAccent;
            Widgets.Label(rect, label);
            GUI.color = previous;
        }
    }

    /// <summary>Full searchable DefDatabase recipe picker for adding one-off recipes to a folder.</summary>
    public class Dialog_BlocksMenuRecipePicker : BlocksMenuDashboardDialog
    {
        private readonly GameComponent_BlocksMenuQueue component;
        private readonly string folder;
        private string search;
        private Vector2 scroll;
        private string status = string.Empty;
        private List<RecipeDef> cachedMatches = new List<RecipeDef>();
        private string cachedSearch;
        private Map cachedMap;

        public Dialog_BlocksMenuRecipePicker(GameComponent_BlocksMenuQueue component, string folder, string initialSearch)
        {
            this.component = component;
            this.folder = folder;
            search = initialSearch ?? string.Empty;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize { get { return new Vector2(560f, 620f); } }

        public override void DoWindowContents(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 32f), DashboardPanelRaised);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, 32f), DashboardAccent);
            DrawSectionHeading(new Rect(rect.x + 10f, rect.y + 2f, rect.width - 20f, 28f), "FULL RECIPE LIST  ·  " + folder);

            search = Widgets.TextField(new Rect(rect.x + 8f, rect.y + 38f, rect.width - 16f, 28f), search ?? string.Empty);
            if (!string.IsNullOrEmpty(status))
                Widgets.Label(new Rect(rect.x + 8f, rect.y + 68f, rect.width - 16f, 22f), status);

            Rect body = new Rect(rect.x, rect.y + 94f, rect.width, rect.height - 130f);
            DrawPanel(body, DashboardPanel);
            string query = (search ?? string.Empty).Trim();
            Map currentMap = Find.CurrentMap;
            if (cachedSearch != query || cachedMap != currentMap)
            {
                cachedSearch = query;
                cachedMap = currentMap;
                cachedMatches = new List<RecipeDef>();
                List<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    RecipeDef recipe = all[i];
                    if (recipe == null) continue;
                    if (query.Length > 0 &&
                        MainTabWindow_CraftingMenu.GetRecipeProductLabel(recipe).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        recipe.LabelCap.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                        recipe.defName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;
                    // Manual folder additions are selected from recipes actually supported
                    // by at least one currently built Bill-giver, not from every Def in the game.
                    if (currentMap == null || RecipeBenchFinder.Find(currentMap, recipe).Count == 0)
                        continue;
                    cachedMatches.Add(recipe);
                }
                cachedMatches.Sort(delegate(RecipeDef a, RecipeDef b)
                {
                    return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase);
                });
            }
            List<RecipeDef> matches = cachedMatches;

            HashSet<RecipeDef> manuallyAdded = new HashSet<RecipeDef>(component.GetFolderManualRecipes(folder));
            const float rowHeight = 30f;
            Rect view = new Rect(0f, 0f, body.width - 18f, Math.Max(body.height, matches.Count * rowHeight));
            Widgets.BeginScrollView(body, ref scroll, view);
            for (int i = 0; i < matches.Count; i++)
            {
                RecipeDef recipe = matches[i];
                float y = i * rowHeight;
                Widgets.Label(new Rect(8f, y + 2f, view.width - 116f, 26f), MainTabWindow_CraftingMenu.GetRecipeProductLabel(recipe));
                bool alreadyAdded = manuallyAdded.Contains(recipe);
                if (alreadyAdded)
                    Widgets.Label(new Rect(view.width - 104f, y + 2f, 96f, 26f), "Added");
                else
                {
                    RecipeDef captured = recipe;
                    if (Widgets.ButtonText(new Rect(view.width - 104f, y + 2f, 96f, 26f), "Add"))
                    {
                        component.AddManualRecipeToFolder(folder, captured);
                        status = "Recipe added: " + captured.LabelCap;
                    }
                }
            }
            Widgets.EndScrollView();

            if (Widgets.ButtonText(new Rect(rect.xMax - 108f, rect.yMax - 30f, 100f, 26f), "Done"))
                Close();
        }
    }

    /// <summary>
    /// Selects the concrete workbenches (and therefore the Bills) before the order is created.
    /// The native Bill editor remains available on each created queue card.
    /// </summary>
    public class Dialog_BlocksMenuOrderBuilder : BlocksMenuDashboardDialog
    {
        private readonly RecipeDef recipe;
        private readonly int count;
        private readonly ProductionOrderMode mode;
        private readonly int targetCount;
        private readonly int resumeAtCount;
        private readonly List<Building> compatibleBenches;
        private readonly HashSet<Building> selectedBenches = new HashSet<Building>();
        private Vector2 scroll;

        public Dialog_BlocksMenuOrderBuilder(RecipeDef recipe, int count, ProductionOrderMode mode, int targetCount, int resumeAtCount)
        {
            this.recipe = recipe;
            this.count = Math.Max(1, count);
            this.mode = mode;
            this.targetCount = Math.Max(1, targetCount);
            this.resumeAtCount = Math.Max(0, Math.Min(resumeAtCount, this.targetCount));
            Map map = Find.CurrentMap;
            compatibleBenches = map == null || recipe == null ? new List<Building>() : RecipeBenchFinder.Find(map, recipe);
            for (int i = 0; i < compatibleBenches.Count; i++) selectedBenches.Add(compatibleBenches[i]);
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize { get { return new Vector2(900f, 650f); } }

        public override void DoWindowContents(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 38f), DashboardPanelRaised);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, 38f), DashboardAccent);
            DrawSectionHeading(new Rect(rect.x + 14f, rect.y + 3f, rect.width - 28f, 30f), "BLOCKS MENU  /  CREATE PRODUCTION ORDER");

            float top = rect.y + 48f;
            float gap = 10f;
            float leftWidth = 275f;
            float rightWidth = 270f;
            float centerWidth = rect.width - leftWidth - rightWidth - gap * 2f;
            float bodyHeight = rect.height - 100f;

            Rect productPanel = new Rect(rect.x, top, leftWidth, bodyHeight);
            Rect settingsPanel = new Rect(productPanel.xMax + gap, top, centerWidth, bodyHeight);
            Rect benchesPanel = new Rect(settingsPanel.xMax + gap, top, rightWidth, bodyHeight);
            DrawPanel(productPanel, DashboardPanel);
            DrawPanel(settingsPanel, DashboardPanel);
            DrawPanel(benchesPanel, DashboardPanel);

            DrawSectionHeading(new Rect(productPanel.x + 10f, productPanel.y + 8f, leftWidth - 20f, 24f), "PRODUCT & MATERIALS");
            if (recipe != null)
            {
                ThingDef product = GameComponent_BlocksMenuQueue.GetPrimaryProduct(recipe);
                if (product != null) Widgets.ThingIcon(new Rect(productPanel.x + 12f, productPanel.y + 42f, 76f, 76f), product);
                Widgets.Label(new Rect(productPanel.x + 96f, productPanel.y + 43f, leftWidth - 108f, 56f), MainTabWindow_CraftingMenu.GetRecipeProductLabel(recipe));
                Widgets.Label(new Rect(productPanel.x + 96f, productPanel.y + 99f, leftWidth - 108f, 38f),
                    mode == ProductionOrderMode.MakeUntilTarget ? "Repeat until " + targetCount : mode == ProductionOrderMode.Forever ? "Infinite production" : "Count: " + count);
            }
            Widgets.DrawBoxSolid(new Rect(productPanel.x + 8f, productPanel.y + 144f, leftWidth - 16f, 1f), DashboardBorder);
            DrawSectionHeading(new Rect(productPanel.x + 10f, productPanel.y + 154f, leftWidth - 20f, 24f), "REQUIRED MATERIALS");
            float ingredientY = productPanel.y + 184f;
            if (recipe != null && recipe.ingredients != null)
            {
                for (int i = 0; i < recipe.ingredients.Count && ingredientY + 38f < productPanel.yMax - 8f; i++)
                {
                    IngredientCount ingredient = recipe.ingredients[i];
                    if (ingredient == null || ingredient.filter == null) continue;
                    List<ThingDef> allowed = new List<ThingDef>(ingredient.filter.AllowedThingDefs);
                    if (allowed.Count > 0) Widgets.ThingIcon(new Rect(productPanel.x + 12f, ingredientY, 28f, 28f), allowed[0]);
                    Widgets.Label(new Rect(productPanel.x + 46f, ingredientY, leftWidth - 58f, 30f),
                        ingredient.GetBaseCount().ToString("0.##") + " × " + MainTabWindow_CraftingMenu.GetIngredientDisplayName(ingredient.filter));
                    ingredientY += 34f;
                }
            }

            DrawSectionHeading(new Rect(settingsPanel.x + 10f, settingsPanel.y + 8f, centerWidth - 20f, 24f), "PRODUCTION SETTINGS");
            Widgets.Label(new Rect(settingsPanel.x + 12f, settingsPanel.y + 42f, centerWidth - 24f, 22f), "Execution Mode");
            string modeLabel = mode == ProductionOrderMode.MakeUntilTarget ? "Repeat Until X" : mode == ProductionOrderMode.Forever ? "Forever" : "Make N";
            Widgets.DrawBoxSolid(new Rect(settingsPanel.x + 10f, settingsPanel.y + 66f, centerWidth - 20f, 38f), DashboardPanelRaised);
            Widgets.Label(new Rect(settingsPanel.x + 18f, settingsPanel.y + 70f, centerWidth - 36f, 28f), modeLabel);
            Widgets.Label(new Rect(settingsPanel.x + 12f, settingsPanel.y + 116f, 112f, 26f), mode == ProductionOrderMode.MakeUntilTarget ? "Target stock:" : "Count:");
            Widgets.Label(new Rect(settingsPanel.x + 128f, settingsPanel.y + 116f, centerWidth - 140f, 26f), mode == ProductionOrderMode.MakeUntilTarget ? targetCount.ToString() : mode == ProductionOrderMode.Forever ? "∞" : count.ToString());
            if (mode == ProductionOrderMode.MakeUntilTarget)
            {
                Widgets.Label(new Rect(settingsPanel.x + 12f, settingsPanel.y + 148f, centerWidth - 24f, 42f), "The native Bill uses the TargetCount mode. The game determines how many cycles must be completed.");
                Widgets.Label(new Rect(settingsPanel.x + 12f, settingsPanel.y + 194f, 112f, 24f), "Resume at:");
                Widgets.Label(new Rect(settingsPanel.x + 128f, settingsPanel.y + 194f, centerWidth - 140f, 24f), resumeAtCount.ToString());
            }
            Widgets.DrawBoxSolid(new Rect(settingsPanel.x + 10f, settingsPanel.y + 232f, centerWidth - 20f, 1f), DashboardBorder);
            DrawSectionHeading(new Rect(settingsPanel.x + 10f, settingsPanel.y + 242f, centerWidth - 20f, 24f), "HOW THE ORDER WILL BE CREATED");
            Widgets.Label(new Rect(settingsPanel.x + 12f, settingsPanel.y + 272f, centerWidth - 24f, 84f),
                "A separate native Bill will be created at each selected workbench. Its ingredient filters, quality, and other settings can be configured independently in the RimWorld editor.");

            DrawSectionHeading(new Rect(benchesPanel.x + 10f, benchesPanel.y + 8f, rightWidth - 20f, 24f), "COMPATIBLE WORKBENCHES");
            Widgets.Label(new Rect(benchesPanel.x + 10f, benchesPanel.y + 34f, rightWidth - 20f, 38f), "Select the workbenches where separate Bills should be created.");
            if (Widgets.ButtonText(new Rect(benchesPanel.x + 10f, benchesPanel.y + 76f, (rightWidth - 25f) / 2f, 26f), "Select All"))
                for (int i = 0; i < compatibleBenches.Count; i++) selectedBenches.Add(compatibleBenches[i]);
            if (Widgets.ButtonText(new Rect(benchesPanel.x + 15f + (rightWidth - 25f) / 2f, benchesPanel.y + 76f, (rightWidth - 25f) / 2f, 26f), "Deselect All")) selectedBenches.Clear();

            Rect benchList = new Rect(benchesPanel.x + 8f, benchesPanel.y + 110f, rightWidth - 16f, bodyHeight - 120f);
            float rowHeight = 42f;
            Rect benchView = new Rect(0f, 0f, benchList.width - 16f, Math.Max(benchList.height, compatibleBenches.Count * rowHeight + 8f));
            Widgets.BeginScrollView(benchList, ref scroll, benchView);
            if (compatibleBenches.Count == 0)
                Widgets.Label(new Rect(8f, 8f, benchView.width - 16f, 44f), "There are no built workbenches on this map compatible with this recipe.");
            for (int i = 0; i < compatibleBenches.Count; i++)
            {
                Building bench = compatibleBenches[i];
                float y = 5f + i * rowHeight;
                Widgets.DrawBoxSolid(new Rect(4f, y, benchView.width - 8f, rowHeight - 3f), DashboardPanelRaised);
                bool chosen = selectedBenches.Contains(bench);
                Widgets.CheckboxLabeled(new Rect(10f, y + 3f, benchView.width - 20f, 30f), bench.LabelCap + "  ·  " + bench.Position, ref chosen);
                if (chosen) selectedBenches.Add(bench); else selectedBenches.Remove(bench);
            }
            Widgets.EndScrollView();

            float buttonY = rect.yMax - 40f;
            if (Widgets.ButtonText(new Rect(rect.xMax - 238f, buttonY, 144f, 30f), "Create Bills")) CreateOrder();
            if (Widgets.ButtonText(new Rect(rect.xMax - 88f, buttonY, 80f, 30f), "Cancel")) Close();
            Widgets.Label(new Rect(rect.x + 8f, buttonY + 3f, rect.width - 350f, 26f), "Workbenches selected: " + selectedBenches.Count + " / " + compatibleBenches.Count);
        }

        private void CreateOrder()
        {
            if (recipe == null || selectedBenches.Count == 0)
            {
                Messages.Message("Select at least one compatible workbench.", MessageTypeDefOf.RejectInput, false);
                return;
            }
            GameComponent_BlocksMenuQueue component = Current.Game == null ? null : Current.Game.GetComponent<GameComponent_BlocksMenuQueue>();
            if (component == null) return;
            BlocksMenuProductionOrder order = new BlocksMenuProductionOrder
            {
                Recipe = recipe,
                Count = count,
                Mode = mode,
                TargetCount = targetCount,
                ResumeAtCount = resumeAtCount,
                TargetBenches = new List<Building>()
            };
            for (int i = 0; i < compatibleBenches.Count; i++)
                if (selectedBenches.Contains(compatibleBenches[i])) order.TargetBenches.Add(compatibleBenches[i]);
            component.Orders.Add(order);
            component.SynchronizeOrder(order);
            Close();
        }
    }

    public class Dialog_BlocksMenuRecipeDetails : BlocksMenuDashboardDialog
    {
        private readonly RecipeDef recipe;
        private Vector2 scroll;
        private List<Building> cachedBenches;

        public Dialog_BlocksMenuRecipeDetails(RecipeDef recipe)
        {
            this.recipe = recipe;
            Map map = Find.CurrentMap;
            cachedBenches = map == null || recipe == null ? new List<Building>() : RecipeBenchFinder.Find(map, recipe);
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize { get { return new Vector2(620f, 560f); } }

        public override void DoWindowContents(Rect inRect)
        {
            if (recipe == null) { Widgets.Label(inRect, "Recipe unavailable"); return; }
            Widgets.DrawBoxSolid(new Rect(inRect.x, inRect.y, inRect.width, 32f), DashboardPanelRaised);
            Widgets.DrawBoxSolid(new Rect(inRect.x, inRect.y, 4f, 32f), DashboardAccent);
            DrawSectionHeading(new Rect(inRect.x + 10f, inRect.y + 2f, inRect.width - 20f, 28f), "RECIPE DETAILS  ·  " + recipe.LabelCap);
            Rect body = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 72f);
            DrawPanel(body, DashboardPanel);
            float contentHeight = 50f + (recipe.ingredients == null ? 0 : recipe.ingredients.Count * 48f) + 100f;
            Rect view = new Rect(0f, 0f, body.width - 18f, Math.Max(body.height, contentHeight));
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            Widgets.Label(new Rect(8f, y, view.width - 16f, 24f), "Recipe Preview"); y += 26f;
            Widgets.Label(new Rect(8f, y, view.width - 16f, 38f), "Full filters and restrictions can be configured in the native Bill editor using the Bill button on the order card."); y += 42f;
            Widgets.Label(new Rect(8f, y, view.width - 16f, 24f), "Required Ingredients"); y += 28f;
            if (recipe.ingredients != null)
            {
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ingredient = recipe.ingredients[i];
                    if (ingredient == null || ingredient.filter == null) continue;
                    Widgets.Label(new Rect(8f, y, view.width - 16f, 42f),
                        ingredient.GetBaseCount().ToString("0.##") + " × " + MainTabWindow_CraftingMenu.GetIngredientDisplayName(ingredient.filter));
                    y += 46f;
                }
            }
            y += 8f;
            Widgets.Label(new Rect(8f, y, view.width - 16f, 24f), "Compatible Workbenches"); y += 28f;
            List<Building> benches = cachedBenches ?? new List<Building>();
            for (int i = 0; i < benches.Count; i++)
            {
                Building bench = benches[i];
                Widgets.Label(new Rect(8f, y, view.width - 16f, 24f), bench.LabelCap + " — " + bench.Position);
                y += 26f;
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(inRect.x + inRect.width - 150f, inRect.yMax - 30f, 140f, 26f), "Close")) Close();
        }
    }

    /// <summary>Scrollable directory of every user folder, so older folders remain editable.</summary>
    public class Dialog_BlocksMenuFolderManager : BlocksMenuDashboardDialog
    {
        private readonly GameComponent_BlocksMenuQueue component;
        private Vector2 scroll;
        public Dialog_BlocksMenuFolderManager(GameComponent_BlocksMenuQueue component)
        {
            this.component = component;
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
        }
        public override Vector2 InitialSize { get { return new Vector2(500f, 520f); } }
        public override void DoWindowContents(Rect rect)
        {
            DrawSectionHeading(new Rect(8f, 4f, rect.width - 16f, 28f), "PRODUCTION FOLDER MANAGEMENT");
            Widgets.Label(new Rect(8f, 32f, rect.width - 16f, 24f), "Select a folder to edit its workbenches, recipes, or name.");
            Rect listRect = new Rect(4f, 60f, rect.width - 8f, rect.height - 100f);
            DrawPanel(listRect, DashboardPanel);
            int count = component == null || component.CustomFolders == null ? 0 : component.CustomFolders.Count;
            float contentHeight = Mathf.Max(listRect.height - 8f, count * 42f + 8f);
            Rect view = new Rect(0f, 0f, listRect.width - 18f, contentHeight);
            Widgets.BeginScrollView(listRect, ref scroll, view);
            float y = 4f;
            if (count == 0) Widgets.Label(new Rect(8f, y, view.width - 16f, 30f), "There are no custom folders yet.");
            for (int i = 0; i < count; i++)
            {
                string folder = component.CustomFolders[i];
                Rect row = new Rect(6f, y, view.width - 12f, 36f);
                Widgets.DrawBoxSolid(row, DashboardPanelRaised);
                Widgets.Label(new Rect(row.x + 10f, row.y + 2f, row.width - 118f, 32f), folder);
                List<string> benches = component.GetFolderBenchDefs(folder);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.xMax - 112f, row.y + 2f, 38f, 32f), benches.Count.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonText(new Rect(row.xMax - 70f, row.y + 4f, 64f, 28f), "Edit"))
                    Find.WindowStack.Add(new Dialog_BlocksMenuFolderEditor(component, folder));
                y += 42f;
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(rect.width - 222f, rect.height - 32f, 104f, 28f), "+ Folder"))
                Find.WindowStack.Add(new Dialog_BlocksMenuFolderEditor(component, null));
            if (Widgets.ButtonText(new Rect(rect.width - 110f, rect.height - 32f, 102f, 28f), "Close")) Close();
        }
    }

    /// <summary>Редактор пользовательской папки: станки текущей колонии + вручную выбранные рецепты.</summary>
    public class Dialog_BlocksMenuFolderEditor : BlocksMenuDashboardDialog
    {
        private readonly GameComponent_BlocksMenuQueue component;
        private readonly string editingFolder;
        private string folderName;
        private string recipeSearch = string.Empty;
        private Vector2 scroll;
        private readonly HashSet<string> selectedBenches = new HashSet<string>();
        private List<RecipeDef> cachedAutoRecipes = new List<RecipeDef>();
        private string cachedAutoQuery;
        private string cachedAutoBenchKey;
        private Map cachedAutoMap;

        public Dialog_BlocksMenuFolderEditor(GameComponent_BlocksMenuQueue component, string folder)
        {
            this.component = component;
            editingFolder = folder;
            folderName = folder ?? string.Empty;
            if (component != null && !string.IsNullOrEmpty(folder))
                foreach (string id in component.GetFolderBenchDefs(folder)) selectedBenches.Add(id);
            doCloseX = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize { get { return new Vector2(620f, 650f); } }

        public override void DoWindowContents(Rect rect)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 30f), DashboardPanelRaised);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, 30f), DashboardAccent);
            DrawSectionHeading(new Rect(rect.x + 10f, rect.y + 2f, rect.width - 20f, 26f), editingFolder == null ? "CREATE FOLDER" : "CONFIGURE FOLDER");
            Widgets.Label(new Rect(rect.x, rect.y + 34f, 100f, 24f), "Name:");
            folderName = Widgets.TextField(new Rect(rect.x + 104f, rect.y + 34f, rect.width - 108f, 24f), folderName ?? string.Empty);
            Rect body = new Rect(rect.x, rect.y + 66f, rect.width, rect.height - 106f);
            DrawPanel(body, DashboardPanel);
            string query = (recipeSearch ?? string.Empty).Trim();
            string activeFolderName = editingFolder ?? (folderName ?? string.Empty).Trim();
            Map map = Find.CurrentMap;
            List<ThingDef> benchDefs = GetBuiltBenchDefs(map);
            // Keep previously selected benches visible even if they were dismantled,
            // moved off-map, or temporarily unavailable. Otherwise the player cannot
            // remove a stale bench assignment from the folder.
            HashSet<string> visibleBenchIds = new HashSet<string>();
            HashSet<string> builtBenchIds = new HashSet<string>();
            for (int i = 0; i < benchDefs.Count; i++)
                if (benchDefs[i] != null)
                {
                    visibleBenchIds.Add(benchDefs[i].defName);
                    builtBenchIds.Add(benchDefs[i].defName);
                }
            foreach (string selectedId in selectedBenches)
            {
                if (string.IsNullOrEmpty(selectedId) || visibleBenchIds.Contains(selectedId)) continue;
                ThingDef savedBench = DefDatabase<ThingDef>.GetNamedSilentFail(selectedId);
                if (savedBench != null)
                {
                    benchDefs.Add(savedBench);
                    visibleBenchIds.Add(selectedId);
                }
            }
            benchDefs.Sort(delegate(ThingDef a, ThingDef b)
            {
                return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase);
            });
            List<RecipeDef> manual = component.GetFolderManualRecipes(activeFolderName);
            List<RecipeDef> automaticRecipes = map != null && selectedBenches.Count > 0
                ? GetCachedAutomaticRecipes(map, query) : new List<RecipeDef>();
            float contentHeight = 150f + benchDefs.Count * 28f + manual.Count * 26f + automaticRecipes.Count * 26f;
            contentHeight = Mathf.Max(body.height, contentHeight + 80f);
            Rect view = new Rect(0f, 0f, body.width - 18f, contentHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            Widgets.Label(new Rect(6f, y, view.width - 12f, 26f), "COLONY WORKBENCHES"); y += 30f;
            Widgets.Label(new Rect(6f, y, view.width - 12f, 34f), "Select workbenches. The folder will include all recipes available at at least one selected workbench."); y += 38f;
            if (benchDefs.Count == 0)
            {
                Widgets.Label(new Rect(8f, y, view.width - 16f, 26f), "There are no production workbenches built on the current map yet."); y += 30f;
            }
            for (int i = 0; i < benchDefs.Count; i++)
            {
                ThingDef def = benchDefs[i];
                bool chosen = selectedBenches.Contains(def.defName);
                bool builtNow = builtBenchIds.Contains(def.defName);
                string benchLabel = def.LabelCap.ToString() + (builtNow ? string.Empty : " (not built on this map)");
                Widgets.CheckboxLabeled(new Rect(8f, y, view.width - 16f, 26f), benchLabel, ref chosen);
                if (chosen) selectedBenches.Add(def.defName); else selectedBenches.Remove(def.defName);
                y += 28f;
            }
            y += 12f;
            Widgets.Label(new Rect(6f, y, view.width - 12f, 26f), "INDIVIDUAL RECIPES"); y += 30f;
            recipeSearch = Widgets.TextField(new Rect(8f, y, view.width - 16f, 26f), recipeSearch ?? string.Empty); y += 32f;
            if (Widgets.ButtonText(new Rect(8f, y, 150f, 26f), "Add Recipe"))
            {
                if (activeFolderName.Length == 0)
                    Messages.Message("Enter a folder name first.", MessageTypeDefOf.RejectInput, false);
                else
                {
                    component.CreateFolder(activeFolderName);
                    OpenRecipePicker(activeFolderName);
                }
            }
            y += 32f;
            for (int i = 0; i < manual.Count; i++)
            {
                RecipeDef recipe = manual[i];
                Widgets.Label(new Rect(8f, y, view.width - 120f, 24f), recipe.LabelCap.ToString());
                if (Widgets.ButtonText(new Rect(view.width - 104f, y, 96f, 24f), "Remove"))
                    component.ExcludeRecipeFromFolder(activeFolderName, recipe);
                y += 26f;
            }
            if (map != null && selectedBenches.Count > 0)
            {
                y += 8f;
                Widgets.Label(new Rect(6f, y, view.width - 12f, 24f), "RECIPES FROM SELECTED WORKBENCHES"); y += 26f;
                for (int i = 0; i < automaticRecipes.Count; i++)
                {
                    RecipeDef candidate = automaticRecipes[i];
                    Widgets.Label(new Rect(8f, y, view.width - 120f, 24f), candidate.LabelCap.ToString());
                    RecipeDef captured = candidate;
                    if (Widgets.ButtonText(new Rect(view.width - 104f, y, 96f, 24f), "Exclude"))
                    {
                        component.ExcludeRecipeFromFolder(activeFolderName, captured);
                        cachedAutoRecipes.Remove(captured);
                    }
                    y += 26f;
                }
            }
            Widgets.EndScrollView();
            if (Widgets.ButtonText(new Rect(rect.x + rect.width - 260f, rect.yMax - 30f, 120f, 26f), "Save"))
            {
                string name = (folderName ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    Messages.Message("Enter a folder name.", MessageTypeDefOf.RejectInput, false);
                }
                else if (editingFolder != null && name != editingFolder && component.CustomFolders.Contains(name))
                {
                    Messages.Message("A folder with this name already exists.", MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    if (editingFolder == null) component.CreateFolder(name);
                    else if (name != editingFolder) component.RenameFolder(editingFolder, name);
                    component.SetFolderBenchDefs(name, selectedBenches);
                    Messages.Message("Folder “" + name + "” saved.", MessageTypeDefOf.PositiveEvent, false);
                    Close();
                }
            }
            if (editingFolder != null && Widgets.ButtonText(new Rect(rect.x + 8f, rect.yMax - 30f, 120f, 26f), "Delete Folder"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "Delete folder “" + editingFolder + "”? Recipes and Bills will not be deleted.",
                    delegate { component.DeleteFolder(editingFolder); Close(); }));
            }
            if (Widgets.ButtonText(new Rect(rect.x + rect.width - 130f, rect.yMax - 30f, 120f, 26f), "Close")) Close();
        }

        private List<RecipeDef> GetCachedAutomaticRecipes(Map map, string query)
        {
            List<string> ids = new List<string>(selectedBenches);
            ids.Sort(StringComparer.Ordinal);
            string benchKey = string.Join("|", ids.ToArray());
            if (cachedAutoRecipes != null && cachedAutoMap == map &&
                cachedAutoQuery == query && cachedAutoBenchKey == benchKey)
                return cachedAutoRecipes;

            cachedAutoMap = map;
            cachedAutoQuery = query;
            cachedAutoBenchKey = benchKey;
            cachedAutoRecipes = new List<RecipeDef>();
            List<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef candidate = all[i];
                if (candidate == null || (!string.IsNullOrEmpty(query) &&
                    candidate.LabelCap.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0))
                    continue;
                List<Building> compatible = RecipeBenchFinder.Find(map, candidate);
                bool match = false;
                for (int j = 0; j < compatible.Count; j++)
                    if (compatible[j].def != null && selectedBenches.Contains(compatible[j].def.defName))
                    { match = true; break; }
                if (match) cachedAutoRecipes.Add(candidate);
            }
            return cachedAutoRecipes;
        }

        private void OpenRecipePicker(string targetFolder)
        {
            string name = (targetFolder ?? string.Empty).Trim();
            if (name.Length == 0) return;
            Find.WindowStack.Add(new Dialog_BlocksMenuRecipePicker(component, name, recipeSearch));
        }

        private static List<ThingDef> GetBuiltBenchDefs(Map map)
        {
            List<ThingDef> result = new List<ThingDef>();
            if (map == null) return result;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building table = buildings[i] as Building;
                if (table == null || !(table is IBillGiver) || BlocksMenuBenchUtility.GetBillStack(table) == null ||
                    table.Destroyed || !table.Spawned || table.def == null || result.Contains(table.def)) continue;
                result.Add(table.def);
            }
            result.Sort(delegate(ThingDef a, ThingDef b) { return string.Compare(a.LabelCap.ToString(), b.LabelCap.ToString(), StringComparison.CurrentCultureIgnoreCase); });
            return result;
        }
    }

    // Собственная очередь Blocks Menu.
    public class GameComponent_BlocksMenuQueue : GameComponent
    {
        public List<BlocksMenuProductionOrder> Orders = new List<BlocksMenuProductionOrder>();
        public List<string> CustomFolders = new List<string>();
        public List<string> FolderRecipeDefs = new List<string>();
        public List<string> FolderRecipeNames = new List<string>();
        public List<string> FolderBenchFolderNames = new List<string>();
        public List<string> FolderBenchDefNames = new List<string>();
        public List<string> FolderManualNames = new List<string>();
        public List<string> FolderManualRecipeDefs = new List<string>();
        public List<string> FolderExcludedNames = new List<string>();
        public List<string> FolderExcludedRecipeDefs = new List<string>();
        public List<string> HiddenRecipeDefs = new List<string>(); // legacy global data; retained for save compatibility
        public List<string> HiddenFolderNames = new List<string>();
        public List<string> HiddenFolderRecipeDefs = new List<string>();
        private int lastRefreshTick = -1;
        private readonly Dictionary<ThingDef, int> stockCountCache = new Dictionary<ThingDef, int>();
        private Map stockCacheMap;
        private int stockCacheTick = -1;
        private bool stockCacheBuilt;
        private readonly Dictionary<string, bool> folderMembershipCache = new Dictionary<string, bool>();
        private Map folderMembershipMap;
        private int folderMembershipTick = -1;

        private void InvalidateFolderMembershipCache()
        {
            folderMembershipCache.Clear();
            folderMembershipMap = null;
            folderMembershipTick = -1;
        }

        public void CreateFolder(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || CustomFolders.Contains(name)) return;
            CustomFolders.Add(name);
            InvalidateFolderMembershipCache();
        }

        public void RenameFolder(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName) return;
            int index = CustomFolders.IndexOf(oldName);
            if (index < 0) return;
            // If the new name was created while adding a recipe in the editor,
            // merge the old folder into it instead of silently refusing the rename.
            if (CustomFolders.Contains(newName))
                CustomFolders.RemoveAt(index);
            else
                CustomFolders[index] = newName;
            InvalidateFolderMembershipCache();
            RenameFolderPairs(FolderBenchFolderNames, oldName, newName);
            RenameFolderPairs(FolderManualNames, oldName, newName);
            RenameFolderPairs(FolderExcludedNames, oldName, newName);
            RenameFolderPairs(HiddenFolderNames, oldName, newName);
            for (int i = 0; i < FolderRecipeNames.Count; i++)
                if (FolderRecipeNames[i] == oldName) FolderRecipeNames[i] = newName;
        }

        public void DeleteFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            CustomFolders.Remove(folder);
            InvalidateFolderMembershipCache();
            RemoveFolderPairs(FolderBenchFolderNames, FolderBenchDefNames, folder);
            RemoveFolderPairs(FolderManualNames, FolderManualRecipeDefs, folder);
            RemoveFolderPairs(FolderExcludedNames, FolderExcludedRecipeDefs, folder);
            RemoveFolderPairs(HiddenFolderNames, HiddenFolderRecipeDefs, folder);
            for (int i = FolderRecipeNames.Count - 1; i >= 0; i--)
                if (FolderRecipeNames[i] == folder)
                {
                    FolderRecipeNames.RemoveAt(i);
                    if (i < FolderRecipeDefs.Count) FolderRecipeDefs.RemoveAt(i);
                }
        }

        private static void RemoveFolderPairs(List<string> names, List<string> values, string folder)
        {
            for (int i = names.Count - 1; i >= 0; i--)
                if (names[i] == folder)
                {
                    names.RemoveAt(i);
                    if (i < values.Count) values.RemoveAt(i);
                }
        }

        public bool IsRecipeHidden(RecipeDef recipe)
        {
            return false; // legacy global hiding is intentionally disabled; visibility is folder-scoped.
        }

        public bool IsRecipeHidden(RecipeDef recipe, string folder)
        {
            if (recipe == null || string.IsNullOrEmpty(folder)) return false;
            for (int i = 0; i < HiddenFolderNames.Count && i < HiddenFolderRecipeDefs.Count; i++)
                if (HiddenFolderNames[i] == folder && HiddenFolderRecipeDefs[i] == recipe.defName) return true;
            return false;
        }

        public void SetRecipeHidden(RecipeDef recipe, bool hidden)
        {
            // Compatibility overload: deliberately does nothing to avoid global hiding.
        }

        public void SetRecipeHidden(RecipeDef recipe, string folder, bool hidden)
        {
            if (recipe == null || string.IsNullOrEmpty(folder) || !CustomFolders.Contains(folder)) return;
            if (hidden)
            {
                for (int i = 0; i < HiddenFolderNames.Count && i < HiddenFolderRecipeDefs.Count; i++)
                    if (HiddenFolderNames[i] == folder && HiddenFolderRecipeDefs[i] == recipe.defName) return;
                HiddenFolderNames.Add(folder);
                HiddenFolderRecipeDefs.Add(recipe.defName);
            }
            else
            {
                for (int i = HiddenFolderNames.Count - 1; i >= 0; i--)
                    if (HiddenFolderNames[i] == folder && HiddenFolderRecipeDefs[i] == recipe.defName)
                    { HiddenFolderNames.RemoveAt(i); HiddenFolderRecipeDefs.RemoveAt(i); }
            }
        }

        public bool RecipeBelongsToAnyFolder(Map map, RecipeDef recipe)
        {
            if (recipe == null) return false;
            if (FolderRecipeDefs.Contains(recipe.defName)) return true;

            // "All" hides recipes assigned to a folder even while its selected bench
            // is temporarily dismantled. The folder itself still shows the recipe only
            // when a selected compatible bench is currently built (RecipeBelongsToFolder).
            for (int f = 0; f < CustomFolders.Count; f++)
            {
                string folder = CustomFolders[f];
                bool excluded = false;
                for (int e = 0; e < FolderExcludedNames.Count && e < FolderExcludedRecipeDefs.Count; e++)
                    if (FolderExcludedNames[e] == folder && FolderExcludedRecipeDefs[e] == recipe.defName) { excluded = true; break; }
                if (excluded) continue;

                for (int m = 0; m < FolderManualNames.Count && m < FolderManualRecipeDefs.Count; m++)
                    if (FolderManualNames[m] == folder && FolderManualRecipeDefs[m] == recipe.defName) return true;

                List<string> selected = GetFolderBenchDefs(folder);
                for (int b = 0; b < selected.Count; b++)
                {
                    ThingDef benchDef = DefDatabase<ThingDef>.GetNamedSilentFail(selected[b]);
                    if (benchDef != null && benchDef.AllRecipes != null && benchDef.AllRecipes.Contains(recipe))
                        return true;
                }
            }
            return false;
        }

        private static void RenameFolderPairs(List<string> names, string oldName, string newName)
        { for (int i = 0; i < names.Count; i++) if (names[i] == oldName) names[i] = newName; }

        public List<string> GetFolderBenchDefs(string folder)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < FolderBenchFolderNames.Count && i < FolderBenchDefNames.Count; i++)
                if (FolderBenchFolderNames[i] == folder) result.Add(FolderBenchDefNames[i]);
            return result;
        }

        public void SetFolderBenchDefs(string folder, IEnumerable<string> defs)
        {
            for (int i = FolderBenchFolderNames.Count - 1; i >= 0; i--) if (FolderBenchFolderNames[i] == folder) { FolderBenchFolderNames.RemoveAt(i); FolderBenchDefNames.RemoveAt(i); }
            if (defs == null) { InvalidateFolderMembershipCache(); return; }
            foreach (string def in defs) if (!string.IsNullOrEmpty(def)) { FolderBenchFolderNames.Add(folder); FolderBenchDefNames.Add(def); }
            InvalidateFolderMembershipCache();
        }

        public void AddManualRecipeToFolder(string folder, RecipeDef recipe)
        {
            if (string.IsNullOrEmpty(folder) || recipe == null) return;
            IncludeRecipeInFolder(folder, recipe);
            for (int i = 0; i < FolderManualNames.Count && i < FolderManualRecipeDefs.Count; i++)
                if (FolderManualNames[i] == folder && FolderManualRecipeDefs[i] == recipe.defName) return;
            FolderManualNames.Add(folder); FolderManualRecipeDefs.Add(recipe.defName);
            InvalidateFolderMembershipCache();
        }

        public List<RecipeDef> GetFolderManualRecipes(string folder)
        {
            List<RecipeDef> result = new List<RecipeDef>();
            for (int i = 0; i < FolderManualNames.Count && i < FolderManualRecipeDefs.Count; i++)
                if (FolderManualNames[i] == folder) { RecipeDef def = DefDatabase<RecipeDef>.GetNamedSilentFail(FolderManualRecipeDefs[i]); if (def != null) result.Add(def); }
            return result;
        }

        public void RemoveManualRecipeFromFolder(string folder, RecipeDef recipe)
        {
            if (recipe == null) return;
            for (int i = FolderManualNames.Count - 1; i >= 0; i--)
                if (FolderManualNames[i] == folder && FolderManualRecipeDefs[i] == recipe.defName) { FolderManualNames.RemoveAt(i); FolderManualRecipeDefs.RemoveAt(i); }
            InvalidateFolderMembershipCache();
        }

        public bool RecipeBelongsToFolder(Map map, RecipeDef recipe, string folder)
        {
            if (recipe == null || string.IsNullOrEmpty(folder)) return false;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (folderMembershipMap != map || folderMembershipTick < 0 || tick - folderMembershipTick >= 300)
            {
                folderMembershipCache.Clear();
                folderMembershipMap = map;
                folderMembershipTick = tick;
            }
            string cacheKey = folder + "|" + recipe.defName;
            bool cached;
            if (folderMembershipCache.TryGetValue(cacheKey, out cached)) return cached;

            bool belongs = false;
            for (int i = 0; i < FolderExcludedNames.Count && i < FolderExcludedRecipeDefs.Count; i++)
                if (FolderExcludedNames[i] == folder && FolderExcludedRecipeDefs[i] == recipe.defName)
                {
                    folderMembershipCache[cacheKey] = false;
                    return false;
                }
            int legacyIndex = FolderRecipeDefs.IndexOf(recipe.defName);
            if (legacyIndex >= 0 && legacyIndex < FolderRecipeNames.Count && FolderRecipeNames[legacyIndex] == folder)
            {
                folderMembershipCache[cacheKey] = true;
                return true;
            }
            for (int i = 0; i < FolderManualNames.Count && i < FolderManualRecipeDefs.Count; i++)
                if (FolderManualNames[i] == folder && FolderManualRecipeDefs[i] == recipe.defName)
                {
                    folderMembershipCache[cacheKey] = true;
                    return true;
                }

            List<string> selected = GetFolderBenchDefs(folder);
            if (selected.Count > 0 && map != null)
            {
                List<Building> benches = RecipeBenchFinder.Find(map, recipe);
                for (int i = 0; i < benches.Count; i++)
                    if (benches[i].def != null && selected.Contains(benches[i].def.defName)) { belongs = true; break; }
            }
            folderMembershipCache[cacheKey] = belongs;
            return belongs;
        }

        public void ExcludeRecipeFromFolder(string folder, RecipeDef recipe)
        {
            if (string.IsNullOrEmpty(folder) || recipe == null) return;
            for (int i = 0; i < FolderExcludedNames.Count && i < FolderExcludedRecipeDefs.Count; i++)
                if (FolderExcludedNames[i] == folder && FolderExcludedRecipeDefs[i] == recipe.defName) return;
            FolderExcludedNames.Add(folder); FolderExcludedRecipeDefs.Add(recipe.defName);
            RemoveManualRecipeFromFolder(folder, recipe);
            InvalidateFolderMembershipCache();
        }

        public void IncludeRecipeInFolder(string folder, RecipeDef recipe)
        {
            if (string.IsNullOrEmpty(folder) || recipe == null) return;
            for (int i = FolderExcludedNames.Count - 1; i >= 0; i--)
                if (FolderExcludedNames[i] == folder && FolderExcludedRecipeDefs[i] == recipe.defName) { FolderExcludedNames.RemoveAt(i); FolderExcludedRecipeDefs.RemoveAt(i); }
            InvalidateFolderMembershipCache();
        }

        public void AssignRecipeToFolder(RecipeDef recipe, string folder)
        {
            if (recipe == null || string.IsNullOrEmpty(folder)) return;
            int index = FolderRecipeDefs.IndexOf(recipe.defName);
            if (index < 0)
            {
                FolderRecipeDefs.Add(recipe.defName);
                FolderRecipeNames.Add(folder);
            }
            else if (index < FolderRecipeNames.Count) FolderRecipeNames[index] = folder;
            InvalidateFolderMembershipCache();
        }

        public string GetRecipeFolder(RecipeDef recipe)
        {
            if (recipe == null) return string.Empty;
            int index = FolderRecipeDefs.IndexOf(recipe.defName);
            return index >= 0 && index < FolderRecipeNames.Count ? FolderRecipeNames[index] : string.Empty;
        }

        public GameComponent_BlocksMenuQueue(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref Orders, "blocksMenuProductionOrders", LookMode.Deep);
            Scribe_Collections.Look(ref CustomFolders, "blocksMenuCustomFolders", LookMode.Value);
            Scribe_Collections.Look(ref FolderRecipeDefs, "blocksMenuFolderRecipeDefs", LookMode.Value);
            Scribe_Collections.Look(ref FolderRecipeNames, "blocksMenuFolderRecipeNames", LookMode.Value);
            Scribe_Collections.Look(ref FolderBenchFolderNames, "blocksMenuFolderBenchFolderNames", LookMode.Value);
            Scribe_Collections.Look(ref FolderBenchDefNames, "blocksMenuFolderBenchDefNames", LookMode.Value);
            Scribe_Collections.Look(ref FolderManualNames, "blocksMenuFolderManualNames", LookMode.Value);
            Scribe_Collections.Look(ref FolderManualRecipeDefs, "blocksMenuFolderManualRecipeDefs", LookMode.Value);
            Scribe_Collections.Look(ref FolderExcludedNames, "blocksMenuFolderExcludedNames", LookMode.Value);
            Scribe_Collections.Look(ref FolderExcludedRecipeDefs, "blocksMenuFolderExcludedRecipeDefs", LookMode.Value);
            Scribe_Collections.Look(ref HiddenRecipeDefs, "blocksMenuHiddenRecipeDefs", LookMode.Value);
            Scribe_Collections.Look(ref HiddenFolderNames, "blocksMenuHiddenFolderNames", LookMode.Value);
            Scribe_Collections.Look(ref HiddenFolderRecipeDefs, "blocksMenuHiddenFolderRecipeDefs", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Orders == null) Orders = new List<BlocksMenuProductionOrder>();
                if (CustomFolders == null) CustomFolders = new List<string>();
                if (FolderRecipeDefs == null) FolderRecipeDefs = new List<string>();
                if (FolderRecipeNames == null) FolderRecipeNames = new List<string>();
                if (FolderBenchFolderNames == null) FolderBenchFolderNames = new List<string>();
                if (FolderBenchDefNames == null) FolderBenchDefNames = new List<string>();
                if (FolderManualNames == null) FolderManualNames = new List<string>();
                if (FolderManualRecipeDefs == null) FolderManualRecipeDefs = new List<string>();
                if (FolderExcludedNames == null) FolderExcludedNames = new List<string>();
                if (FolderExcludedRecipeDefs == null) FolderExcludedRecipeDefs = new List<string>();
                if (HiddenRecipeDefs == null) HiddenRecipeDefs = new List<string>();
                if (HiddenFolderNames == null) HiddenFolderNames = new List<string>();
                if (HiddenFolderRecipeDefs == null) HiddenFolderRecipeDefs = new List<string>();
            }
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            // Poll native Bills every 300 ticks (5 in-game seconds), not every second.
            // This work is independent of GUI repaint and avoids repeated full scans.
            if (Find.TickManager == null || Find.TickManager.TicksGame % 300 != 0)
                return;
            Map map = Find.CurrentMap;
            if (map == null || lastRefreshTick == Find.TickManager.TicksGame)
                return;
            lastRefreshTick = Find.TickManager.TicksGame;
            ImportManualBills(map);
            for (int i = Orders.Count - 1; i >= 0; i--)
            {
                BlocksMenuProductionOrder order = Orders[i];
                if (order == null)
                {
                    Orders.RemoveAt(i);
                    continue;
                }
                if (order.IsImported)
                {
                    // Если игрок удалил Bill прямо на верстаке, убираем только
                    // его ссылку из общей очереди. Остальные Bills не затрагиваются.
                    if (order.SourceBench == null || order.SourceBench.Destroyed ||
                        BlocksMenuBenchUtility.GetBillStack(order.SourceBench) == null || order.SourceBill == null ||
                        !BlocksMenuBenchUtility.GetBillStack(order.SourceBench).Bills.Contains(order.SourceBill))
                    {
                        Orders.RemoveAt(i);
                        continue;
                    }
                    RefreshImportedOrder(order);
                }
            }
        }

        public void ImportManualBills(Map map)
        {
            if (map == null)
                return;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int b = 0; b < buildings.Count; b++)
            {
                Building bench = buildings[b] as Building;
                if (bench == null || !(bench is IBillGiver) || BlocksMenuBenchUtility.GetBillStack(bench) == null || BlocksMenuBenchUtility.GetBillStack(bench).Bills == null)
                    continue;
                List<Bill> bills = BlocksMenuBenchUtility.GetBillStack(bench).Bills;
                for (int i = 0; i < bills.Count; i++)
                {
                    Bill_Production production = bills[i] as Bill_Production;
                    if (production == null || production.recipe == null || IsManagedBill(production) || IsAlreadyImported(production))
                        continue;
                    BlocksMenuProductionOrder imported = new BlocksMenuProductionOrder
                    {
                        Recipe = production.recipe,
                        Count = Math.Max(1, production.repeatCount),
                        Mode = GetModeFromNativeBill(production),
                        Suspended = production.suspended,
                        SourceBill = production,
                        SourceBench = bench,
                        IsImported = true
                    };
                    Orders.Add(imported);
                }
            }
        }

        private static ProductionOrderMode GetModeFromNativeBill(Bill_Production bill)
        {
            if (bill == null || bill.repeatMode == null)
                return ProductionOrderMode.MakeCount;
            string mode = bill.repeatMode.defName;
            if (mode == "TargetCount" || mode == "Target") return ProductionOrderMode.MakeUntilTarget;
            if (mode == "Forever") return ProductionOrderMode.Forever;
            return ProductionOrderMode.MakeCount;
        }

        private static void RefreshImportedOrder(BlocksMenuProductionOrder order)
        {
            if (order == null || order.SourceBill == null) return;
            // Не копируем Bill вручную: храним ссылку на оригинальный объект.
            // Так автоматически сохраняются IngredientFilter, ограничения качества,
            // материалы, режим повтора и любые дополнительные настройки модов.
            order.Recipe = order.SourceBill.recipe;
            order.Mode = GetModeFromNativeBill(order.SourceBill);
            order.Count = Math.Max(1, order.SourceBill.repeatCount);
            order.TargetCount = Math.Max(1, ReadBillInt(order.SourceBill, "targetCount", order.TargetCount));
            order.Suspended = order.SourceBill.suspended;
        }

        private static int ReadBillInt(Bill_Production bill, string memberName, int fallback)
        {
            if (bill == null) return fallback;
            try
            {
                Type type = bill.GetType();
                System.Reflection.FieldInfo field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(int)) return (int)field.GetValue(bill);
                System.Reflection.PropertyInfo property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(int) && property.CanRead) return (int)property.GetValue(bill, null);
            }
            catch (Exception ex) { Log.WarningOnce("[Blocks Menu] Failed to read " + memberName + ": " + ex.Message, 728342); }
            return fallback;
        }

        private static void WriteBillInt(Bill_Production bill, string memberName, int value)
        {
            if (bill == null) return;
            try
            {
                Type type = bill.GetType();
                System.Reflection.FieldInfo field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (field != null && field.FieldType == typeof(int)) { field.SetValue(bill, value); return; }
                System.Reflection.PropertyInfo property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (property != null && property.PropertyType == typeof(int) && property.CanWrite) property.SetValue(bill, value, null);
            }
            catch (Exception ex) { Log.WarningOnce("[Blocks Menu] Failed to write " + memberName + ": " + ex.Message, 728343); }
        }

        private static void SetNativeBillMode(Bill_Production bill, ProductionOrderMode mode)
        {
            if (bill == null) return;
            string defName = mode == ProductionOrderMode.MakeUntilTarget ? "TargetCount" :
                mode == ProductionOrderMode.Forever ? "Forever" : "RepeatCount";
            BillRepeatModeDef nativeMode = DefDatabase<BillRepeatModeDef>.GetNamedSilentFail(defName);
            if (nativeMode != null) bill.repeatMode = nativeMode;
        }

        private bool IsManagedBill(Bill bill)
        {
            for (int i = 0; i < Orders.Count; i++)
            {
                List<BlocksMenuManagedBill> managed = Orders[i].ManagedBills;
                if (managed == null) continue;
                for (int j = 0; j < managed.Count; j++)
                    if (managed[j] != null && managed[j].Bill == bill) return true;
            }
            return false;
        }

        private bool IsAlreadyImported(Bill bill)
        {
            for (int i = 0; i < Orders.Count; i++)
                if (Orders[i] != null && Orders[i].IsImported && Orders[i].SourceBill == bill)
                    return true;
            return false;
        }

        private static bool HasActiveManagedBills(BlocksMenuProductionOrder order)
        {
            if (order == null || order.ManagedBills == null || order.ManagedBills.Count == 0)
                return false;
            for (int i = 0; i < order.ManagedBills.Count; i++)
            {
                BlocksMenuManagedBill managed = order.ManagedBills[i];
                if (managed != null && managed.Bench != null && !managed.Bench.Destroyed &&
                    managed.Bill != null && !managed.Bill.suspended && BlocksMenuBenchUtility.GetBillStack(managed.Bench) != null &&
                    BlocksMenuBenchUtility.GetBillStack(managed.Bench).Bills.Contains(managed.Bill))
                    return true;
            }
            return false;
        }

        public void SynchronizeOrder(BlocksMenuProductionOrder order)
        {
            if (order == null || order.Recipe == null || Current.Game == null)
                return;
            if (order.IsImported)
            {
                if (order.SourceBill != null && order.SourceBench != null && !order.SourceBench.Destroyed)
                {
                    // Не трогаем нативные фильтры, ограничения и настройки Bill.
                    SetNativeBillMode(order.SourceBill, order.Mode);
                    if (order.Mode == ProductionOrderMode.MakeCount)
                        order.SourceBill.repeatCount = Math.Max(1, order.Count);
                    else if (order.Mode == ProductionOrderMode.MakeUntilTarget)
                        WriteBillInt(order.SourceBill, "targetCount", Math.Max(1, order.TargetCount));
                    order.SourceBill.suspended = order.Suspended;
                }
                return;
            }

            Map map = Find.CurrentMap;
            if (map == null)
                return;

            if (order.Suspended)
            {
                SetManagedBillsSuspended(order, true);
                return;
            }

            List<Building> benches = new List<Building>();
            if (order.TargetBenches != null && order.TargetBenches.Count > 0)
            {
                for (int i = 0; i < order.TargetBenches.Count; i++)
                {
                    Building targetBench = order.TargetBenches[i];
                    if (targetBench != null && !targetBench.Destroyed && targetBench.Spawned &&
                        targetBench.Map == map && BlocksMenuBenchUtility.GetBillStack(targetBench) != null && !benches.Contains(targetBench))
                        benches.Add(targetBench);
                }
            }
            else
            {
                benches = RecipeBenchFinder.Find(map, order.Recipe);
            }
            if (benches.Count == 0)
                return;

            // TargetCount is a native RimWorld Bill mode. Do not convert it into a
            // calculated RepeatCount: native Bill logic accounts for product yield
            // (including recipes such as boards) and keeps the target live over time.
            bool nativeTargetMode = order.Mode == ProductionOrderMode.MakeUntilTarget;
            bool nativeForeverMode = order.Mode == ProductionOrderMode.Forever;
            int craftsToSchedule = Math.Max(1, order.Count);

            if (order.ManagedBills == null)
                order.ManagedBills = new List<BlocksMenuManagedBill>();

            HashSet<Building> usedBenches = new HashSet<Building>();
            int baseCount = craftsToSchedule / benches.Count;
            int remainder = craftsToSchedule % benches.Count;

            for (int i = 0; i < benches.Count; i++)
            {
                Building bench = benches[i];
                int amount = baseCount + (i < remainder ? 1 : 0);
                if (!nativeTargetMode && !nativeForeverMode && amount <= 0)
                    continue;

                BlocksMenuManagedBill managed = null;
                for (int j = 0; j < order.ManagedBills.Count; j++)
                {
                    BlocksMenuManagedBill candidate = order.ManagedBills[j];
                    if (candidate != null && candidate.Bench == bench && candidate.Bill != null &&
                        BlocksMenuBenchUtility.GetBillStack(bench) != null && BlocksMenuBenchUtility.GetBillStack(bench).Bills.Contains(candidate.Bill))
                    {
                        managed = candidate;
                        break;
                    }
                }

                if (managed == null)
                {
                    Bill bill = order.Recipe.MakeNewBill(null);
                    Bill_Production productionBill = bill as Bill_Production;
                    if (productionBill == null)
                        continue;
                    SetNativeBillMode(productionBill, order.Mode);
                    if (order.Mode == ProductionOrderMode.MakeCount)
                        productionBill.repeatCount = amount;
                    else if (order.Mode == ProductionOrderMode.MakeUntilTarget)
                        WriteBillInt(productionBill, "targetCount", Math.Max(1, order.TargetCount));
                    productionBill.suspended = false;
                    BlocksMenuBenchUtility.GetBillStack(bench).AddBill(bill);
                    managed = new BlocksMenuManagedBill { Bench = bench, Bill = bill };
                    order.ManagedBills.Add(managed);
                }
                else
                {
                    Bill_Production productionBill = managed.Bill as Bill_Production;
                    if (productionBill != null)
                    {
                        SetNativeBillMode(productionBill, order.Mode);
                        if (order.Mode == ProductionOrderMode.MakeCount)
                            productionBill.repeatCount = amount;
                        else if (order.Mode == ProductionOrderMode.MakeUntilTarget)
                            WriteBillInt(productionBill, "targetCount", Math.Max(1, order.TargetCount));
                        productionBill.suspended = false;
                    }
                }
                usedBenches.Add(bench);
            }

            // Удаляем только Bills, назначенные этим заказом, если их станок больше не подходит.
            for (int i = order.ManagedBills.Count - 1; i >= 0; i--)
            {
                BlocksMenuManagedBill managed = order.ManagedBills[i];
                if (managed == null || managed.Bench == null || !usedBenches.Contains(managed.Bench))
                {
                    if (managed != null && managed.Bench != null && managed.Bill != null && !managed.Bench.Destroyed && BlocksMenuBenchUtility.GetBillStack(managed.Bench) != null)
                    {
                        try { BlocksMenuBenchUtility.GetBillStack(managed.Bench).Delete(managed.Bill); }
                        catch (Exception ex) { Log.Warning("[Blocks Menu] Failed to remove obsolete order Bill: " + ex.Message); }
                    }
                    order.ManagedBills.RemoveAt(i);
                }
            }
        }

        private static void SetManagedBillsSuspended(BlocksMenuProductionOrder order, bool suspended)
        {
            if (order == null || order.ManagedBills == null)
                return;
            for (int i = 0; i < order.ManagedBills.Count; i++)
            {
                BlocksMenuManagedBill managed = order.ManagedBills[i];
                if (managed != null && managed.Bill != null && managed.Bench != null && !managed.Bench.Destroyed &&
                    BlocksMenuBenchUtility.GetBillStack(managed.Bench) != null && BlocksMenuBenchUtility.GetBillStack(managed.Bench).Bills.Contains(managed.Bill))
                    managed.Bill.suspended = suspended;
            }
        }

        public static ThingDef GetPrimaryProduct(RecipeDef recipe)
        {
            return recipe.products != null && recipe.products.Count > 0 && recipe.products[0] != null
                ? recipe.products[0].thingDef : null;
        }

        private static int GetPrimaryProductCount(RecipeDef recipe)
        {
            return recipe.products != null && recipe.products.Count > 0 && recipe.products[0] != null
                ? Math.Max(1, recipe.products[0].count) : 1;
        }

        public int CountInStockpiles(Map map, ThingDef product)
        {
            if (map == null || product == null || map.listerThings == null)
                return 0;

            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (stockCacheMap != map || stockCacheTick < 0 || tick - stockCacheTick >= 300)
            {
                stockCountCache.Clear();
                stockCacheMap = map;
                stockCacheTick = tick;
                stockCacheBuilt = false;
            }

            if (!stockCacheBuilt)
                RebuildStockCountCache(map);

            int count;
            return stockCountCache.TryGetValue(product, out count) ? count : 0;
        }

        // Build all product totals in one pass. Previously every distinct ingredient
        // ThingDef triggered a complete scan of map.listerThings.AllThings, multiplying
        // the work by the number of materials shown in the visible recipe rows.
        private void RebuildStockCountCache(Map map)
        {
            stockCacheBuilt = true;
            stockCountCache.Clear();

            HashSet<IntVec3> stockpileCells = new HashSet<IntVec3>();
            if (map.zoneManager != null)
            {
                List<Zone> zones = map.zoneManager.AllZones;
                for (int z = 0; z < zones.Count; z++)
                {
                    Zone_Stockpile stockpile = zones[z] as Zone_Stockpile;
                    if (stockpile == null) continue;
                    foreach (IntVec3 cell in stockpile.Cells)
                        stockpileCells.Add(cell);
                }
            }

            HashSet<Thing> counted = new HashSet<Thing>();
            List<Thing> allThings = map.listerThings.AllThings;
            for (int i = 0; i < allThings.Count; i++)
            {
                Thing thing = allThings[i];
                if (thing == null || thing.Destroyed || thing.def == null || !counted.Add(thing))
                    continue;

                bool inStockpileZone = thing.Spawned && thing.Position.IsValid && stockpileCells.Contains(thing.Position);
                if (inStockpileZone || IsStoredInStockpile(map, thing) || IsOnShelfOrStorage(thing))
                    AddStockCount(thing.def, thing.stackCount);
            }

            // Include only free colonists' inventories, matching the previous behaviour.
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int p = 0; p < colonists.Count; p++)
            {
                Pawn pawn = colonists[p];
                if (pawn == null || pawn.inventory == null || pawn.inventory.innerContainer == null)
                    continue;
                ThingOwner<Thing> inventory = pawn.inventory.innerContainer;
                for (int t = 0; t < inventory.Count; t++)
                {
                    Thing thing = inventory[t];
                    if (thing != null && !thing.Destroyed && thing.def != null && counted.Add(thing))
                        AddStockCount(thing.def, thing.stackCount);
                }
            }
        }

        private void AddStockCount(ThingDef def, int amount)
        {
            if (def == null || amount <= 0)
                return;
            int current;
            stockCountCache.TryGetValue(def, out current);
            stockCountCache[def] = current + amount;
        }

        private static bool IsOnShelfOrStorage(Thing thing)
        {
            if (thing == null) return false;
            // Предметы в ThingOwner (включая контейнеры/полки) имеют holder.
            try { return thing.ParentHolder != null; }
            catch { return false; }
        }

        private static System.Reflection.MethodInfo isInAnyStorageMethod;
        private static bool storageMethodResolved;

        private static bool IsStoredInStockpile(Map map, Thing thing)
        {
            if (thing == null) return false;

            // Используем штатный метод игры, если он доступен в данной сборке.
            // Reflection здесь нужен для совместимости с разными сборками HSK.
            if (!storageMethodResolved)
            {
                storageMethodResolved = true;
                isInAnyStorageMethod = typeof(Thing).GetMethod(
                    "IsInAnyStorage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
            }
            if (isInAnyStorageMethod != null)
            {
                try
                {
                    object result = isInAnyStorageMethod.Invoke(thing, null);
                    if (result is bool && (bool)result)
                        return true;
                }
                catch (Exception ex)
                {
                    Log.WarningOnce("[Blocks Menu] Failed to check native item storage: " + ex.Message, 728341);
                }
            }

            // Stockpile-zone cells were checked once by RebuildStockCountCache.
            return false;
        }

        public void SetOrderPaused(BlocksMenuProductionOrder order, bool paused)
        {
            if (order == null) return;
            order.Suspended = paused;
            if (order.IsImported)
            {
                if (order.SourceBill != null) order.SourceBill.suspended = paused;
            }
            else if (paused) SetManagedBillsSuspended(order, true);
            else SynchronizeOrder(order);
        }

        public void RemoveOrder(BlocksMenuProductionOrder order)
        {
            if (order == null) return;
            if (order.IsImported && order.SourceBench != null && order.SourceBill != null && !order.SourceBench.Destroyed)
                BlocksMenuBenchUtility.GetBillStack(order.SourceBench).Delete(order.SourceBill);
            RemoveManagedBills(order);
            Orders.Remove(order);
        }

        private void RemoveManagedBills(BlocksMenuProductionOrder order)
        {
            if (order.ManagedBills == null)
                order.ManagedBills = new List<BlocksMenuManagedBill>();
            for (int i = order.ManagedBills.Count - 1; i >= 0; i--)
            {
                BlocksMenuManagedBill managed = order.ManagedBills[i];
                if (managed != null && managed.Bench != null && managed.Bill != null)
                {
                    try { BlocksMenuBenchUtility.GetBillStack(managed.Bench).Delete(managed.Bill); }
                    catch (Exception ex) { Log.Warning("[Blocks Menu] Failed to remove custom Bill: " + ex.Message); }
                }
            }
            order.ManagedBills.Clear();
        }
    }

    public class BlocksMenuProductionOrder : IExposable
    {
        public RecipeDef Recipe;
        public int Count = 1;
        public ProductionOrderMode Mode = ProductionOrderMode.MakeCount;
        public int TargetCount = 200;
        public int ResumeAtCount = 50;
        public bool Suspended;
        public int CurrentStock;
        public bool IsImported;
        public Bill_Production SourceBill;
        public Building SourceBench;
        public List<BlocksMenuManagedBill> ManagedBills = new List<BlocksMenuManagedBill>();
        public List<Building> TargetBenches = new List<Building>();
        public string CustomLabel = string.Empty;
        public bool IsFavorite;
        public bool IsRenaming;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref Recipe, "recipe");
            Scribe_Values.Look(ref Count, "count", 1);
            Scribe_Values.Look(ref Mode, "mode", ProductionOrderMode.MakeCount);
            Scribe_Values.Look(ref TargetCount, "targetCount", 200);
            Scribe_Values.Look(ref ResumeAtCount, "resumeAtCount", 50);
            Scribe_Values.Look(ref Suspended, "suspended", false);
            Scribe_Values.Look(ref CurrentStock, "currentStock", 0);
            Scribe_Values.Look(ref IsImported, "isImported", false);
            Scribe_References.Look(ref SourceBill, "sourceBill");
            Scribe_References.Look(ref SourceBench, "sourceBench");
            Scribe_Collections.Look(ref ManagedBills, "managedBills", LookMode.Deep);
            Scribe_Collections.Look(ref TargetBenches, "targetBenches", LookMode.Reference);
            Scribe_Values.Look(ref CustomLabel, "customLabel", string.Empty);
            Scribe_Values.Look(ref IsFavorite, "isFavorite", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Count < 1)
                    Count = 1;
                if (ManagedBills == null)
                    ManagedBills = new List<BlocksMenuManagedBill>();
                if (TargetBenches == null)
                    TargetBenches = new List<Building>();
            }
        }
    }

    public class BlocksMenuManagedBill : IExposable
    {
        public Building Bench;
        public Bill Bill;

        public void ExposeData()
        {
            Scribe_References.Look(ref Bench, "bench");
            Scribe_References.Look(ref Bill, "bill");
        }
    }

}
