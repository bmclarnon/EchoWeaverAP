// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) 2026 The EchoWeaverAP Authors

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace EchoWeaverAP.Patches;

/// <summary>
/// Harmony patches to enable randomizing Collectables.
/// </summary>
public class CollectablePatcher
{
    internal class ItemDescriptionOverride : MonoBehaviour
    {
        public string Description { get; set; }
    }

    /// <summary>
    /// Replaces collectable sprite and pickup reward.
    /// </summary>
    [HarmonyPatch(typeof(ShakeAbsorbable))]
    [HarmonyPatch("Start")]
    public static class ShakeAbsorbable_Start_Patch
    {
        private static readonly List<(Collectable c, long locationId)> s_deferredCollectables = [];

        static void Prefix(ShakeAbsorbable __instance)
        {
            if (__instance is Collectable c &&
                TryGetSaveId(c, out string saveId) &&
                s_locationIdsBySaveId.TryGetValue(saveId, out long locationId))
            {
                if (!Plugin.Randomizer.Initialized)
                {
                    // Echo Weaver loads some sprites immediately, before the Archipelago connection can be established.
                    // These collectables should be queued until the randomizer has finished initializing.
                    s_deferredCollectables.Add((c, locationId));
                }
                else if (Plugin.Randomizer.TryPeekLocation(locationId, out long itemId, out Randomizer.ItemImportance importance, out string desc) &&
                        s_keyItemsByItemId.TryGetValue(itemId, out KeyItem keyItem))
                {
                    UpdateCollectable(c, keyItem, importance, desc);
                }
            }
        }

        internal static void OnRandomizerInitialized()
        {
            foreach (var (c, locationId) in s_deferredCollectables)
            {
                if (Plugin.Randomizer.TryPeekLocation(locationId, out long itemId, out Randomizer.ItemImportance importance, out string desc) &&
                    s_keyItemsByItemId.TryGetValue(itemId, out KeyItem keyItem))
                {
                    UpdateCollectable(c, keyItem, importance, desc);
                }
            }
            s_deferredCollectables.Clear();
        }

        private static readonly AccessTools.FieldRef<Collectable, KeyItem> s_keyItemToPickupRef =
            AccessTools.FieldRefAccess<Collectable, KeyItem>("keyItemToPickup");

        private static void UpdateCollectable(Collectable collectable, KeyItem keyItem, Randomizer.ItemImportance importance, string desc)
        {
            KeyItem prefab = (keyItem == KeyItem.BoneShard || keyItem == KeyItem.HealStrength || keyItem == KeyItem.DriedTear) ? keyItem : KeyItem.CalcifiedEnth;
            var visuals = ReplaceSprites(collectable, prefab);
            if (prefab != keyItem)
            {
                // Archipelago items have different sprites based on importance.
                Sprite sprite = null;
                if (keyItem == KeyItem.IntermindSeparation)
                {
                    switch (importance)
                    {
                        case Randomizer.ItemImportance.Progression:
                        case Randomizer.ItemImportance.Useful:
                        case Randomizer.ItemImportance.Trap:
                            sprite = LoadSprite("EchoWeaverAP.Resources.ArchipelagoProgression.png", 176, 176);
                            break;

                        case Randomizer.ItemImportance.Filler:
                            sprite = LoadSprite("EchoWeaverAP.Resources.ArchipelagoFiller.png", 176, 176);
                            break;
                    }
                }
                else
                {
                    sprite = EchoWeaverAssetManager.GetKeyItemSprite(keyItem);
                }

                if (sprite != null)
                {
                    visuals[0].sprite = sprite;
                    visuals[0].transform.localScale = visuals[0].transform.localScale * (2.35f / (sprite.rect.height / sprite.pixelsPerUnit));
                }
                else
                {
                    Plugin.Logger.LogWarning($"No KeyItem sprite for {keyItem}");
                }
            }

            s_keyItemToPickupRef(collectable) = keyItem;

            if (desc != null)
            {
                collectable.gameObject.AddComponent<ItemDescriptionOverride>().Description = desc;
            }

            // Replacing the sprite can undo the visual effects for already-collected pickups.
            if (SimpleSaveable.CompletedAcrossLoops(collectable.gameObject))
            {
                collectable.CompletedAcrossLoops(true);
            }
        }

        private static GameObject[] s_keyItemPrefabs;

        private static readonly AccessTools.FieldRef<Collectable, SpriteRenderer[]> s_visualsRef =
                AccessTools.FieldRefAccess<Collectable, SpriteRenderer[]>("visuals");

        private static SpriteRenderer[] ReplaceSprites(Collectable collectable, KeyItem keyItem)
        {
            SpriteRenderer[] visuals = s_visualsRef(collectable);
            if (collectable.Type == keyItem)
            {
                return visuals;
            }

            foreach (SpriteRenderer sr in visuals)
            {
                sr.gameObject.SetActive(false);
            }

            s_keyItemPrefabs ??= Traverse.Create(EchoWeaverAssetManager.Instance).Field("upgradePrefabs").GetValue<GameObject[]>();
            foreach (GameObject prefab in s_keyItemPrefabs)
            {
                Collectable c = prefab.GetComponent<Collectable>();
                if (c != null && c.Type == keyItem)
                {
                    Transform parent = visuals[0].transform.parent;
                    SpriteRenderer[] prefabVisuals = s_visualsRef(c);
                    visuals = new SpriteRenderer[prefabVisuals.Length];
                    for (int i = 0; i < prefabVisuals.Length; ++i)
                    {
                        GameObject clone = UnityEngine.Object.Instantiate(prefabVisuals[i].gameObject, parent);
                        visuals[i] = clone.GetComponent<SpriteRenderer>();
                    }
                    return s_visualsRef(collectable) = visuals;
                }
            }
            Plugin.Logger.LogError($"Failed to find prefab for KeyItem {keyItem}");
            return visuals;
        }
    }

    /// <summary>
    /// Informs the Randomizer when a Collectable has been obtained.
    /// </summary>
    [HarmonyPatch(typeof(Collectable))]
    [HarmonyPatch("onAbsorbComplete")]
    public static class Collectable_onAbsorbComplete_Patch
    {
        static void Postfix(Collectable __instance)
        {
            if (TryGetSaveId(__instance, out string saveId) &&
                s_locationIdsBySaveId.TryGetValue(saveId, out long locationId))
            {
                Plugin.Randomizer.MarkLocationChecked(locationId);
            }
        }
    }

    /// <summary>
    /// Updates the local game for remotely checked locations.
    /// </summary>
    // Patching Awake ensures callbacks are registered after initialization is complete.
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch("Awake")]
    public static class GameManager_Awake_Patch
    {
        private delegate void SetStateToPickedUpDelegate(Collectable c);
        private static readonly SetStateToPickedUpDelegate s_setStateToPickedUp =
            AccessTools.MethodDelegate<SetStateToPickedUpDelegate>(AccessTools.Method(typeof(Collectable), "setStateToPickedUp"));

        static void Postfix()
        {
            Plugin.Randomizer.OnLocationCheckedRemotely += OnLocationCheckedRemotely;
            Plugin.Randomizer.OnInitialized += ShakeAbsorbable_Start_Patch.OnRandomizerInitialized;
        }

        // Updates local tracking to indicate the location was checked.
        private static void OnLocationCheckedRemotely(long locationId)
        {
            if (s_locationSaveIdsByLocationId.TryGetValue(locationId, out string saveId))
            {
                PlayerData.Instance?.VariableSaveData.SetBool(saveId, true);
                foreach (SaveID sid in UnityEngine.Object.FindObjectsByType<SaveID>(FindObjectsSortMode.None))
                {
                    if (sid.ID == saveId && sid.gameObject.TryGetComponent(out Collectable c))
                    {
                        s_setStateToPickedUp(c);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Adds localized strings for newly used KeyItems.
    /// </summary>
    [HarmonyPatch(typeof(GameCopy))]
    [HarmonyPatch("loadCopyForLanguage")]
    public static class GameCopy_loadCopyForLanguage_Patch
    {
        static void Postfix(Dictionary<string, (string copy, string speaker, string agent)> _mapping)
        {
            _mapping["UI_DOUBLEJUMP_NAME"] = ("Anti-Grav Boost", string.Empty, string.Empty);
            _mapping["UI_DOUBLEJUMP_POPUPDESC"] = ("Enables a second jump.", string.Empty, string.Empty);
            _mapping["UI_REACH_NAME"] = ("Mag Suit", string.Empty, string.Empty);
            _mapping["UI_REACH_POPUPDESC"] = ("Enables jumping off of walls.", string.Empty, string.Empty);
            _mapping["UI_INPLACERECALL_NAME"] = ("Enth Cloak", string.Empty, string.Empty);
            _mapping["UI_INPLACERECALL_POPUPDESC"] = ("Enables gliding after healing.", string.Empty, string.Empty);
            _mapping["UI_INTERMINDSEPARATION_NAME"] = ("Otherworldly Item", string.Empty, string.Empty);
            _mapping["UI_INTERMINDSEPARATION_POPUPDESC"] = ("An unknown Archipelago item.", string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// Stores the current Collectable's ItemDescriptionOverride when showing a popup.
    /// </summary>
    [HarmonyPatch(typeof(Collectable))]
    [HarmonyPatch(nameof(Collectable.OpenPopupOverlay), [typeof(Collectable), typeof(bool)])]
    public static class Collectable_OpenPopupOverlay1_Patch
    {
        internal static ItemDescriptionOverride CurrentOverride = null;

        static void Prefix(Collectable _collectable)
        {
            if (_collectable.gameObject.TryGetComponent(out ItemDescriptionOverride ido))
            {
                CurrentOverride = ido;
            }
        }

        static void Postfix()
        {
            CurrentOverride = null;
        }
    }

    /// <summary>
    /// Uses the current Collectable's ItemDescriptionOverride (if any) in popups.
    /// </summary>
    [HarmonyPatch(typeof(Collectable))]
    [HarmonyPatch(nameof(Collectable.OpenPopupOverlay), [typeof(KeyItem), typeof(bool)])]
    public static class Collectable_OpenPopupOverlay2_Patch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(ItemInfo), nameof(ItemInfo.PopupDescription))))
                .ThrowIfInvalid("Failed to find keyItemInfo.PopupDescription")
                .Advance(1)
                .SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Collectable_OpenPopupOverlay2_Patch), nameof(GetPopupDescription))));
            return codeMatcher.Instructions();
        }

        private static string GetPopupDescription(ItemInfo itemInfo)
        {
            return Collectable_OpenPopupOverlay1_Patch.CurrentOverride?.Description ?? itemInfo.PopupDescription;
        }
    }


    /// <summary>
    /// Adds item receipt overlays for new key items.
    /// </summary>
    [HarmonyPatch(typeof(InventoryItemMetadata))]
    [HarmonyPatch("GetKeyItem")]
    public static class InventoryItemMetadata_GetKeyItem_Patch
    {
        private static readonly KeyItemInfo[] s_keyItemOverrides = [
            new KeyItemInfo(null, string.Empty, null, false, PopupType.Lead, KeyItem.Reach),
            new KeyItemInfo(null, string.Empty, null, false, PopupType.Lead, KeyItem.InPlaceRecall),
            new KeyItemInfo(null, string.Empty, null, false, PopupType.Lead, KeyItem.IntermindSeparation),
        ];

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            // Make a best-effort attempt to remove a warning log since the postfix supports additional key items.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldstr, "Metadata not defined for this key item: {0}!"),
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Ldfld),
                new CodeMatch(OpCodes.Box),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(string), nameof(string.Format), [typeof(string), typeof(object)])),
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogWarning), [typeof(object)])));
            if (codeMatcher.IsValid)
            {
                codeMatcher.RemoveInstructions(6);
            }
            return codeMatcher.Instructions();
        }

        static void Postfix(KeyItem _ki, ref KeyItemInfo __result)
        {
            __result ??= s_keyItemOverrides.SingleOrDefault<KeyItemInfo>(i => (KeyItem)i.ItemType == _ki);
        }
    }

    /// <summary>
    /// Sets sprites for new KeyItems.
    /// </summary>
    [HarmonyPatch(typeof(EchoWeaverAssetManager))]
    [HarmonyPatch("GetKeyItemSprite")]
    public static class EchoWeaverAssetManager_GetKeyItemSprite_Patch
    {
        static void Postfix(KeyItem _item, ref Sprite __result)
        {
            if (__result == null)
            {
                switch (_item)
                {
                    case KeyItem.DoubleJump:
                        __result = LoadSprite("EchoWeaverAP.Resources.DoubleJump.png", 156, 192);
                        break;

                    case KeyItem.Reach:
                        __result = LoadSprite("EchoWeaverAP.Resources.WallJump.png", 176, 176);
                        break;

                    case KeyItem.InPlaceRecall:
                        __result = LoadSprite("EchoWeaverAP.Resources.Glide.png", 176, 176);
                        break;

                    case KeyItem.IntermindSeparation:
                        __result = LoadSprite("EchoWeaverAP.Resources.Archipelago.png", 176, 176);
                        break;

                    default:
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Fixes spawning of the reward for mending the Prototype Node.
    /// </summary>
    [HarmonyPatch(typeof(EchoRift))]
    [HarmonyPatch("onMendingAnimsComplete")]
    public static class EchoRift_onMendingAnimsComplete_Patch
    {
        private static readonly AccessTools.FieldRef<EchoWeaverAssetManager, GameObject[]> s_upgradePrefabsRef =
            AccessTools.FieldRefAccess<EchoWeaverAssetManager, GameObject[]>("upgradePrefabs");
        private static readonly AccessTools.FieldRef<SaveID, string> s_fullIDRef =
            AccessTools.FieldRefAccess<SaveID, string>("fullID");

        private const string SaveId = "gotRewardForMendingPrototypeNodeRift";

        private static GameObject s_collectable = null;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(EchoWeaverAssetManager), nameof(EchoWeaverAssetManager.Instance))),
                new CodeMatch(OpCodes.Ldc_I4_S, (sbyte)KeyItem.CalcifiedEnth),
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(EchoWeaverAssetManager), nameof(EchoWeaverAssetManager.SpawnCollectable))))
                .ThrowIfInvalid("Failed to find EchoWeaverAssetManager.Instance.SpawnCollectable")
                .Advance(3)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EchoRift_onMendingAnimsComplete_Patch), nameof(SpawnCollectable))));
            // Instead of marking the item as collected once it's spawned, we'll wait until it's absorbed by using the standard SaveID codepath.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(PlayerData), nameof(PlayerData.Instance))),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerData), nameof(PlayerData.VariableSaveData))),
                new CodeMatch(OpCodes.Ldstr, SaveId),
                new CodeMatch(OpCodes.Ldc_I4_1),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(VariableSaveData), nameof(VariableSaveData.SetBool))))
                .ThrowIfInvalid("Failed to find PlayerData.Instance.VariableSaveData.SetBool")
                .RemoveInstructions(5);
            return codeMatcher.Instructions();
        }

        private static void SpawnCollectable(EchoWeaverAssetManager echoWeaverAssetManager, KeyItem keyItem, Vector3 position)
        {
            // The anchor node renders in front of the collectable.
            var anchorNode = GameObject.Find("/Systems Office(Clone)/Landmarks/Anchor Node/Anchor Node Main/anchor-node-broken");
            if (anchorNode?.TryGetComponent(out SpriteRenderer sr) == true)
            {
                sr.sortingOrder = 1;
            }

            // Only spawn the collectable once. When it's collected, the SaveID will be recorded and it will not spawn again.
            if (s_collectable == null)
            {
                foreach (GameObject gameObject in s_upgradePrefabsRef(echoWeaverAssetManager))
                {
                    if (gameObject.TryGetComponent(out Collectable c) && c.Type == keyItem)
                    {
                        s_collectable = UnityEngine.Object.Instantiate(gameObject, position + Vector3.up * 1.5f, Quaternion.identity);
                        s_fullIDRef(s_collectable.AddComponent<SaveID>()) = SaveId;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Fixes spawning of the reward for Nova's alt quest conclusion.
    /// </summary>
    [HarmonyPatch(typeof(GameManager))]
    [HarmonyPatch(nameof(GameManager.SpawnRewardFromNovaLakeDeadend))]
    public static class GameManager_SpawnRewardFromNovaLakeDeadend_Patch
    {
        private static readonly AccessTools.FieldRef<EchoWeaverAssetManager, GameObject[]> s_upgradePrefabsRef =
            AccessTools.FieldRefAccess<EchoWeaverAssetManager, GameObject[]>("upgradePrefabs");
        private static readonly AccessTools.FieldRef<SaveID, string> s_fullIDRef =
            AccessTools.FieldRefAccess<SaveID, string>("fullID");

        private const string SaveId = "receivedNovaLakeReward";

        private static GameObject s_collectable = null;

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldsfld, AccessTools.Field(typeof(EchoWeaverAssetManager), nameof(EchoWeaverAssetManager.Instance))),
                new CodeMatch(OpCodes.Ldc_I4_S, (sbyte)KeyItem.CalcifiedEnth),
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(EchoWeaverAssetManager), nameof(EchoWeaverAssetManager.SpawnCollectable))))
                .ThrowIfInvalid("Failed to find EchoWeaverAssetManager.Instance.SpawnCollectable")
                .Advance(3)
                .SetInstructionAndAdvance(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GameManager_SpawnRewardFromNovaLakeDeadend_Patch), nameof(SpawnCollectable))));
            // Instead of marking the item as collected once it's spawned, we'll wait until it's absorbed by using the standard SaveID codepath.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(PlayerData), nameof(PlayerData.Instance))),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(PlayerData), nameof(PlayerData.VariableSaveData))),
                new CodeMatch(OpCodes.Ldstr, SaveId),
                new CodeMatch(OpCodes.Ldc_I4_1),
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(VariableSaveData), nameof(VariableSaveData.SetBool))))
                .RemoveInstructions(5);
            return codeMatcher.Instructions();
        }

        private static void SpawnCollectable(EchoWeaverAssetManager echoWeaverAssetManager, KeyItem keyItem, Vector3 position)
        {
            // Only spawn the collectable once. When it's collected, the SaveID will be recorded and it will not spawn again.
            if (s_collectable == null)
            {
                foreach (GameObject gameObject in s_upgradePrefabsRef(echoWeaverAssetManager))
                {
                    if (gameObject.TryGetComponent(out Collectable c) && c.Type == keyItem)
                    {
                        s_collectable = UnityEngine.Object.Instantiate(gameObject, position, Quaternion.identity);
                        s_fullIDRef(s_collectable.AddComponent<SaveID>()) = SaveId;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Spawns plinth rewards even if the bronze time isn't beaten.
    /// </summary>
    [HarmonyPatch]
    public static class TimeTrialPlinth_waitForPlinthSlam_Patch
    {
        static MethodBase TargetMethod()
        {

            return AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(TimeTrialPlinth), "waitForPlinthSlam"));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codeMatcher = new CodeMatcher(instructions);
            // Always set newBest = true.
            codeMatcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(PlayerData), nameof(PlayerData.UpdateTimeTrialTime))),
                new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(TimeTrialPlinth), "newBest")))
                .ThrowIfInvalid("Failed to find this.newBest = PlayerData.UpdateTimeTrialTime(...)")
                .Advance(1)
                .InsertAndAdvance(
                    new CodeInstruction(OpCodes.Pop),
                    new CodeInstruction(OpCodes.Ldc_I4_1));
            // Don't require a bronze time to spawn the collectable.
            codeMatcher.MatchForward(false,
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(TimeTrialPlinth), "finishTime")),
                new CodeMatch(i => i.IsLdloc()),
                new CodeMatch(OpCodes.Call, AccessTools.PropertyGetter(typeof(TimeTrialPlinth), nameof(TimeTrialPlinth.BronzeTime))),
                new CodeMatch(i => i.opcode == OpCodes.Bge_Un || i.opcode == OpCodes.Bge_Un_S))
                .ThrowIfInvalid("Failed to find this.finishTime < this.BronzeTime")
                .RemoveInstructions(5);
            return codeMatcher.Instructions();
        }
    }

    private static bool TryGetSaveId(Collectable collectable, out string saveId)
    {
        Transform transform = collectable.transform;
        while (transform)
        {
            Plugin.Logger.LogDebug($"Attempting to get save id from {transform.name}");
            if (SimpleSaveable.TryGetSaveID(transform.gameObject, out saveId))
            {
                Plugin.Logger.LogDebug($"Found save id: {saveId}");
                return true;
            }

            transform = transform.parent;
        }

        saveId = default;
        return false;
    }

    private static readonly Dictionary<string, Sprite> s_spriteCache = [];

    private static Sprite LoadSprite(string resourceName, int width, int height)
    {
        if (s_spriteCache.TryGetValue(resourceName, out Sprite sprite))
        {
            return sprite;
        }

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Could not find embedded resource: {resourceName}");

        var buffer = new byte[stream.Length];
        stream.Read(buffer, 0, buffer.Length);
        var texture = new Texture2D(width, height, TextureFormat.DXT1, false);
        ImageConversion.LoadImage(texture, buffer);
        return s_spriteCache[resourceName] = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
    }

    private static readonly Dictionary<long, KeyItem> s_keyItemsByItemId = new()
    {
        // Placeholder for items from other worlds.
        {-1, KeyItem.IntermindSeparation},

        // Glaive abilities.
        {10, KeyItem.GlaiveRecall},
        {11, KeyItem.RecallImpulse},

        // Revelations.
        {20, KeyItem.GlaiveWarp},  // Revelation of Folding
        {21, KeyItem.LinkingRoom},  // Revelation of Nexus
        {22, KeyItem.WeaverOverclock},  // Revelation of Amplification
        {23, KeyItem.GlaiveInfusion},  // Revelation of Twin Sight
        {24, KeyItem.MendTime},  // Revelation of Unbinding

        // Minor abilities.
        {30, KeyItem.DoubleJump},
        {31, KeyItem.Reach},  // Wall Jump
        {32, KeyItem.Grapple},
        {33, KeyItem.InPlaceRecall},  // Glide
        {34, KeyItem.MazeLang},

        // Collectables.
        {40, KeyItem.BoneShard},
        {41, KeyItem.HealStrength},
        {42, KeyItem.DriedTear},
        {43, KeyItem.CalcifiedEnth},
    };
    public static IEnumerable<KeyItem> AllKeyItems => s_keyItemsByItemId.Values;

    private static readonly Dictionary<long, string> s_locationSaveIdsByLocationId = new()
    {
        // Calcified Enth locations
        {0x100, "/Reservoir (Grapple Tutorial)/Pickups/Calcified Enth Collectable/8c9d193b"},
        {0x101, "/Central Island/Pickups/Calcified Enth Collectable/1fa89c87"},
        {0x102, "/Lake Tower - West/Pickups/Time Trial Plinth/3807ee46"},
        {0x103, "/Above Center Island/Pickups/Calcified Enth Collectable/a1438f01"},
        {0x104, "receivedNovaLakeReward"},
        {0x105, "/Crumble Blocks Intro/Pickups/Calcified Enth Collectable/3a945459"},
        {0x106, "/Lake Tower - Central Interior/Pickups/Time Trial Plinth/d3c03d06"},
        {0x107, "/East Shore/Pickups/Calcified Enth Collectable/199e3a07"},
        {0x108, "/East Shore/Pickups/Calcified Enth Collectable/b6345d9c"},
        {0x109, "/Lake Tower - East Interior/Pickups/Time Trial Plinth - East Lake Tower/ab9e8d13"},
        {0x10a, "/East Shore/Pickups/Calcified Enth Collectable/96097cee"},
        {0x10b, "/Lake Approach/Pickups/Calcified Enth Collectable/acc0ad9d"},
        {0x10c, "/Lake Approach Top Run/Widgets/Cuttable Sarcophagus (1)/Crushbox/Calcified Enth Collectable/d8eb2be7"},
        {0x10d, "/Wash Plateau/Pickups/Calcified Enth Collectable/b4cfa536"},
        {0x10e, "/Outskirts East/Pickups/Time Trial Plinth/8933dfa7"},
        {0x10f, "/Emergence/Pickups/Calcified Enth Collectable/6ccaac66"},
        {0x110, "/Church Main Entrance/Pickups/Calcified Enth Collectable/604d722e"},
        {0x111, "/Deck 1 - Map Room/Pickups/Time Trial Plinth/5366304e"},
        {0x112, "/Deck 1 East/Pickups/Calcified Enth Collectable/5870b3e6"},
        {0x113, "/Lower Chapel (cult ambush)/Pickups/Calcified Enth Collectable/f037bca0"},
        {0x114, "/Marrow Access/Pickups/Time Trial Plinth/99a3d363"},
        {0x115, "/Marrow Access/Pickups/Calcified Enth Collectable/48e742d2"},
        {0x116, "/Deck 5 - Big Tomb Room/Pickups/Calcified Enth Collectable (2)/b8397542"},
        {0x117, "/Deck 5 - Tomb Loop/Pickups/Calcified Enth Collectable (1)/79bb89b8"},
        {0x118, "/Deck 5 - Tomb Loop/Pickups/Time Trial Plinth/c07fc917"},
        {0x119, "/Prison Block/Pickups/Time Trial Plinth/1adce940"},
        {0x11a, "/Old Start Secret Extension/Pickups/Time Trial Plinth/3b0e40e7"},
        {0x11b, "/Underlake T2 Path Upper/Pickups/Time Trial Plinth/91f54f02"},
        {0x11c, "/Underlake Access Elevator Exit/Collectables/Calcified Enth Collectable/2c06fd95"},
        {0x11d, "/Underground Access Interior/Pickups/Calcified Enth Collectable/363ff321"},
        {0x11e, "/Underlake New T2 Path Lower/Pickups/Calcified Enth Collectable/59e927d6"},
        {0x11f, "/Underlake Final Node Chamber/Pickups/Calcified Enth Collectable/81e82274"},
        {0x120, "/Underlake Final Node Chamber/Pickups/Calcified Enth Collectable (1)/65b65730"},
        {0x121, "/Underlake Final Node Chamber/Pickups/Calcified Enth Collectable (3)/44366a64"},
        {0x122, "/Underlake Final Node Chamber/Pickups/Calcified Enth Collectable (2)/31cd0fc8"},
        {0x123, "/Path to Underlake/Pickups/Calcified Enth Collectable/18b88ba9"},
        {0x124, "/Start West - Glaive Access/Pickups/Calcified Enth Collectable/ef7c2e17"},
        {0x125, "/Tutorial - Falling Loopback/Pickups/Calcified Enth Collectable/fd613bc4"},
        {0x126, "/Blue Platforming Section/Pickups/Time Trial Plinth/357ff133"},
        {0x127, "/Spillways Central Pit/Pickups/Calcified Enth Collectable/6dae92f6"},
        {0x128, "/Vault Entrance/Widgets/Time Trial Plinth/c0dd98bc"},
        {0x129, "/No-Glaive-To-Warp Path/Widgets/Time Trial Plinth/4bf2d4e4"},
        {0x12a, "/Skybridge/Pickups/Calcified Enth Collectable/25e4b9b5"},
        {0x12b, "/Approach Timed Branch/Pickups/Time Trial Plinth/d895fc29"},
        {0x12c, "/Approach Timed Branch/Pickups/Calcified Enth Collectable/df3ad935"},
        {0x12d, "/Sky To Central Isle/Pickups/Calcified Enth Collectable/1fc39dd1"},
        {0x12e, "/Tree Hopping Tut/Pickups/Calcified Enth Collectable/2297353e"},
        {0x12f, "/Tree Hopping Lower Area 3/Pickups/Time Trial Plinth/af2899b6"},
        {0x130, "/Tree Hopping Lower Area 3/Pickups/Calcified Enth Collectable/aecfbb27"},
        {0x131, "/Sky Tippy Top/Pickups/Time Trial Plinth/5b84bdbc"},
        {0x132, "gotRewardForMendingPrototypeNodeRift"},
        
        // Remnant of a Weaver locations
        {0x200, "/Reservoir (Grapple Tutorial)/Pickups/Health Upgrade Collectable/dacefffc"},
        {0x201, "/High Water Softgate/Pickups/Health Upgrade Collectable/f0a0e062"},
        {0x202, "/Lake Tower - East/Pickups/Health Upgrade Collectable/2286f9c6"},
        {0x203, "/Lake Approach Top Run/Pickups/Health Upgrade Collectable/38dd56ad"},
        {0x204, "/Recall Impulse Tutorial/Pickups/Health Upgrade Collectable (1)/8e9fc2bf"},
        {0x205, "/Emergence/Pickups/Health Upgrade Collectable/d628eb40"},
        {0x206, "/Church Main Entrance/Pickups/Health Upgrade Collectable/d3673dbd"},
        {0x207, "/Main Floor (Deck 2)/Pickups/Health Upgrade Collectable/100c3e42"},
        {0x208, "/Depths Anchor Node/Pickups/Health Upgrade Collectable/4084f2cd"},
        {0x209, "/Prison Block - Cell 1/Pickups/Health Upgrade Collectable/c9904ca1"},
        {0x20a, "/Underlake New T2 Path Lower/Pickups/Health Upgrade Collectable/5ec7c567"},
        {0x20b, "/Secret Supply Closet/Pickups/Health Upgrade Collectable/109d7d74"},
        {0x20c, "/Tutorial - NW Slide+Jump/Pickups/Health Upgrade Collectable/db8234c3"},
        {0x20d, "/Monitoring & Observation/Widgets/Health Upgrade Collectable/8263319a"},
        {0x20e, "/Glaive Route/Pickups/Health Upgrade Collectable (1)/b8bc3817"},
        {0x20f, "/No-Glaive-To-Warp Path/Pickups/Health Upgrade Collectable/5c00149b"},
        {0x210, "/Town TopRight Goodie/Pickups/Health Upgrade Collectable/417a6473"},
        {0x211, "/Dash Climb/Pickups/Health Upgrade Collectable/6ef4706d"},
        {0x212, "/Tree Hopping Lower Area/Pickups/Health Upgrade Collectable/9f80398f"},
        {0x213, "/Dash Climb 4/Pickups/Health Upgrade Collectable/c57c5057"},
        
        // Vestige of an Orator locations
        {0x300, "/Central Island/Pickups/Heal Strength Upgrade Collectable/493b2194"},
        {0x301, "/East Shore/Pickups/Heal Strength Upgrade Collectable/6b1d3808"},
        {0x302, "/Church Main Entrance/Pickups/Heal Strength Upgrade Collectable/72c568bc"},
        {0x303, "/Very Bottom - Deck 7/Pickups/Heal Strength Upgrade Collectable/9dabcf1c"},
        {0x304, "/Deck 5 - Tomb Loop/Pickups/Heal Strength Upgrade Collectable (1)/83e323fd"},
        {0x305, "/Prison Block - Cell 3/Pickups/Heal Strength Upgrade Collectable/87a9bc83"},
        {0x306, "/Underlake Access Elevator Exit/Collectables/Heal Strength Upgrade Collectable/27c068c5"},
        {0x307, "/Underlake New T2 Path Lower/Pickups/Heal Strength Upgrade Collectable/f8056e04"},
        {0x308, "/Start West - Glaive Access/Widgets/Storage Locker - final reward/visual/backing/content-spawn-pos/Heal Strength Upgrade Collectable/3e1606c4"},
        {0x309, "/Spillways Central Pit/Pickups/Heal Strength Upgrade Collectable/2d824c6d"},
        {0x30a, "/Tree Hopping Lower Area 2/Pickups/Heal Strength Upgrade Collectable/d42fc267"},
        {0x30b, "/Dash Climb 2/Pickups/Heal Strength Upgrade Collectable/667e7232"},

        // Dewdrop of Enessa locations
        {0x400, "/Town Main/Pickups/Time Upgrade Collectable/4ee64540"},
        {0x401, "/Town Main/Pickups/Time Upgrade Collectable (1)/e8782c4b"},
        {0x402, "/Marrow-to-Church Connection/Pickups/Time Upgrade Collectable/c9c8bf2c"},
        {0x403, "/Central Island/Pickups/Time Upgrade Collectable/ed9784dd"},
    };
    private static readonly Dictionary<string, long> s_locationIdsBySaveId = s_locationSaveIdsByLocationId.ToDictionary(e => e.Value, e => e.Key);
}
