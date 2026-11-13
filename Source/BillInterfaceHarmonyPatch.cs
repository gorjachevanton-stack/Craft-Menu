using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BlocksMenu
{
    [StaticConstructorOnStartup]
    internal static class BillInterfaceHarmonyPatch
    {
        // True only while Blocks Menu renders its own active-order list.
        // Native workstation bill lists are deliberately left untouched.
        internal static bool IsDrawingBlocksMenuQueue;

        static BillInterfaceHarmonyPatch()
        {
            try
            {
                new Harmony("BlocksMenu.NativeBillInterface").PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[Blocks Menu] Native Bill interface patch loaded.");
            }
            catch (Exception ex)
            {
                Log.Error("[Blocks Menu] Failed to patch native Bill interface: " + ex);
            }
        }
    }

    // The native Bill draws reorder arrows from inside DoInterface. Suppress only
    // those two button textures while Blocks Menu is rendering its own queue.
    [HarmonyPatch]
    internal static class Bill_ReorderButtons_BlocksMenuPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(Widgets).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "ButtonImage");
        }

        private static bool Prefix(object[] __args)
        {
            if (!BillInterfaceHarmonyPatch.IsDrawingBlocksMenuQueue || __args == null)
                return true;

            for (int i = 0; i < __args.Length; i++)
            {
                if (ReferenceEquals(__args[i], TexButton.ReorderUp) ||
                    ReferenceEquals(__args[i], TexButton.ReorderDown))
                    return false;
            }
            return true;
        }
    }

    // Safety net for any reorder action triggered while the custom queue is drawn.
    // This does not affect BillStacks in ordinary workbench windows.
    [HarmonyPatch(typeof(BillStack), "Reorder")]
    internal static class BillStack_Reorder_BlocksMenuPatch
    {
        private static bool Prefix()
        {
            return !BillInterfaceHarmonyPatch.IsDrawingBlocksMenuQueue;
        }
    }

    [HarmonyPatch(typeof(Bill), "DoInterface")]
    internal static class Bill_DoInterface_BlocksMenuPatch
    {
        private static void Postfix(Bill __instance, float x, float y, float width, int index)
        {
            if (!BillInterfaceHarmonyPatch.IsDrawingBlocksMenuQueue ||
                __instance == null || __instance.recipe == null || __instance.DeletedOrDereferenced)
                return;

            Color oldColor = GUI.color;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            try
            {
                // The native Bill draws its own title. Never draw it a second time.
                // Use only the left icon strip, leaving the native title at x + 28 intact.
                Rect iconRect = new Rect(x + 1f, y + 1f, 24f, 24f);
                ThingDef product = (__instance.recipe.products != null && __instance.recipe.products.Count > 0)
                    ? __instance.recipe.products[0].thingDef : null;
                if (product != null && product.uiIcon != null)
                {
                    GUI.color = Color.white;
                    GUI.DrawTexture(iconRect, product.uiIcon, ScaleMode.ScaleToFit, true);
                }

                // Draw the navigation button only in the 64 px column reserved by
                // DrawProductionQueue. The native Bill row is intentionally narrower
                // in Blocks Menu, so its title and right-side controls cannot overlap it.
                float jumpWidth = 60f;
                float jumpX = x + width + 4f;
                Rect jumpRect = new Rect(jumpX, y, jumpWidth, 24f);

                GUI.color = oldColor;
                if (Widgets.ButtonText(jumpRect, "Go to workbench", true, true, true))
                {
                    JumpToOwningBench(__instance);
                }
                TooltipHandler.TipRegion(jumpRect, "Go to the workbench assigned to this order");
            }
            finally
            {
                GUI.color = oldColor;
                Text.Font = oldFont;
                Text.Anchor = oldAnchor;
            }
        }

        private static void JumpToOwningBench(Bill bill)
        {
            BillStack stack = bill != null ? bill.billStack : null;
            Building bench = stack != null ? stack.billGiver as Building : null;
            if (bench == null || bench.Destroyed || !bench.Spawned)
                return;

            if (Find.CameraDriver != null)
                Find.CameraDriver.JumpToCurrentMapLoc(bench.Position);

            try
            {
                object selector = Find.Selector;
                if (selector == null) return;
                MethodInfo[] methods = selector.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != "Select") continue;
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    if (parameters.Length == 0 || !parameters[0].ParameterType.IsAssignableFrom(bench.GetType())) continue;
                    object[] args = new object[parameters.Length];
                    args[0] = bench;
                    for (int p = 1; p < parameters.Length; p++)
                        args[p] = parameters[p].HasDefaultValue ? parameters[p].DefaultValue :
                            (parameters[p].ParameterType.IsValueType ? Activator.CreateInstance(parameters[p].ParameterType) : null);
                    methods[i].Invoke(selector, args);
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Blocks Menu] Could not select workstation after camera jump: " + ex.Message);
            }
        }
    }
}
