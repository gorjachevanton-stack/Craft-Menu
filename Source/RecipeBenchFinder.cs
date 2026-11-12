using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace BlocksMenu
{
    /// <summary>
    /// Finds every spawned colony building that exposes RimWorld's native BillStack.
    /// This intentionally uses IBillGiver rather than Building_WorkTable so modded
    /// production buildings (for example electric arc furnaces) are discoverable.
    /// </summary>
    public static class RecipeBenchFinder
    {
        // One map scan builds the index for every recipe. The previous implementation
        // scanned all colony buildings separately for each RecipeDef, causing a large
        // burst when the catalogue refreshed.
        private static readonly Dictionary<RecipeDef, List<Building>> Cache =
            new Dictionary<RecipeDef, List<Building>>();
        private static readonly HashSet<ThingDef> ProductsWithBenchRecipe = new HashSet<ThingDef>();
        private static readonly List<Building> Empty = new List<Building>();
        private static Map cachedMap;
        private static int cacheTick = -1;
        private static bool cacheBuilt;

        public static List<Building> Find(Map map, RecipeDef recipe)
        {
            if (map == null || recipe == null)
                return Empty;

            EnsureIndex(map);
            List<Building> result;
            return Cache.TryGetValue(recipe, out result) ? result : Empty;
        }

        /// <summary>
        /// True when at least one completed, spawned colony workstation on this map
        /// has the recipe and can produce the specified ThingDef. Power, fuel,
        /// flick state and current bill/job availability are intentionally ignored.
        /// </summary>
        public static bool CanProduceOnMap(Map map, ThingDef product)
        {
            if (map == null || product == null)
                return false;
            EnsureIndex(map);
            return ProductsWithBenchRecipe.Contains(product);
        }

        private static void EnsureIndex(Map map)
        {
            int tick = Verse.Find.TickManager == null ? 0 : Verse.Find.TickManager.TicksGame;
            if (cachedMap != map || cacheTick < 0 || tick - cacheTick >= 300)
            {
                Cache.Clear();
                ProductsWithBenchRecipe.Clear();
                cachedMap = map;
                cacheTick = tick;
                cacheBuilt = false;
            }
            if (!cacheBuilt)
                BuildIndex(map);
        }

        private static void BuildIndex(Map map)
        {
            cacheBuilt = true;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (building == null || !(building is IBillGiver) || building.Destroyed || !building.Spawned ||
                    building.def == null || building.def.AllRecipes == null)
                    continue;

                BillStack stack = BlocksMenuBenchUtility.GetBillStack(building);
                if (stack == null)
                    continue;

                List<RecipeDef> recipes = building.def.AllRecipes;
                for (int r = 0; r < recipes.Count; r++)
                {
                    RecipeDef recipe = recipes[r];
                    if (recipe == null)
                        continue;

                    List<Building> benches;
                    if (!Cache.TryGetValue(recipe, out benches))
                    {
                        benches = new List<Building>();
                        Cache.Add(recipe, benches);
                    }
                    benches.Add(building);

                    // Index products once alongside recipes. All five catalogues
                    // can then filter items without rescanning every RecipeDef.
                    if (recipe.products != null)
                    {
                        for (int p = 0; p < recipe.products.Count; p++)
                        {
                            ThingDef product = recipe.products[p] == null ? null : recipe.products[p].thingDef;
                            if (product != null)
                                ProductsWithBenchRecipe.Add(product);
                        }
                    }
                }
            }
        }
    }

    internal static class BlocksMenuBenchUtility
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static BillStack GetBillStack(Building building)
        {
            if (building == null || !(building is IBillGiver))
                return null;

            // RimWorld's IBillGiver interface does not expose BillStack.
            // The stack is stored by the concrete building implementation, so
            // resolve the conventional field/property on that implementation.
            for (System.Type type = building.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField("billStack", InstanceFlags | BindingFlags.DeclaredOnly);
                if (field != null && typeof(BillStack).IsAssignableFrom(field.FieldType))
                    return field.GetValue(building) as BillStack;

                PropertyInfo property = type.GetProperty("BillStack", InstanceFlags | BindingFlags.DeclaredOnly) ??
                                        type.GetProperty("billStack", InstanceFlags | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0 &&
                    typeof(BillStack).IsAssignableFrom(property.PropertyType))
                {
                    MethodInfo getter = property.GetGetMethod(true);
                    if (getter != null)
                        return getter.Invoke(building, null) as BillStack;
                }
            }
            return null;
        }
    }
}
