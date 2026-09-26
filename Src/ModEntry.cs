using System;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using MV.Common;
using MV.WorldObject;

[assembly: MelonInfo(typeof(KogamaOfflinePatch.ModEntry), "KogamaOfflinePatch", "1.0", "MarshZE")]
[assembly: MelonGame("Multiverse ApS", "KoGaMa")]

namespace KogamaOfflinePatch
{
    public class ModEntry : MelonMod
    {
        private bool _photonForced = false;
        private bool _mapLoaded = false;
        private float _spawnTimer = 0f;
        private float _logicStepTimer = 0f;



        public override void OnInitializeMelon()
        
        {
            MelonLogger.Msg("Kogama Offline Patch (Mono) Initialized.");
            HarmonyInstance.PatchAll();
            BypassMVGameControllerInit.Initialize();  
            MelonLogger.Msg("Patches applied successfully.");
        }

        public override void OnUpdate()
        {
            Time.timeScale = 1f;

            if (MVGameControllerBase.Game != null && MVGameControllerBase.JoinState == MVJoinState.Playing)
            {
                _logicStepTimer += Time.deltaTime;
                if (_logicStepTimer >= 1f)
                {
                    _logicStepTimer = 0f;
                    try
                    {
                        var wrapperField = AccessTools.Field(typeof(MVNetworkGame), "logicObjectManagerClientWrapper");
                        if (wrapperField is object)
                        {
                            var wrapper = wrapperField.GetValue(MVGameControllerBase.Game);
                            if (wrapper is object)
                            {
                                var stepMethod = wrapper.GetType().GetMethod("Step");
                                if (stepMethod is object)
                                {
                                    stepMethod.Invoke(wrapper, null);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            BypassMVGameControllerInit.DriveJoinStateForward();

            if (!_photonForced && MVGameControllerBase.Game != null && MVGameControllerBase.JoinState == MVJoinState.Joining)
            {
                _photonForced = true;
                BypassMVGameControllerInit.ForcePhotonConnect();
            }

            if (BypassMVGameControllerInit.NeedsToLoadGui)
            {
                BypassMVGameControllerInit.NeedsToLoadGui = false;
                MelonLogger.Msg("[ModEntry] Safely invoking LoadModeGui() from main thread...");
                var loadGuiMethod = AccessTools.Method(typeof(MVNetworkGame), "LoadModeGui");
                if (loadGuiMethod is object && MVGameControllerBase.Game != null)
                {
                    loadGuiMethod.Invoke(MVGameControllerBase.Game, null);
                }
            }

            if (!_mapLoaded && MVGameControllerBase.JoinState == MVJoinState.Playing && MVGameControllerBase.Game != null)
            {
                MelonLogger.Msg("[ModEntry] Reached Playing state. Forcing map load...");
                _mapLoaded = true;
                BypassMVGameControllerInit.ForceLoadKgmMapFromDisk();
                BypassMVGameControllerInit.InitializeSettingsAndSkybox();
                FixEnvironmentAndShaders();
            }

            // WEAPON HOTBAR INPUT
            if (!object.ReferenceEquals(BypassMVGameControllerInit.CurrentAvatar, null) && MVGameControllerBase.JoinState == MVJoinState.Playing)
            {
                try
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1))
                    {
                        var equipField = AccessTools.Field(typeof(MVAvatarLocal), "avatarEquipable");
                        if (equipField is object)
                        {
                            var equipable = equipField.GetValue(BypassMVGameControllerInit.CurrentAvatar) as AvatarEquipable;
                            if (equipable is object)
                            {
                                equipable.Unequip();
                                MelonLogger.Msg("[Hotbar] Equipped Hand");
                            }
                        }
                    }
                    else if (Input.GetKeyDown(KeyCode.Alpha2))
                    {
                        var equipField = AccessTools.Field(typeof(MVAvatarLocal), "avatarEquipable");
                        if (equipField is object)
                        {
                            var equipable = equipField.GetValue(BypassMVGameControllerInit.CurrentAvatar) as AvatarEquipable;
                            if (equipable is object)
                            {
                                equipable.Equip(AvatarItemType.SlapGun, AvatarEquipableType.Weapon, null);
                                MelonLogger.Msg("[Hotbar] Equipped SlapGun");
                            }
                        }
                    }
                }
                catch { }

                // MODIFIER HOTBAR INPUT
                try
                {
                    var avatar = BypassMVGameControllerInit.CurrentAvatar as MVAvatarLocal;
                    if (avatar is object)
                    {
                        var interactable = avatar.InteractableLocal;

                        if (Input.GetKeyDown(KeyCode.F))
                        {
                            interactable.AddModifier(AvatarModifierPackageType.Fire);
                            MelonLogger.Msg("[Modifiers] Applied Fire");
                        }
                        else if (Input.GetKeyDown(KeyCode.G))
                        {
                            interactable.AddModifier(AvatarModifierPackageType.Poison);
                            MelonLogger.Msg("[Modifiers] Applied Poison");
                        }
                        else if (Input.GetKeyDown(KeyCode.H))
                        {
                            interactable.AddModifier(AvatarModifierPackageType.NinjaRun);
                            MelonLogger.Msg("[Modifiers] Applied NinjaRun");
                        }
                        else if (Input.GetKeyDown(KeyCode.J))
                        {
                            interactable.AddModifier(AvatarModifierPackageType.Shrunken);
                            MelonLogger.Msg("[Modifiers] Applied Shrunken");
                        }
                        else if (Input.GetKeyDown(KeyCode.K))
                        {
                            interactable.AddModifier(AvatarModifierPackageType.Enlarged);
                            MelonLogger.Msg("[Modifiers] Applied Enlarged");
                        }
                        else if (Input.GetKeyDown(KeyCode.L))
                        {
                            interactable.AddModifier(AvatarModifierPackageType.Frozen);
                            MelonLogger.Msg("[Modifiers] Applied Frozen");
                        }
                        else if (Input.GetKeyDown(KeyCode.M))
                        {
                            interactable.ClearModifiers();
                            MelonLogger.Msg("[Modifiers] Cleared All Modifiers");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Error($"[Modifiers] Failed to apply: {ex.Message}");
                }
            }

            // AUTOMATICALLY SPAWN AVATAR AFTER 3 SECONDS
            if (_mapLoaded && object.ReferenceEquals(BypassMVGameControllerInit.CurrentAvatar, null) && !object.ReferenceEquals(BypassMVGameControllerInit.CachedSpawnerInstance, null))
            {
                _spawnTimer += Time.deltaTime;
                if (_spawnTimer > 3f)
                {
                    MelonLogger.Msg("[ModEntry] Automatically spawning avatar...");
                    BypassMVGameControllerInit.TryCloneAndActivateAvatar();
                    _spawnTimer = 0f;
                }
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            MelonLogger.Msg($"[ModEntry] Scene loaded: {sceneName}");

            if (sceneName == "DesktopBase")
            {
                var prefabPool = UnityEngine.Object.FindObjectOfType<PrefabPool>();
                if (prefabPool != null)
                {
                    UnityEngine.Object.DontDestroyOnLoad(prefabPool.gameObject);
                    MelonLogger.Msg("[Bypass] Marked PrefabPool as DontDestroyOnLoad.");
                }
                
                var gameController = UnityEngine.Object.FindObjectOfType<MVGameControllerDesktop>();
                if (gameController != null)
                {
                    UnityEngine.Object.DontDestroyOnLoad(gameController.gameObject);
                }
            }

            if (sceneName == "DesktopPlayModeGUI")
            {
                if (_mapLoaded) return;
                MelonLogger.Msg("[ModEntry] Play mode scene loaded. Forcing map load...");
                _mapLoaded = true;
                BypassMVGameControllerInit.ForceLoadKgmMapFromDisk();
                BypassMVGameControllerInit.InitializeSettingsAndSkybox();
                FixEnvironmentAndShaders();
                MelonCoroutines.Start(EnableRenderersDelayed());

                var localPlayer = MVGameControllerBase.LocalPlayer;
                if (localPlayer != null)
                {
                    var isReadyProp = localPlayer.GetType().GetProperty("IsReady", BindingFlags.Public | BindingFlags.Instance);
                    if (isReadyProp is object && isReadyProp.CanWrite)
                    {
                        isReadyProp.SetValue(localPlayer, true, null);
                    }
                    
                    if (MVGameControllerBase.PlayModeUI != null)
                    {
                        var setUIReadyMethod = MVGameControllerBase.PlayModeUI.GetType().GetMethod("SetUIReady");
                        if (setUIReadyMethod is object)
                        {
                            setUIReadyMethod.Invoke(MVGameControllerBase.PlayModeUI, null);
                            MelonLogger.Msg("[ModEntry] Manually called SetUIReady() to unlock UI.");
                        }
                    }
                }
            }
        }

        public override void OnLateUpdate()
        {
            if (RenderSettings.fog)
            {
                RenderSettings.fog = false;
            }

            if (!object.ReferenceEquals(BypassMVGameControllerInit.CurrentAvatar, null) && MVGameControllerBase.MainCameraManager != null)
            {
                if (MVGameControllerBase.MainCameraManager.IsCameraControllerSet())
                {
                    try
                    {
                        BypassMVGameControllerInit.IsUpdatingCamera = true;
                        MVGameControllerBase.MainCameraManager.UpdateCamera();
                    }
                    catch { }
                    finally
                    {
                        BypassMVGameControllerInit.IsUpdatingCamera = false;
                    }
                }
            }
        }

        private System.Collections.IEnumerator EnableRenderersDelayed()
        {
            yield return new WaitForSeconds(1f);

            try
            {
                RenderSettings.fog = false;
            }
            catch { }

            for (int pass = 0; pass < 5; pass++)
            {
                try
                {
                    var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                    int enabled = 0;
                    foreach (var r in renderers)
                    {
                        if (r != null && !r.enabled) { r.enabled = true; enabled++; }
                    }
                    MelonLogger.Msg($"[ModEntry] Pass {pass + 1}: Enabled {enabled} renderers.");
                }
                catch { }
                yield return new WaitForSeconds(2f);
            }
        }

        public static void FixEnvironmentAndShaders()
        {
            try
            {
                var standardShader = Shader.Find("Standard");
                var unlitShader = Shader.Find("Unlit/Texture");
                if (standardShader == null && unlitShader == null) return;

                var renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
                int fixedCount = 0;

                foreach (var r in renderers)
                {
                    if (r == null || r.sharedMaterial == null) continue;
                    if (r.sharedMaterial.shader == null || r.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
                    {
                        if (standardShader != null) r.sharedMaterial.shader = standardShader;
                        else if (unlitShader != null) r.sharedMaterial.shader = unlitShader;
                        fixedCount++;
                    }
                }
                MelonLogger.Msg($"[Environment] Fixed {fixedCount} missing shaders.");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"[Environment] FixEnvironmentAndShaders failed: {ex.Message}");
            }
        }

    }
}