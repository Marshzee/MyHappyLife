/*
Why the fuck do you wanna emulate a game when you can buy a Photon service ? :clueless:
Shoutout to every professional developer on this game. I do have a serious question though.
Why is it that you ARE STILL paying for networking your own game ? This is a dumb concept to be honest- If you made the game, you shall know all the aspects of it from A to Z. Instead of having someone to Reverse engineer your GAME and have to struggle with all shit at once, this was more of a personal fun project and I don't really care about making it playable at all, my drive was to learn more about networking and server emulating which I did and the difficulty is fucking beyond basic game development, I do not plan to stick to this shit for long, it costed me 3 months (2 months respectively wasted on the IL2CPP build that never worked in the first place due to Anti-Cheat Toolkits on the client (Which is still bypassable but I won't bother much) )

I'd also like to say many thanks to those who backed me up with lots of resources on information to at least progress this far, with the most of my gratitude returning to Zode (The greatest source of logic one coul have. Tee-Hee) 
*/

using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using ExitGames.Client.Photon;
using UnityEngine;
using MV.WorldObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MV.Common;
using Assets.Scripts.Network.Player.SpawnRoles.SpawnRoleData.Mediator;
using UnityEngine.Networking;
using System.Linq;
using System.Collections;

namespace KogamaOfflinePatch
{
    public static class BypassMVGameControllerInit
    {
        public static Vector3 CachedSpawnerPosition = Vector3.zero;
        public static int CachedSpawnerWoId = -1;
        public static MVWorldObjectClient CachedSpawnerInstance = null;
        public static object CachedWorldObjectsDict = null;
        public static object CachedPrototypesDict = null;
        public static bool NeedsToLoadGui = false;
        public static MVWorldObjectClient CurrentAvatar = null;
        private static bool _avatarSpawned = false;
        private static bool _synthesized = false;
        public static bool IsUpdatingCamera = false;
        public static bool IsUIOpen = false;

        public static void Initialize()
        {
            if (!_synthesized)
            {
                _synthesized = true;
                TrySynthesizeGameSessionData();
            }
        }

        public static void DriveJoinStateForward()
        {
            try
            {
                if (MVGameControllerBase.JoinState < MVJoinState.Joining)
                {
                    MVGameControllerBase.JoinState = MVJoinState.Joining;
                }
            }
            catch { }
        }

        public static void ForcePhotonConnect()
        {
            if (MVGameControllerBase.Game == null) return;

            try
            {
                MelonLogger.Msg("[Bypass] Faking Photon Connect status...");
                
                var listener = MVGameControllerBase.Game as IPhotonPeerListener;
                if (listener != null)
                {
                    listener.OnOperationResponse(new OperationResponse
                    {
                        OperationCode = 255,
                        ReturnCode = 0,
                        DebugMessage = "",
                        Parameters = new Dictionary<byte, object>()
                    });

                    listener.OnEvent(new EventData
                    {
                        Code = 63,
                        Parameters = new Dictionary<byte, object> { { 254, 1 } }
                    });
                }
                
                MelonLogger.Msg("[Bypass] Photon Connect sequence completed successfully!");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bypass] ForcePhotonConnect CRASHED: {ex}");
            }
        }

        public static void ForceLoadKgmMapFromDisk()
        {
            if (MVGameControllerBase.Game == null)
            {
                MelonLogger.Error("[Bypass] Game is null. Cannot load map.");
                return;
            }

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string mapPath = System.IO.Path.Combine(localAppData, "KogamaMaps");
                
                string kgmPath = System.IO.Path.Combine(mapPath, "1.kgm");
                string kgmapPath = System.IO.Path.Combine(mapPath, "1.kgmap");
                
                string targetPath = System.IO.File.Exists(kgmapPath) ? kgmapPath : (System.IO.File.Exists(kgmPath) ? kgmPath : null);

                if (targetPath == null)
                {
                    MelonLogger.Error($"[Bypass] Map file not found in {mapPath}. Looked for 1.kgm and 1.kgmap.");
                    return;
                }

                MelonLogger.Msg($"[Bypass] Reading map from disk: {targetPath}");
                
                byte[] mapData;

                if (targetPath.EndsWith(".kgmap"))
                {
                    byte[] decompressed;
                    using (var fs = System.IO.File.OpenRead(targetPath))
                    using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                    using (var ms = new System.IO.MemoryStream())
                    {
                        byte[] buffer = new byte[4096];
                        int read;
                        while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ms.Write(buffer, 0, read);
                        }
                        decompressed = ms.ToArray();
                    }

                    using var reader = new System.IO.BinaryReader(new System.IO.MemoryStream(decompressed), System.Text.Encoding.UTF8);
                    
                    uint magic = reader.ReadUInt32();
                    if (magic != 0x504D474B) // "KGMAP"
                    {
                        MelonLogger.Error("[Bypass] Invalid .kgmap file (Magic mismatch).");
                        return;
                    }
                    
                    ushort version = reader.ReadUInt16();
                    MelonLogger.Msg($"[Bypass] .kgmap version: {version}");

                    if (version >= 6)
                    {
                        int metaLen = reader.ReadInt32();
                        if (metaLen > 0) reader.ReadBytes(metaLen);
                    }

                    int batchCount = reader.ReadInt32();
                    MelonLogger.Msg($"[Bypass] Found {batchCount} batches in .kgmap.");
                    
                    var combinedBytes = new List<byte>();
                    for (int i = 0; i < batchCount; i++)
                    {
                        int len = reader.ReadInt32();
                        byte[] batchBytes = reader.ReadBytes(len);
                        combinedBytes.AddRange(batchBytes);
                    }
                    
                    mapData = combinedBytes.ToArray();
                }
                else
                {
                    mapData = System.IO.File.ReadAllBytes(targetPath);
                }

                var bytePacker = new BytePacker(mapData);

                var worldNetwork = MVGameControllerBase.Game.World as WorldNetwork;
                if (worldNetwork == null)
                {
                    MelonLogger.Error("[Bypass] Game.World is null or not a WorldNetwork! InitializeManagers might have failed.");
                    return;
                }

                MelonLogger.Msg("[Bypass] Forcing map deserialization...");
                worldNetwork.CreateGameWorldFromQueryData(bytePacker, 1);
                
                MelonLogger.Msg("[Bypass] Map loaded successfully!");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bypass] Failed to load map: {ex}");
            }
        }

private static void TryInjectKoGaMaSettings()
{
    try
    {
        var settingsContainers = UnityEngine.Resources.FindObjectsOfTypeAll<KoGaMaSettingsContainer>();
        KoGaMaSettingsContainer settingsContainer = (settingsContainers.Length > 0) ? settingsContainers[0] : null;
        
        if (settingsContainer is object)
        {
            var settingsProp = typeof(MVGameControllerBase).GetProperty("KoGaMaSettings", BindingFlags.Public | BindingFlags.Static);
            if (settingsProp is object && settingsProp.CanWrite)
            {
                settingsProp.SetValue(null, settingsContainer, null);
                MelonLogger.Msg("[Bypass] Successfully injected KoGaMaSettingsContainer.");
            }
        }
    }
    catch (Exception ex)
    {
        MelonLogger.Error($"[Bypass] Failed to inject KoGaMaSettings: {ex.Message}");
    }
}

        private static void TrySynthesizeGameSessionData()
        {
            try
            {
                var sessionData = new GameSessionData
                {
                    serverIP = "127.0.0.1",
                    region = "local",
                    planetID = 1,
                    gameMode = MVGameMode.Play
                };

                var prop = typeof(MVGameControllerBase).GetProperty("GameSessionData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (prop is object && prop.CanWrite)
                {
                    prop.SetValue(null, sessionData, null);
                }
                else
                {
                    var field = typeof(MVGameControllerBase).GetField("<GameSessionData>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                    field?.SetValue(null, sessionData);
                }
                
                MelonLogger.Msg("[Bypass] GameSessionData synthesized and assigned.");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bypass] Failed to synthesize GameSessionData: {ex.Message}");
            }
        }

        public static void FireFakeEvent(byte eventCode, Dictionary<byte, object> parameters)
        {
            try
            {
                var game = MVGameControllerBase.Game;
                if (game != null)
                {
                    var eventData = new EventData
                    {
                        Code = eventCode,
                        Parameters = parameters
                    };
                    
                    var onEventMethod = typeof(MVNetworkGame).GetMethod("OnEvent");
                    if (onEventMethod is object) 
                    {
                        onEventMethod.Invoke(game, new object[] { eventData });
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[FireFakeEvent] Failed: {ex.Message}");
            }
        }

        public static void TryCloneAndActivateAvatar()
        {
            if (_avatarSpawned)
            {
                MelonLogger.Warning("[Bypass] Avatar already spawned. Skipping.");
                return;
            }

            if (CachedSpawnerWoId == -1 || !(CachedSpawnerInstance is object))
            {
                MelonLogger.Error("[Bypass] SpawnPoint not cached. Cannot spawn avatar.");
                return;
            }

            _avatarSpawned = true;

            try
            {
                MelonLogger.Msg("[Bypass]  Starting direct avatar creation at SpawnPoint ");

                var worldNetwork = MVGameControllerBase.Game.World as WorldNetwork;
                if (!(worldNetwork is object) || !(worldNetwork.WorldObjectClientManagerNetwork is object))
                {
                    MelonLogger.Error("[Bypass] WorldNetwork or WOCM is null.");
                    return;
                }

                var wocm = worldNetwork.WorldObjectClientManagerNetwork;

                var woDictField = typeof(MVWorldObjectClientManagerNetwork).GetField("worldObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(woDictField is object)) woDictField = typeof(MVWorldObjectClientManager).GetField("worldObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                if (!(woDictField is object)) { MelonLogger.Error("[Bypass] Could not find worldObjects dictionary field."); return; }

                var worldObjectsDict = woDictField.GetValue(wocm) as Dictionary<int, MVWorldObjectClient>;
                if (!(worldObjectsDict is object)) { MelonLogger.Error("[Bypass] worldObjects dictionary is null."); return; }

                MelonLogger.Msg("[Bypass] Creating dummy MVGroup for avatar parent...");
                var groupData = new Dictionary<object, object>();
                groupData[WorldObjectDataParameters.WorldObjectType] = WorldObjectType.Group;
                groupData[WorldObjectDataParameters.Id] = 777777;
                groupData[WorldObjectDataParameters.GroudId] = -1;
                groupData[WorldObjectDataParameters.ItemId] = 0;
                groupData[WorldObjectDataParameters.OwnerActorNumber] = 1;
                groupData[WorldObjectDataParameters.Position] = Vector3.zero;
                groupData[WorldObjectDataParameters.Rotation] = Quaternion.identity;
                groupData[WorldObjectDataParameters.Scale] = Vector3.one;
                groupData[WorldObjectDataParameters.Data] = new Dictionary<object, object>();
                groupData[WorldObjectDataParameters.RuntimeData] = RuntimeVariablesRepository.GetRuntimeVariables(WorldObjectType.Group);

                MVGroup dummyGroup = KoGaMaPackageClient.WorldObjectFactory(groupData, worldObjectsDict, worldNetwork.WorldInventory.RuntimePrototypes) as MVGroup;
                if (dummyGroup is object)
                {
                    wocm.AddToWorldObjects(dummyGroup);
                    dummyGroup.Initialize();
                }

                MelonLogger.Msg("[Bypass] Creating MVAvatarLocal via factory...");
                var avatarData = new Dictionary<object, object>();
                avatarData[WorldObjectDataParameters.WorldObjectType] = WorldObjectType.PlayModeAvatar;
                avatarData[WorldObjectDataParameters.Id] = 999999;
                avatarData[WorldObjectDataParameters.GroudId] = dummyGroup.Id;
                avatarData[WorldObjectDataParameters.ItemId] = 0;
                avatarData[WorldObjectDataParameters.OwnerActorNumber] = 1;
                avatarData[WorldObjectDataParameters.PreviewOwnerProfileId] = 1;
                avatarData[WorldObjectDataParameters.Position] = CachedSpawnerPosition + new Vector3(0, 5, 0);
                avatarData[WorldObjectDataParameters.Rotation] = Quaternion.identity;
                avatarData[WorldObjectDataParameters.Scale] = Vector3.one;
                avatarData[WorldObjectDataParameters.Data] = new Dictionary<object, object>();

                var runtimeData = new Dictionary<object, object>();
                runtimeData.Add("health", 100f);
                runtimeData.Add("maxHealth", 100);
                runtimeData.Add("shield", 0f);
                runtimeData.Add("isFiring", false);
                runtimeData.Add("modifiers", new Dictionary<object, object>());
                runtimeData.Add("currentItem", new Dictionary<object, object> { { "type", 5 } });
                runtimeData.Add("lineOfFire", new Dictionary<object, object>());
                runtimeData.Add("invulnerable", false);
                runtimeData.Add("seat", -1);
                runtimeData.Add("spawnRoleModeType", 4);
                runtimeData.Add("headRotationYaw", 0f);
                runtimeData.Add("headRotationPitch", 0f);
                runtimeData.Add("pointRotationYaw", 0f);
                runtimeData.Add("pointRotationPitch", 0f);
                runtimeData.Add("size", 1f);
                runtimeData.Add("emote", 0);
                var animDict = new Dictionary<object, object>();
                animDict.Add("state", "Idle");
                animDict.Add("timeStamp", 0);
                runtimeData.Add("animation", animDict);
                avatarData[WorldObjectDataParameters.RuntimeData] = runtimeData;

                MVAvatarLocal avatarLocal = (MVAvatarLocal)KoGaMaPackageClient.WorldObjectFactory(avatarData, worldObjectsDict, worldNetwork.WorldInventory.RuntimePrototypes);
                CurrentAvatar = avatarLocal;
                if (!(avatarLocal is object)) { MelonLogger.Error("[Bypass] Factory failed to create MVAvatarLocal."); return; }

                wocm.AddToWorldObjects(avatarLocal);
                if (avatarLocal.GameObject is object) avatarLocal.GameObject.transform.position = CachedSpawnerPosition + new Vector3(0, 5, 0);

                try
                {
                    var avatarType = AccessTools.TypeByName("Avatar");
                    if (avatarType is object)
                    {
                        var fields = avatarLocal.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        bool assigned = false;
                        foreach (var f in fields)
                        {
                            if (f.FieldType.Equals(avatarType) || f.FieldType.IsAssignableFrom(avatarType))
                            {
                                var comp = avatarLocal.GameObject.GetComponent(avatarType);
                                if (comp == null) comp = avatarLocal.GameObject.AddComponent(avatarType);
                                f.SetValue(avatarLocal, comp);
                                assigned = true;
                                break;
                            }
                        }
                        if (assigned) MelonLogger.Msg("[Bypass] Manually assigned AvatarLocal component.");
                    }
                }
                catch { }

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string jsonPath = System.IO.Path.Combine(localAppData, "KogamaMaps");
                jsonPath = System.IO.Path.Combine(jsonPath, "avatar.json");
                if (!System.IO.File.Exists(jsonPath))
                {
                    MelonLogger.Error($"[Bypass] avatar.json not found at {jsonPath}");
                    return;
                }
                string json = System.IO.File.ReadAllText(jsonPath);
                JObject jsonData = JObject.Parse(json);

                int protoIdCounter = 600000;
                var partProtoIds = new Dictionary<string, int>();

                foreach (var part in jsonData)
                {
                    string boneName = part.Key;
                    if (part.Value == null) continue;
                    
                    JArray voxels = part.Value["voxels"] as JArray;
                    if (voxels == null || voxels.Count == 0)
                    {
                        MelonLogger.Warning($"[Bypass] No voxels found for {boneName} in JSON.");
                        continue;
                    }

                    var bp = new BytePacker();
                    bp.Write(voxels.Count);
                    byte[] identity = new byte[] { 20, 120, 124, 24, 4, 104, 100, 0 };

                    foreach (JObject voxel in voxels)
                    {
                        short x = (short)voxel["x"];
                        short y = (short)voxel["y"];
                        short z = (short)voxel["z"];
                        byte mat = (byte)(int)voxel["mat"];
                        JArray cornersArr = voxel["corners"] as JArray;
                        byte[] corners = new byte[8];
                        for (int i = 0; i < 8; i++) corners[i] = (byte)(int)cornersArr[i];

                        bp.Write(x);
                        bp.Write(y);
                        bp.Write(z);

                        byte compressionFlags = 4;
                        bool isIdentity = true;
                        for (int i = 0; i < 8; i++) { if (corners[i] != identity[i]) { isIdentity = false; break; } }
                        if (isIdentity) compressionFlags |= 1;
                        compressionFlags |= 2;

                        bp.Write(compressionFlags);
                        if (!isIdentity) bp.Write(corners);
                        bp.Write(mat);
                    }

                    int protoId = protoIdCounter++;
                    var proto = new RuntimePrototypeCubeModel(protoId, 1, 0.1f, bp.ToArray());
                    worldNetwork.WorldInventory.RuntimePrototypes.Add(protoId, proto);
                    partProtoIds[boneName] = protoId;
                    MelonLogger.Msg($"[Bypass] Created prototype for {boneName} ({voxels.Count} voxels) with SCALE: {proto.Scale}");
                }

                MelonLogger.Msg("[Bypass] Creating MVBody via factory...");
                var bodyData = new Dictionary<object, object>();
                bodyData[WorldObjectDataParameters.WorldObjectType] = WorldObjectType.Blueprint;
                bodyData[WorldObjectDataParameters.Id] = 888888;
                bodyData[WorldObjectDataParameters.GroudId] = avatarLocal.Id;
                bodyData[WorldObjectDataParameters.ItemId] = 0;
                bodyData[WorldObjectDataParameters.OwnerActorNumber] = 1;
                bodyData[WorldObjectDataParameters.Position] = Vector3.zero;
                bodyData[WorldObjectDataParameters.Rotation] = Quaternion.identity;
                bodyData[WorldObjectDataParameters.Scale] = Vector3.one;

                var dataDict = new Dictionary<object, object>();
                var blueprintData = new Dictionary<object, object>();
                blueprintData["ClientSideType"] = (byte)BlueprintType.Body;
                blueprintData["ChildrenMap"] = new Dictionary<object, object>();
                blueprintData[BlueprintData.AvatarAccessoryData2.ToString("d")] = new Dictionary<object, object>();
                dataDict["BlueprintData"] = blueprintData;
                bodyData[WorldObjectDataParameters.Data] = dataDict;
                bodyData[WorldObjectDataParameters.RuntimeData] = RuntimeVariablesRepository.GetRuntimeVariables(WorldObjectType.Blueprint);

                MVBody body = KoGaMaPackageClient.WorldObjectFactory(bodyData, worldObjectsDict, worldNetwork.WorldInventory.RuntimePrototypes) as MVBody;
                if (!(body is object)) { MelonLogger.Error("[Bypass] Factory failed to create MVBody."); return; }

                wocm.AddToWorldObjects(body);

                int partIdCounter = 800000;

                foreach (var kvp in partProtoIds)
                {
                    string boneName = kvp.Key;
                    int protoId = kvp.Value;

                    var partData = new Dictionary<object, object>();
                    partData[WorldObjectDataParameters.WorldObjectType] = WorldObjectType.CubeModel;
                    partData[WorldObjectDataParameters.Id] = partIdCounter++;
                    partData[WorldObjectDataParameters.GroudId] = body.Id;
                    partData[WorldObjectDataParameters.ItemId] = 0;
                    partData[WorldObjectDataParameters.OwnerActorNumber] = 1;
                    partData[WorldObjectDataParameters.Position] = Vector3.zero;
                    partData[WorldObjectDataParameters.Rotation] = Quaternion.identity;
                    partData[WorldObjectDataParameters.Scale] = Vector3.one;

                    var partDataDict = new Dictionary<object, object>();
                    partDataDict["protoTypeID"] = protoId;
                    partData[WorldObjectDataParameters.Data] = partDataDict;
                    partData[WorldObjectDataParameters.RuntimeData] = RuntimeVariablesRepository.GetRuntimeVariables(WorldObjectType.CubeModel);

                    MVCubeModelInstance part = KoGaMaPackageClient.WorldObjectFactory(partData, worldObjectsDict, worldNetwork.WorldInventory.RuntimePrototypes) as MVCubeModelInstance;
                    if (part is object)
                    {
                        wocm.AddToWorldObjects(part);
                        
                        ((Dictionary<object, object>)blueprintData["ChildrenMap"])[boneName] = part.Id;
                        MelonLogger.Msg($"[Bypass] Created body part {boneName} (ID: {part.Id})");
                    }
                }

                var mapDataMethod = typeof(MVBlueprintBase).GetMethod("MapDataToFields", BindingFlags.NonPublic | BindingFlags.Instance);
                if (mapDataMethod is object) mapDataMethod.Invoke(body, null);

                body.Initialize();

                var attachBodyMethod = typeof(MVAvatar).GetMethod("AttachBody", BindingFlags.NonPublic | BindingFlags.Instance);
                if (attachBodyMethod is object) attachBodyMethod.Invoke(avatarLocal, new object[] { body });

                try
                {
                    var fader = avatarLocal.GameObject.GetComponent<AvatarFader>();
                    if (fader is object)
                    {
                        var startMethod = AccessTools.Method(typeof(AvatarFader), "Start");
                        if (startMethod is object) startMethod.Invoke(fader, null);
                        MelonLogger.Msg("[Bypass] Refreshed AvatarFader materials.");
                    }
                }
                catch { }

                try
                {
                    avatarLocal.Initialize();
                    MelonLogger.Msg("[Bypass] MVAvatarLocal initialized successfully!");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Bypass] MVAvatarLocal.Initialize() FAILED:\n{ex}");
                }

                var localPlayer = MVGameControllerBase.LocalPlayer;
                if (localPlayer is object)
                {
                    localPlayer.Team = MVTeam.Blue;
                    var spawnRolesRuntimeData = new MV.WorldObject.SpawnRoles.SpawnRolesRuntimeData();
                    localPlayer.SetupPlayerWorldObjects(avatarLocal.Id, spawnRolesRuntimeData);
                }

                if (localPlayer is object)
                {
                    var srm = localPlayer.SpawnRolesManager;
                    if (srm is object)
                    {
                        try
                        {
                            srm.AddSpawnRole(avatarLocal.Id);
                            srm.ActivateSpawnRole(avatarLocal.Id, CachedSpawnerPosition, Quaternion.identity);
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[Bypass] ActivateSpawnRole failed:\n{ex}");
                        }
                        
                        try
                        {
                            localPlayer = MVGameControllerBase.LocalPlayer;
                            if (localPlayer is object)
                            {
                                var isReadyProp = localPlayer.GetType().GetProperty("IsReady", BindingFlags.Public | BindingFlags.Instance);
                                if (isReadyProp is object && isReadyProp.CanWrite)
                                {
                                    isReadyProp.SetValue(localPlayer, true, null);
                                }
                                
                                if (MVGameControllerBase.PlayModeUI is object)
                                {
                                    var setUIReadyMethod = MVGameControllerBase.PlayModeUI.GetType().GetMethod("SetUIReady");
                                    if (setUIReadyMethod is object)
                                    {
                                        setUIReadyMethod.Invoke(MVGameControllerBase.PlayModeUI, null);
                                        MelonLogger.Msg("[Bypass] Manually called SetUIReady() to unlock UI.");
                                    }
                                }
                            }

                            avatarLocal.GameObject.SetActive(true);
                            avatarLocal.Visible = true;
                            avatarLocal.Scale = Vector3.one;
                            MelonLogger.Msg($"[Bypass] Avatar Final Scale: {avatarLocal.Scale}");

                            var avatarLocalProp = typeof(MVAvatarLocal).GetProperty("AvatarLocal", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (avatarLocalProp is object)
                            {
                                object avatarLocalComp = avatarLocalProp.GetValue(avatarLocal, null);
                                if (avatarLocalComp is object)
                                {
                                    var camControllerProp = avatarLocalComp.GetType().GetProperty("CameraController");
                                    if (camControllerProp is object)
                                    {
                                        object camController = camControllerProp.GetValue(avatarLocalComp, null);
                                        if (camController is object)
                                        {
                                            var activateCamMethod = camController.GetType().GetMethod("ActivateCameraController");
                                            if (activateCamMethod is object)
                                            {
                                                activateCamMethod.Invoke(camController, null);
                                                MelonLogger.Msg("[Bypass] Activated Avatar Camera Controller.");
                                            }

                                            var setCameraMethod = camController.GetType().GetMethod("SetCamera", new[] { typeof(CameraType) });
                                            if (setCameraMethod is object)
                                            {
                                                setCameraMethod.Invoke(camController, new object[] { CameraType.ThirdPerson });
                                                MelonLogger.Msg("[Bypass] Set camera to ThirdPerson.");
                                            }
                                        }
                                    }
                                }
                            }

                            avatarLocal.SetMode(AvatarRuntimeState.Playing);

                            MVGameControllerBase.Game.PlayerController.SetAvatarLocalObject(avatarLocal);

                            var camManager = MVGameControllerBase.MainCameraManager;
                            if (camManager is object && camManager.MainCamera is object)
                            {
                                camManager.MainCamera.cullingMask = -1; 
                                camManager.MainCamera.useOcclusionCulling = false;
                                camManager.MainCamera.farClipPlane = 5000f;
                                camManager.MainCamera.clearFlags = CameraClearFlags.Skybox;
                                
                                if (camManager.CurrentCamera is object)
                                {
                                    camManager.CurrentCamera.Reset();
                                    camManager.CurrentCamera.transform.rotation = avatarLocal.Transform.rotation;
                                }
                            }

                            if (MVGameControllerBase.Game.GameCoinManager != null)
                            {
                                var trv = Traverse.Create(MVGameControllerBase.Game.GameCoinManager);
                                var coinField = trv.Field("<GameCoinAmount>k__BackingField");
                                if (coinField.FieldExists()) coinField.SetValue(9999);
                            }

                            if (MVGameControllerBase.GameMode == MVGameMode.Play)
                            {
                                var lockCursorProp = typeof(MVGameControllerDesktop).GetProperty("LockCursorManager", BindingFlags.Public | BindingFlags.Static);
                                if (lockCursorProp is object)
                                {
                                    object lockCursorManager = lockCursorProp.GetValue(null, null);
                                    if (lockCursorManager is object)
                                    {
                                        var cursorLockProp = lockCursorManager.GetType().GetProperty("CursorLock");
                                        if (cursorLockProp is object && cursorLockProp.CanWrite)
                                        {
                                            cursorLockProp.SetValue(lockCursorManager, true, null);
                                        }
                                    }
                                }
                            }

                            MelonLogger.Msg("[Bypass] Forced WalkMode, camera, and player controller attached.");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[Bypass] Camera/Input attach failed: {ex.Message}");
                        }

                        MelonLogger.Msg("[Bypass] Avatar spawned.");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bypass] TryCloneAndActivateAvatar FAILED: {ex}");
            }
        }

        private static bool _joinResponseProcessed = false;

        [HarmonyPatch(typeof(MVNetworkGame), "OnOperationResponse")]
        public static class MVNetworkGame_OnOperationResponse_Patch
        {
            public static bool Prefix(MVNetworkGame __instance, OperationResponse operationResponse)
            {
                if (operationResponse.OperationCode == 255 && operationResponse.ReturnCode == 0)
                {
                    if (_joinResponseProcessed) return false;
                    _joinResponseProcessed = true;

                    MelonLogger.Msg("[Bypass] Intercepted Join Response. Synthesizing full join data...");

                    try
                    {
                        __instance.ConnState = MVConnState.Joined;

                        var returnValues = new Dictionary<byte, object>();
                        
                        returnValues[211] = "{\"applicationDescFactoryBase\":{\"ApplicationDescs\":[]}}"; 
                        returnValues[182] = new Dictionary<object, object>(); 
                        returnValues[181] = 1; 
                        returnValues[184] = 1; 
                        returnValues[177] = "VGVzdA=="; 
                        returnValues[170] = (int)MVGameType.Classic; 
                        returnValues[16] = (byte)Region.dev; 
                        returnValues[212] = true; 
                        returnValues[82] = false; 
                        returnValues[168] = (int)ClientSettingFlags.None; 
                        returnValues[215] = 0; 
                        returnValues[233] = 0; 
                        returnValues[225] = ""; 
                        returnValues[226] = ""; 
                        returnValues[228] = 0; 
                        returnValues[229] = ""; 
                        returnValues[230] = false; 
                        returnValues[243] = 0; 
                        returnValues[241] = ""; 
                        returnValues[242] = ""; 
                        returnValues[240] = false; 
                        returnValues[231] = false; 
                        returnValues[232] = 0; 
                        returnValues[237] = false; 
                        returnValues[235] = 0; 
                        returnValues[238] = 0; 
                        returnValues[174] = "https://www.kogama.com/"; 
                        returnValues[104] = "https://www.kogama.com/static/"; 

                        returnValues[254] = 1; 
                        returnValues[14] = 1; 
                        returnValues[89] = (int)MVTeam.Blue; 
                        
                        var userProfileData = MV.WorldObject.MetaData.UserProfileData.GetTouristProfileData("MarshZee");
                        userProfileData.Gold = 99999;
                        returnValues[224] = Newtonsoft.Json.JsonConvert.SerializeObject(userProfileData);

                        var onJoinResponseMethod = AccessTools.Method(typeof(MVNetworkGame), "OnJoinResponse", new[] { typeof(Dictionary<byte, object>) });
                        if (onJoinResponseMethod is object)
                        {
                            onJoinResponseMethod.Invoke(__instance, new object[] { returnValues });
                            MelonLogger.Msg("[Bypass] Native OnJoinResponse executed successfully.");
                        }

                        if (MVGameControllerBase.LocalPlayer != null)
                        {
                            MVGameControllerBase.LocalPlayer.Level = 50;
                        }

                        try
                        {
                            var playerPlanetData = new MV.WorldObject.GamePassSystem.PlayerPlanetData();
                            var planetDataEvent = new EventData 
                            { 
                                Code = (byte)MVEventCodes.PlayerPlanetData, 
                                Parameters = new Dictionary<byte, object> 
                                {
                                    { 245, Newtonsoft.Json.JsonConvert.SerializeObject(playerPlanetData) }
                                }
                            };
                            __instance.OnEvent(planetDataEvent);
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[Bypass] Failed to synthesize PlayerPlanetData: {ex.Message}");
                        }

                        MVGameControllerBase.JoinState = MVJoinState.LoadGUI;

                        try
                        {
                            var playModeEventData = new EventData 
                            { 
                                Code = 61, 
                                Parameters = new Dictionary<byte, object>() 
                            };

                            var spawnRolesRuntimeData = new MV.WorldObject.SpawnRoles.SpawnRolesRuntimeData();
                            playModeEventData.Parameters[245] = Newtonsoft.Json.JsonConvert.SerializeObject(spawnRolesRuntimeData);
                            
                            playModeEventData.Parameters[191] = 0; 
                            playModeEventData.Parameters[35] = 0; 

                            var spawnRolesMetaData = new MV.WorldObject.SpawnRoles.SpawnRolesMetaData();
                            playModeEventData.Parameters[207] = Newtonsoft.Json.JsonConvert.SerializeObject(spawnRolesMetaData);

                            playModeEventData.Parameters[13] = new Dictionary<object, object>();

                            playModeEventData.Parameters[65] = (int)MVGameStateType.Round;
                            playModeEventData.Parameters[67] = 0; 
                            playModeEventData.Parameters[66] = 0; 
                            playModeEventData.Parameters[158] = System.BitConverter.GetBytes(0); 

                            var allModesSetupMethod = AccessTools.Method(typeof(MVNetworkGame), "AllModesSetup", new[] { typeof(EventData) });
                            if (allModesSetupMethod is object) allModesSetupMethod.Invoke(__instance, new object[] { playModeEventData });

                            var playModeSetupMethod = AccessTools.Method(typeof(MVNetworkGame), "PlayModeSetup", new[] { typeof(EventData) });
                            if (playModeSetupMethod is object) playModeSetupMethod.Invoke(__instance, new object[] { playModeEventData });

                            var setupLogicMethod = AccessTools.Method(typeof(MVNetworkGame), "SetupLogicManager", new[] { typeof(int) });
                            if (setupLogicMethod is object)
                            {
                                setupLogicMethod.Invoke(__instance, new object[] { 0 });
                                MelonLogger.Msg("[Bypass] Logic Manager initialized natively.");
                            }

                            MelonLogger.Msg("[Bypass] Native SetupUserPlayMode executed successfully.");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[Bypass] Failed to synthesize SetupUserPlayMode:\n{ex}");
                        }

                        var listener = __instance as IPhotonPeerListener;
                        if (listener != null)
                        {
                            listener.OnEvent(new EventData
                            {
                                Code = 63,
                                Parameters = new Dictionary<byte, object> { { 254, 1 } }
                            });
                        }

                        NeedsToLoadGui = true;
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[Bypass] Failed to synthesize join response: {ex}");
                    }

                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(KoGaMaPackageClient), "WorldObjectFactory")]
        public static class KoGaMaPackageClient_WorldObjectFactory_Patch
        {
            public static void Postfix(object __result, object[] __args)
            {
                if (__args != null && __args.Length > 1 && CachedWorldObjectsDict == null)
                {
                    CachedWorldObjectsDict = __args[1];
                }
                if (__args != null && __args.Length > 2 && CachedPrototypesDict == null)
                {
                    CachedPrototypesDict = __args[2];
                }

                if (__result == null) return;
                if (CachedSpawnerWoId >= 0) return;

                try
                {
                    var wo = __result as MVWorldObjectClient;
                    if (wo is MVSpawnPoint && CachedSpawnerInstance == null)
                    {
                        var idProp = wo.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
                        if (idProp is object)
                        {
                            CachedSpawnerWoId = (int)idProp.GetValue(wo, null);
                            CachedSpawnerPosition = wo.GameObject.transform.position;
                            CachedSpawnerInstance = wo;
                            MelonLogger.Msg($"[Bypass]  Found SpawnPoint! ID: {CachedSpawnerWoId} ");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Bypass] Spawner scan failed: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(WaitUntil), "keepWaiting", MethodType.Getter)]
        public static class WaitUntil_keepWaiting_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(MVGameControllerBase), "InitStandAlone")]
        public static class MVGameControllerBase_InitStandAlone_Patch
        {
            public static bool Prefix(MVGameControllerBase __instance, bool developmentMode)
            {
                MelonLogger.Msg("[Bypass] Intercepted InitStandAlone. Bypassing web session data fetch. Calling StartGame() directly.");
                AccessTools.Method(typeof(MVGameControllerBase), "StartGame").Invoke(__instance, null);
                return false;
            }
        }

        [HarmonyPatch(typeof(MVWorldObjectClientManagerNetwork), "AddToWorldObjects")]
        public static class MVWorldObjectClientManagerNetwork_AddToWorldObjects_Patch
        {
            public static bool Prefix(MVWorldObjectClient wo)
            {
                if (wo == null || wo.GameObject == null)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MVWorldObjectClientManagerNetwork), "AddWorldObject")]
        public static class MVWorldObjectClientManagerNetwork_AddWorldObject_Patch
        {
            public static void Prefix(Dictionary<object, object> data)
            {
                try
                {
                    if (data.ContainsKey(WorldObjectDataParameters.GroudId))
                    {
                        int parentId = (int)data[WorldObjectDataParameters.GroudId];
                        
                        if (parentId != -1 && parentId != 0)
                        {
                            var worldNetwork = MVGameControllerBase.Game.World as WorldNetwork;
                            if (worldNetwork != null)
                            {
                                var wocm = worldNetwork.WorldObjectClientManagerNetwork;
                                if (!wocm.Contains(parentId))
                                {
                                    MelonLogger.Warning($"[Bypass] Object missing parent {parentId}. Reparenting to RootGroup.");
                                    data[WorldObjectDataParameters.GroudId] = -1;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(WorldNetwork), "AddLink", new[] { typeof(MV.WorldObject.Link) })]
        public static class WorldNetwork_AddLink_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is NullReferenceException)
                {
                    return null;
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(WorldNetwork), "AddObjectLink", new[] { typeof(MV.WorldObject.ObjectLink) })]
        public static class WorldNetwork_AddObjectLink_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is NullReferenceException)
                {
                    return null;
                }
                return __exception;
            }
        }
        [HarmonyPatch(typeof(SpawnRoleDataMediator), "DeActivatePrevSpawnRoleDataReceiver")]
        public static class SpawnRoleDataMediator_DeActivatePrevSpawnRoleDataReceiver_Patch
        {
            public static bool Prefix(ISpawnRoleLocal prevSpawnRole)
            {
                if (prevSpawnRole == null)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "SetToSpawnTransform")]
        public static class MVAvatarLocal_SetToSpawnTransform_Patch
        {
            public static bool Prefix(MVAvatarLocal __instance)
            {
                try
                {
                    var camManager = MVGameControllerBase.MainCameraManager;
                    if (camManager is object) camManager.CancelTransitionCam();

                    Vector3 pos = CachedSpawnerPosition + new Vector3(0, 2, 0);
                    Quaternion rot = Quaternion.identity;

                    var setTransformMethod = AccessTools.Method(typeof(MVAvatarLocal), "SetTransform");
                    if (setTransformMethod is object)
                    {
                        setTransformMethod.Invoke(__instance, new object[] { pos, rot });
                        
                        if (camManager is object && camManager.CurrentCamera is object)
                        {
                            camManager.CurrentCamera.Reset();
                            camManager.CurrentCamera.transform.position = pos;
                            camManager.CurrentCamera.transform.rotation = rot;
                        }
                    }
                }
                catch { }
                return false;
            }
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "SetMode")]
        public static class MVAvatarLocal_SetMode_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is NullReferenceException)
                {
                    return null; 
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(PickupGUI), "Initialize")]
        public static class PickupGUI_Initialize_Patch
        {
            public static bool Prefix()
            {
                return false; 
            }
        }

        [HarmonyPatch(typeof(PickupGUI), "LateUpdate")]
        public static class PickupGUI_LateUpdate_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is NullReferenceException)
                {
                    return null; 
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "Update")]
        public static class MVAvatarLocal_Update_Patch
        {
            public static void Prefix(MVAvatarLocal __instance, ref InputToInGameAction interactionMap)
            {
                try
                {
                    if (__instance.IsInMode(SpawnRoleModeType.Dead))
                    {
                        var localPlayer = MVGameControllerBase.LocalPlayer;
                        if (localPlayer != null)
                        {
                            float respawnDuration = localPlayer.RespawnDuration;
                            if (respawnDuration <= 0f || float.IsInfinity(respawnDuration) || float.IsNaN(respawnDuration))
                            {
                                respawnDuration = 3f;
                            }

                            if (Time.time >= localPlayer.RespawnTime)
                            {
                                __instance.SetMode(AvatarRuntimeState.Playing);
                            }
                        }
                    }

                    object boxed = interactionMap;
                    var trv = Traverse.Create(boxed);
                    trv.Field("Fire").SetValue(UnityEngine.Input.GetMouseButton(0));
                    trv.Property("Fire").SetValue(UnityEngine.Input.GetMouseButton(0));
                    trv.Field("Use").SetValue(UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.E));
                    trv.Property("Use").SetValue(UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.E));
                    interactionMap = (InputToInGameAction)boxed;
                }
                catch { }
            }

            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is NullReferenceException || __exception is MissingMethodException)
                {
                    return null; 
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "AvatarStateChangedHandler")]
        public static class MVAvatarLocal_AvatarStateChangedHandler_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "InitializeShield")]
        public static class MVAvatarLocal_InitializeShield_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "InitializeHealth")]
        public static class MVAvatarLocal_InitializeHealth_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
        }

        [HarmonyPatch(typeof(MVAvatarLocal), "Activate")]
        public static class MVAvatarLocal_Activate_Patch
        {
            public static void Postfix(MVAvatarLocal __instance)
            {
                try
                {
                    if (__instance.GameObject is object)
                    {
                        var components = __instance.GameObject.GetComponents<MVComponent>();
                        var woParentField = AccessTools.Field(typeof(MVComponent), "worldObjectParent");
                        if (woParentField is object)
                        {
                            foreach (var comp in components)
                            {
                                if (woParentField.GetValue(comp) == null)
                                {
                                    woParentField.SetValue(comp, __instance);
                                }
                            }
                        }
                    }

                    var initHealthMethod = AccessTools.Method(typeof(MVAvatarLocal), "InitializeHealth");
                    if (initHealthMethod is object) initHealthMethod.Invoke(__instance, null);

                    var initShieldMethod = AccessTools.Method(typeof(MVAvatarLocal), "InitializeShield");
                    if (initShieldMethod is object) initShieldMethod.Invoke(__instance, null);

                    MelonLogger.Msg("[Bypass] Re-bound Health and Shield UI delegates successfully.");

                    var healthBars = UnityEngine.Resources.FindObjectsOfTypeAll<HealthBar>();
                    var healthBar = (healthBars.Length > 0) ? healthBars[0] : null;
                    if (healthBar is object)
                    {
                        healthBar.MaxHealth = __instance.MaxHealth.Value;
                        healthBar.Health = __instance.Health.Value;

                        var healthVar = __instance.Health;
                        healthVar.OnChange = (MVRuntimeDataVariable.OnChangeDelegate)Delegate.Combine(healthVar.OnChange, (MVRuntimeDataVariable.OnChangeDelegate)delegate(object obj)
                        {
                            float newHealth = (float)obj;
                            healthBar.Health = newHealth;
                        });

                        var maxHealthVar = __instance.MaxHealth;
                        maxHealthVar.OnChange = (MVRuntimeDataVariable.OnChangeDelegate)Delegate.Combine(maxHealthVar.OnChange, (MVRuntimeDataVariable.OnChangeDelegate)delegate(object obj)
                        {
                            healthBar.MaxHealth = (float)obj;
                            healthBar.Health = __instance.Health.Value;
                        });

                        MelonLogger.Msg("[Bypass] Manually bound HealthBar UI to Avatar health.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Bypass] Failed to re-bind UI delegates: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(InputToPlayerMovement), "HandleInputState")]
        public static class InputToPlayerMovement_HandleInputState_Patch
        {
            public static bool Prefix(InputToPlayerMovement __instance, bool fromFrameUpdate)
            {
                if (MVGameControllerBase.PlayModeUI != null && MVGameControllerBase.PlayModeUI.InLobbyState)
                {
                    return false;
                }

                int flags = 0;
                if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.W) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.UpArrow)) flags |= 2; 
                if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.S) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.DownArrow)) flags |= 8;
                if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.A) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftArrow)) flags |= 1;
                if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.D) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightArrow)) flags |= 4; 
                if (UnityEngine.Input.GetKey(UnityEngine.KeyCode.Space)) flags |= 16; // This is a jump key dumbass lmao (Don't read the flags for the love of...)

                var flagsType = AccessTools.Inner(typeof(InputToPlayerMovement), "MovementMapFlags");
                var stateField = AccessTools.Field(typeof(InputToPlayerMovement), "movementMapState");
                var frameField = AccessTools.Field(typeof(InputToPlayerMovement), "frameUpdateMovementMapState");

                if (!(flagsType is object)) return false;

                if (fromFrameUpdate)
                {
                    int currentFrame = (int)frameField.GetValue(__instance);
                    int combined = currentFrame | flags;
                    frameField.SetValue(__instance, combined);
                }
                else
                {
                    int currentFrame = (int)frameField.GetValue(__instance);
                    int combined = flags | currentFrame;
                    stateField.SetValue(__instance, combined);
                    
                    frameField.SetValue(__instance, 0);
                }

                return false; 
            }
        }

        [HarmonyPatch(typeof(PickupItem), "OnEquip")]
        public static class PickupItem_OnEquip_Patch
        {
            public static void Postfix(PickupItem __instance)
            {
                try
                {
                    var muzzleField = AccessTools.Field(typeof(PickupItem), "muzzlePoint");
                    if (muzzleField is object)
                    {
                        var muzzle = muzzleField.GetValue(__instance);
                        if (muzzle == null)
                        {
                            muzzleField.SetValue(__instance, __instance.transform);
                        }
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(MVPickupOwner), "get_IgnoreWOIDs")]
        public static class MVPickupOwner_GetIgnoreWOIDs_Patch
        {
            public static bool Prefix(ref HashSet<int> __result)
            {
                try
                {
                    __result = new HashSet<int>();
                    return false;
                }
                catch
                {
                    __result = new HashSet<int>();
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(MVNetworkGame), "OnTriggerBoxStayBegin")]
        public static class MVNetworkGame_OnTriggerBoxStayBegin_Patch
        {
            public static bool Prefix(MVNetworkGame __instance, int worldObjectID)
            {
                try
                {
                    var wo = __instance.WorldObjectClientManager.GetWorldObjectClient(worldObjectID);
                    if (wo == null || !(wo is ITriggerBoxEventsHandler))
                    {
                        return false; 
                    }
                }
                catch { return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(MVNetworkGame), "OnTriggerBoxStayEnd")]
        public static class MVNetworkGame_OnTriggerBoxStayEnd_Patch
        {
            public static bool Prefix(MVNetworkGame __instance, int worldObjectID)
            {
                try
                {
                    var wo = __instance.WorldObjectClientManager.GetWorldObjectClient(worldObjectID);
                    if (wo == null || !(wo is ITriggerBoxEventsHandler))
                    {
                        return false; 
                    }
                }
                catch { return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(MVCameraController), "UpdateCamera")]
        public static class MVCameraController_UpdateCamera_Patch
        {
            public static Exception Finalizer(Exception __exception)
            {
                if (__exception is NullReferenceException)
                {
                    return null; 
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(MVTeamManager), "GetTeamFromActorNr")]
        public static class MVTeamManager_GetTeamFromActorNr_Patch
        {
            public static bool Prefix(MVTeamManager __instance, int actorNumber, ref MVTeam __result)
            {
                if (actorNumber == 0)
                {
                    __result = MVTeam.Server;
                    return false;
                }

                var player = MVGameControllerBase.Game.MVPlayerContainer.GetPlayerUnsafe(actorNumber);
                if (player == null)
                {
                    __result = MVTeam.None;
                    return false;
                }

                __result = player.Team;
                return false; 
            }
        }

        [HarmonyPatch(typeof(InteractionDataHandler), "get_Team")]
        public static class InteractionDataHandler_GetTeam_Patch
        {
            public static bool Prefix(InteractionDataHandler __instance, ref MVTeam __result)
            {
                try
                {
                    var trv = Traverse.Create(__instance);
                    var woParent = trv.Field("worldObjectParent").GetValue<MVWorldObjectClient>();
                    if (woParent == null)
                    {
                        __result = MVTeam.None;
                        return false;
                    }
                }
                catch 
                { 
                    __result = MVTeam.None;
                    return false; 
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(InteractionDataHandler), "HandleInteraction")]
        public static class InteractionDataHandler_HandleInteraction_Patch
        {
            public static bool Prefix(InteractionDataHandler __instance, MVPickupOwner interactor, InteractionData interaction, bool interactionIsLocal)
            {
                try
                {
                    var trv = Traverse.Create(__instance);
                    var woParent = trv.Field("worldObjectParent").GetValue<MVWorldObjectClient>();
                    
                    if (woParent == null)
                    {
                        if (interactionIsLocal)
                        {
                            if (interactor != null && interactor.WorldObjectOwner != null)
                            {
                                interactor.WorldObjectOwner.ReceiveInteractionPackage(interaction, null);
                            }
                        }
                        return false; 
                    }
                }
                catch { return false; }
                return true; 
            }
        }

        [HarmonyPatch(typeof(PickupItemSlapGun), "OnFire")]
        public static class PickupItemSlapGun_OnFire_Patch
        {
            public static bool Prefix(PickupItemSlapGun __instance, bool isLocal)
            {
                try
                {
                    var trv = Traverse.Create(__instance);
                    var owner = trv.Field("owner").GetValue<MVPickupOwner>();
                    var muzzlePoint = trv.Field("muzzlePoint").GetValue<Transform>();
                    var audioSource = trv.Field("audioSource").GetValue<AudioSource>();
                    var slapSounds = trv.Field("slapSounds").GetValue<AudioClip[]>();
                    var maxRange = trv.Field("maxRange").GetValue<float>();
                    var slapStrength = trv.Field("slapStrength").GetValue<float>();
                    var impulseRayPrefab = __instance.impulseRayPrefab;
                    var slapColor = trv.Field("slapColor").GetValue<Color>();
                    var layerMask = trv.Field("layerMask").GetValue<int>();
                    var damage = SlapGunHitPackage.Create(Vector3.zero).Damage;

                    if (muzzlePoint == null) muzzlePoint = __instance.transform;
                    if (audioSource != null && slapSounds != null && slapSounds.Length > 0)
                    {
                        audioSource.clip = slapSounds[UnityEngine.Random.Range(0, slapSounds.Length - 1)];
                        MVGameControllerBase.AudioManager.Play("Sound - slapGunFire", audioSource, audioSource.transform.position);
                    }

                    Ray ray = new Ray(muzzlePoint.position, owner.LookDirection);

                    if (owner.IsLocal)
                    {
                        List<VoxelHit> list = CollisionDetection.MVSphereCastAll(ray, 2f, maxRange, owner.IgnoreWOIDs, layerMask);
                        for (int i = 0; i < list.Count; i++)
                        {
                            VoxelHit voxelHit = list[i];
                            MVGameControllerBase.Game.World.RuntimeEventManager.SendRemoveOneFineGrainedCube(voxelHit, slapStrength);
                            
                            MVWorldObjectClient worldObjectClient = MVGameControllerBase.WOCM.GetWorldObjectClient(voxelHit.woId);
                            
                            if (worldObjectClient != null)
                            {
                                Vector3 impulse = ComputeImpulseDirection(ray) * slapStrength;
                                InteractionDataHandlerBase interactionDataHandlerBase = worldObjectClient.InteractionDataHandlerBase;
                                if (interactionDataHandlerBase != null && !MVGameControllerBase.Game.LocalPlayer.IsOnSameTeam(worldObjectClient))
                                {
                                    interactionDataHandlerBase.HandleInteraction(owner, SlapGunHitPackage.Create(impulse), interactionIsLocal: false);
                                    if (worldObjectClient is IBulletImpactVisualizer)
                                    {
                                        ((IBulletImpactVisualizer)worldObjectClient).VisualizeBulletImpact(voxelHit, ray, owner.WorldObjectOwner.OwnerActorNr, damage);
                                    }
                                }
                            }
                        }
                    }

                    Vector3 target = FindRayTarget(ray, maxRange);
                    if (impulseRayPrefab != null)
                    {
                        ImpulseRay impulseRay = UnityEngine.Object.Instantiate(impulseRayPrefab, muzzlePoint.position, Quaternion.identity);
                        impulseRay.Initialize(target);
                        impulseRay.radius = 1.2f;
                        impulseRay.startColor = slapColor;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[SlapGun Fire Override] {ex.Message}");
                }
                return false; 
            }

            private static Vector3 ComputeImpulseDirection(Ray lineOfFire)
            {
                Vector3 direction = lineOfFire.direction;
                direction.y += 0.2f;
                return direction.normalized;
            }

            private static Vector3 FindRayTarget(Ray lineOfFire, float maxRange)
            {
                VoxelHit voxelHit;
                return (!CollisionDetection.MVHit(lineOfFire, out voxelHit, maxRange, new HashSet<int>(), 1 << LayerMask.NameToLayer("Default"))) ? lineOfFire.GetPoint(maxRange) : voxelHit.point;
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), "LogError", new System.Type[] { typeof(object) })]
        public static class Debug_LogError_Object_Patch
        {
            public static bool Prefix(object message)
            {
                if (message == null) return true;
                
                if (message is string s && (s == "Bad state" || s.Contains("wo does not exist") || s.Contains("Object reference not set to an instance of an object") || s.Contains("Screen position out of view frustum")))
                {
                    return false;
                }
                else if (message is System.Exception ex && (ex is NullReferenceException || ex.Message.Contains("Object reference not set to an instance of an object")))
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), "LogError", new System.Type[] { typeof(object), typeof(UnityEngine.Object) })]
        public static class Debug_LogError_Object_Context_Patch
        {
            public static bool Prefix(object message)
            {
                if (message == null) return true;
                
                if (message is string s && (s == "Bad state" || s.Contains("wo does not exist") || s.Contains("Object reference not set to an instance of an object") || s.Contains("Screen position out of view frustum")))
                {
                    return false;
                }
                else if (message is System.Exception ex && (ex is NullReferenceException || ex.Message.Contains("Object reference not set to an instance of an object")))
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), "LogException", new System.Type[] { typeof(System.Exception) })]
        public static class Debug_LogException_Patch
        {
            public static bool Prefix(System.Exception exception)
            {
                if (exception is NullReferenceException || exception is MissingMethodException)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Debug), "LogException", new System.Type[] { typeof(System.Exception), typeof(UnityEngine.Object) })]
        public static class Debug_LogException_Context_Patch
        {
            public static bool Prefix(System.Exception exception)
            {
                if (exception is NullReferenceException || exception is MissingMethodException)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(CullingApiWrapper), "IsVisible")]
        public static class CullingApiWrapper_IsVisible_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(CullingApiWrapper), "Visible")]
        public static class CullingApiWrapper_Visible_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(WaterPlaneManager), "AddWaterPlaneLogicCube")]
        public static class WaterPlaneManager_AddWaterPlaneLogicCube_Patch
        {
            public static void Postfix(WaterPlaneManager __instance)
            {
                try
                {
                    var waterField = AccessTools.Field(typeof(WaterPlaneManager), "water");
                    if (waterField is object)
                    {
                        var water = waterField.GetValue(__instance) as UnityEngine.Component;
                        if (water != null)
                        {
                            water.gameObject.SetActive(true);
                            var renderer = water.GetComponent<Renderer>();
                            if (renderer != null) renderer.enabled = true;
                        }
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(MVPlayer), "get_IsTourist")] // <---- Don't ask about this one.
        public static class MVPlayer_IsTourist_Patch
        {
            public static bool Prefix(MVPlayer __instance, ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(MVGameControllerBase), "get_IsTouristSession")]
        public static class MVGameControllerBase_IsTouristSession_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(DesktopPlayModeController), "get_InLobbyState")]
        public static class DesktopPlayModeController_GetInLobbyState_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(DesktopPlayModeController), "set_InLobbyState")]
        public static class DesktopPlayModeController_SetInLobbyState_Patch
        {
            public static bool Prefix()
            {
                return false; 
            }
        }

        [HarmonyPatch(typeof(DesktopPlayModeController), "Initialize")]
        public static class DesktopPlayModeController_Initialize_Patch
        {
            public static void Postfix(DesktopPlayModeController __instance)
            {
                try
                {
                    var lockCursorProp = typeof(MVGameControllerDesktop).GetProperty("LockCursorManager", BindingFlags.Public | BindingFlags.Static);
                    if (lockCursorProp is object)
                    {
                        object lockCursorManager = lockCursorProp.GetValue(null, null);
                        if (lockCursorManager != null)
                        {
                            var cursorLockProp = lockCursorManager.GetType().GetProperty("CursorLock");
                            if (cursorLockProp is object && cursorLockProp.CanWrite)
                            {
                                cursorLockProp.SetValue(lockCursorManager, true, null);
                                IsUIOpen = false;
                            }
                        }
                    }

                    var eventSystem = UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
                    if (eventSystem != null)
                    {
                        eventSystem.gameObject.SetActive(true);
                        eventSystem.enabled = true;
                    }

                    if (MVGameControllerBase.LocalPlayer != null)
                    {
                        var inGameControllerField = AccessTools.Field(typeof(DesktopPlayModeController), "inGameController");
                        if (inGameControllerField is object)
                        {
                            var inGameController = inGameControllerField.GetValue(__instance) as DesktopInGameGUIController;
                            if (inGameController != null)
                            {
                                var levelBadgeField = AccessTools.Field(typeof(DesktopInGameGUIController), "levelBadge");
if (levelBadgeField is object)
{
    var levelBadge = levelBadgeField.GetValue(inGameController);
    if (levelBadge != null)
    {
        var setLevelMethod = levelBadge.GetType().GetMethod("SetLevel");
        if (setLevelMethod is object) 
        {
            setLevelMethod.Invoke(levelBadge, new object[] { MVGameControllerBase.LocalPlayer.Level });
            MelonLogger.Msg("[Bypass] Manually updated Level Badge UI.");
        }
    }
}
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Bypass] Failed to skip lobby state UI: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(DesktopPlayModeController), "Awake")]
        public static class DesktopPlayModeController_Awake_Patch
        {
            public static bool Prefix(DesktopPlayModeController __instance)
            {
                try
                {
                    var existing = UnityEngine.Object.FindObjectOfType<DesktopPlayModeController>();
                    if (existing != null && existing != __instance)
                    {
                        MelonLogger.Warning("[Bypass] Destroyed duplicate DesktopPlayModeController UI.");
                        UnityEngine.Object.Destroy(__instance.gameObject);
                        return false; 
                    }
                }
                catch { }
                return true;
            }
        }

[HarmonyPatch(typeof(MVTriggerBox), "triggerBoxEvents_TriggerEnter")]
public static class MVTriggerBox_TriggerEnter_Patch
{
    public static bool Prefix(MVTriggerBox __instance, TriggerEventArgs e)
    {
        try
        {
            var enterMethod = AccessTools.Method(typeof(MVTriggerBox), "Enter");
            if (enterMethod != null)
            {
                enterMethod.Invoke(__instance, new object[] { e.instigatorWOID });
            }
        }
        catch { }
        
        return false; // Skip sending the broken OperationRequest because I don't know how to deal with all these operations. (I'm no wizard, barking up the wrong picture here :aaagaben:)
    }
}

[HarmonyPatch(typeof(MVTriggerBox), "triggerBoxEvents_TriggerExit")]
public static class MVTriggerBox_TriggerExit_Patch
{
    public static bool Prefix(MVTriggerBox __instance)
    {
        try
        {
            var exitMethod = AccessTools.Method(typeof(MVTriggerBox), "Exit");
            if (exitMethod != null)
            {
                exitMethod.Invoke(__instance, null);
            }
        }
        catch { }
        
        return false;
    }
}

        [HarmonyPatch(typeof(DesktopPlayModeController), "OnDestroy")]
        public static class DesktopPlayModeController_OnDestroy_Patch
        {
            public static bool Prefix(DesktopPlayModeController __instance)
            {
                if (MVGameControllerBase.PlayModeUI != __instance)
                {
                    return false; 
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(DesktopPlayModeController), "HandleInput")]
        public static class DesktopPlayModeController_HandleInput_Patch
        {
            public static bool Prefix(DesktopPlayModeController __instance)
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Tab) || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                {
                    IsUIOpen = !IsUIOpen;

                    if (IsUIOpen)
                    {
                        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
                        UnityEngine.Cursor.visible = true;
                    }
                    else
                    {
                        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
                        UnityEngine.Cursor.visible = false;
                    }
                }
                return true; 
            }
        }

        [HarmonyPatch(typeof(ChatControllerUGUI), "InitializeReady")]
        public static class ChatControllerUGUI_InitializeReady_Patch
        {
            public static void Postfix(ChatControllerUGUI __instance)
            {
                try
                {
                    var inputAreaRootField = AccessTools.Field(typeof(ChatControllerUGUI), "inputAreaRoot");
                    var inputAreaDeactivatedField = AccessTools.Field(typeof(ChatControllerUGUI), "inputAreaDeactivated");
                    var enterChatButtonField = AccessTools.Field(typeof(ChatControllerUGUI), "enterChatButton");

                    if (inputAreaRootField is object)
                    {
                        var inputAreaRoot = inputAreaRootField.GetValue(__instance) as RectTransform;
                        if (inputAreaRoot != null) inputAreaRoot.gameObject.SetActive(true);
                    }
                    if (inputAreaDeactivatedField is object)
                    {
                        var inputAreaDeactivated = inputAreaDeactivatedField.GetValue(__instance) as RectTransform;
                        if (inputAreaDeactivated != null) inputAreaDeactivated.gameObject.SetActive(false);
                    }
                    if (enterChatButtonField is object)
                    {
                        var enterChatButton = enterChatButtonField.GetValue(__instance) as UnityEngine.Component;
                        if (enterChatButton != null) enterChatButton.gameObject.SetActive(true);
                    }
                    
                    MelonLogger.Msg("[Bypass] ChatControllerUGUI initialized and input forced active.");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Bypass] Failed to force chat active: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(ChatControllerUGUI), "Update")]
        public static class ChatControllerUGUI_Update_Patch
        {
            private static bool _isChatFocused = false;

            public static void Prefix(ChatControllerUGUI __instance)
            {
                try
                {
                    if (MVGameControllerBase.JoinState != MVJoinState.Playing) return;

                    var inputFieldField = AccessTools.Field(typeof(ChatControllerUGUI), "inputField");
                    if (!(inputFieldField is object)) return;

                    var inputField = inputFieldField.GetValue(__instance);
                    if (!(inputField is object)) return;

                    var textProp = inputField.GetType().GetProperty("text");
                    if (!(textProp is object)) return;

                    bool tPressed = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.T);
                    bool enterPressed = UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Return) || UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.KeypadEnter);

                    if (!_isChatFocused && (tPressed || enterPressed))
                    {
                        _isChatFocused = true;
                        IsUIOpen = true;
                        
                        __instance.ChatFocusChanged(true);
                        
                        var activateMethod = inputField.GetType().GetMethod("ActivateInputField");
                        if (activateMethod is object) activateMethod.Invoke(inputField, null);

                        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
                        UnityEngine.Cursor.visible = true;
                    }
                    else if (_isChatFocused && enterPressed)
                    {
                        _isChatFocused = false;
                        IsUIOpen = false;
                        
                        string text = (string)textProp.GetValue(inputField, null);

                        if (!string.IsNullOrEmpty(text.Trim()))
                        {
                            string username = "OfflinePlayer";
                            var localPlayer = MVGameControllerBase.LocalPlayer;
                            if (localPlayer != null && localPlayer.UserProfileData != null)
                            {
                                username = localPlayer.UserProfileData.UserName;
                            }

                            string formattedMsg = $"<color=#FFFFFF>[{username}]: </color><color=#FFFFFF>{text}</color>";

                            var addLineMethod = AccessTools.Method(typeof(ChatControllerUGUI), "AddLine");
                            if (addLineMethod is object)
                            {
                                addLineMethod.Invoke(__instance, new object[] { formattedMsg });
                            }
                        }

                        textProp.SetValue(inputField, "", null);
                        __instance.ChatFocusChanged(false);

                        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
                        UnityEngine.Cursor.visible = false;
                    }
                }
                catch (Exception ex) 
                { 
                    MelonLogger.Error($"[Chat Patch] Failed: {ex.Message}"); 
                }
            }
        }

        [HarmonyPatch(typeof(MVLocalObjectController), "UpdateLocalControlledObjects")] // <--- I won't fix this
        public static class MVLocalObjectController_UpdateLocalControlledObjects_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is NotImplementedException ? null : __exception;
        }

        [HarmonyPatch(typeof(MVLocalObjectController), "FixedUpdateLocalControlledObjects")] // <--- I won't fix this (Again)
        public static class MVLocalObjectController_FixedUpdateLocalControlledObjects_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is NotImplementedException ? null : __exception;
        }

[HarmonyPatch(typeof(AvatarMotor), "DealImpactDamage")]
public static class AvatarMotor_DealImpactDamage_Patch
{
    public static bool Prefix(AvatarMotor __instance)
    {
        try
        {
            var trv = Traverse.Create(__instance);
            var impactState = trv.Field("impactState").GetValue();
            var interactableLocal = trv.Field("interactableLocal").GetValue();

            if (impactState is null || interactableLocal is null)
            {
                return false; 
            }
        }
        catch { }

        return true;
    }
}

        [HarmonyPatch(typeof(MVTeamManager), "OnAddSpawnPoint")]
        public static class MVTeamManager_OnAddSpawnPoint_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is Exception ? null : __exception;
        }

        [HarmonyPatch(typeof(MVNetworkGame), "Service")]
        public static class MVNetworkGame_Service_Patch
        {
            public static bool Prefix()
            {
                return false; 
            }
        }

        [HarmonyPatch(typeof(MVNetworkGame), "OnStatusChanged")]
        public static class MVNetworkGame_OnStatusChanged_Patch
        {
            public static bool Prefix(ExitGames.Client.Photon.StatusCode statusCode)
            {
                if (statusCode == ExitGames.Client.Photon.StatusCode.Disconnect || 
                    statusCode == ExitGames.Client.Photon.StatusCode.TimeoutDisconnect ||
                    statusCode == ExitGames.Client.Photon.StatusCode.DisconnectByServerLogic)
                {
                    MelonLogger.Warning("[Bypass] Ignored Photon Disconnect status.");
                    return false; 
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(UnityEngine.Application), "Quit", new System.Type[] { })]
        public static class Application_Quit_Patch
        {
            public static bool Prefix()
            {
                return false; 
            }
        }

        [HarmonyPatch(typeof(MVGameControllerBase), "ApplicationQuit")]
        public static class MVGameControllerBase_ApplicationQuit_Patch
        {
            public static bool Prefix()
            {
                return false; 
            }
        }

public static void InitializeSettingsAndSkybox()
{
    try
    {
        var settingsContainer = UnityEngine.Resources.FindObjectsOfTypeAll<KoGaMaSettingsContainer>().FirstOrDefault();
        if (settingsContainer is object)
        {
            var settingsProp = typeof(MVGameControllerBase).GetProperty("KoGaMaSettings", BindingFlags.Public | BindingFlags.Static);
            if (settingsProp is object && settingsProp.CanWrite)
            {
                settingsProp.SetValue(null, settingsContainer, null);
                MelonLogger.Msg("[Bypass] Successfully injected KoGaMaSettingsContainer.");
            }
        }
        else
        {
            MelonLogger.Error("[Bypass] KoGaMaSettingsContainer not found in memory.");
        }

        var onPostGameInitField = typeof(MVGameControllerBase).GetField("OnPostGameInit", BindingFlags.Public | BindingFlags.Static);
        if (onPostGameInitField is object)
        {
            var del = onPostGameInitField.GetValue(null) as MVGameControllerBase.OnPostGameInitDelegate;
            if (del is object)
            {
                del.Invoke();
                MelonLogger.Msg("[Bypass] Invoked OnPostGameInit to initialize SkyboxManager.");
            }
        }
    }
    catch (Exception ex)
    {
        MelonLogger.Error($"[Bypass] Failed to initialize settings/skybox: {ex.Message}");
    }
}

        [HarmonyPatch(typeof(MV.WorldObject.Security.SecurityHelper), "Decrypt")]
        public static class SecurityHelper_Decrypt_Patch
        {
            public static Exception Finalizer(Exception __exception, ref string __result)
            {
                if (__exception != null)
                {
                    __result = string.Empty;
                    return null; 
                }
                return __exception;
            }
        }

        [HarmonyPatch(typeof(HackingToolDetector), "Initialize")]
        public static class HackingToolDetector_Initialize_Patch
        {
            public static bool Prefix()
            {
                return false; 
            }
        }

        [HarmonyPatch(typeof(MVWorldObjectClientManagerNetwork), "AddToWorldObjects")]
        public static class MVWorldObjectClientManagerNetwork_AddToWorldObjects_Patch2
        {
            public static Exception Finalizer(Exception __exception) => __exception is ArgumentException ? null : __exception;
        }

        [HarmonyPatch(typeof(MVBody), "Initialize")]
        public static class MVBody_Initialize_Patch
        {
            public static Exception Finalizer(Exception __exception) => __exception is Exception ? null : __exception;
        }

        [HarmonyPatch(typeof(LockCursorManager3DMode), "set_CursorLock")]
        public static class LockCursorManager3DMode_SetCursorLock_Patch
        {
            public static bool Prefix(bool value)
            {
                try
                {
                    if (value)
                    {
                        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
                        UnityEngine.Cursor.visible = false;
                    }
                    else
                    {
                        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.None;
                        UnityEngine.Cursor.visible = true;
                    }
                }
                catch { }
                return false; 
            }
        }

        [HarmonyPatch(typeof(PickupItemImpulseGun), "Fire")]
        public static class PickupItemImpulseGun_Fire_Patch
        {
            public static bool Prefix(PickupItemImpulseGun __instance, int avatarId, float impulseMagnitude, float recoilMagnitude)
            {
                try
                {
                    var trv = Traverse.Create(__instance);
                    var owner = trv.Field("owner").GetValue<MVPickupOwner>();
                    var muzzlePoint = trv.Field("muzzlePoint").GetValue<Transform>();
                    var radius = trv.Field("radius").GetValue<float>();
                    var missColor = trv.Field("missColor").GetValue<Color>();
                    var impulseRayPrefab = __instance.impulseRayPrefab;

                    if (muzzlePoint == null) muzzlePoint = __instance.transform;

                    Ray lineOfFire = new Ray(owner.LookOrigin, owner.LookDirection);
                    Vector3 vector = trv.Method("FindRayTarget", new object[] { lineOfFire }).GetValue<Vector3>();

                    if (owner.IsLocal)
                    {
                        try
                        {
                            List<MVWorldObjectClient> list = trv.Method("SphereCastAgainstWorldObjects", new object[] { lineOfFire }).GetValue<List<MVWorldObjectClient>>();
                            for (int i = 0; i < list.Count; i++)
                            {
                                MVWorldObjectClient mVWorldObjectClient = list[i];
                                InteractionDataHandlerBase interactionDataHandlerBase = mVWorldObjectClient.InteractionDataHandlerBase;
                                if (interactionDataHandlerBase != null && !MVGameControllerBase.Game.LocalPlayer.IsOnSameTeam(mVWorldObjectClient))
                                {
                                    Vector3 impulse = trv.Method("ComputeImpulseDirection", new object[] { lineOfFire }).GetValue<Vector3>() * impulseMagnitude;
                                    interactionDataHandlerBase.HandleInteraction(owner, ImpulseHitPackage.Create(impulse), interactionIsLocal: false);
                                    if (mVWorldObjectClient is IBulletImpactVisualizer)
                                    {
                                        ((IBulletImpactVisualizer)mVWorldObjectClient).VisualizeBulletImpact(default(VoxelHit), lineOfFire, owner.WorldObjectOwner.OwnerActorNr, 0f);
                                    }
                                }
                            }
                        }
                        catch { }

                        Vector3 b = owner.transform.position + Vector3.up * 1.5f;
                        float num = Vector3.Distance(vector, b);
                        if (num < 10f)
                        {
                            float num2 = recoilMagnitude / Mathf.Max(num * 0.5f, 1f);
                            Vector3 impulse2 = -lineOfFire.direction * num2;
                            MVRigidBody component = owner.GetComponent<MVRigidBody>();
                            if (component != null)
                            {
                                component.AddImpulse(impulse2, suspendImpactDamage: true);
                            }
                        }
                    }

                    if (impulseRayPrefab != null)
                    {
                        ImpulseRay impulseRay = UnityEngine.Object.Instantiate(impulseRayPrefab, muzzlePoint.position, Quaternion.identity);
                        impulseRay.transform.position = muzzlePoint.position;
                        impulseRay.radius = radius;
                        impulseRay.startColor = missColor;
                        impulseRay.Initialize(vector);
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[ImpulseGun Fire Override] {ex.Message}");
                }
                return false; 
            }
        }
    }

[HarmonyPatch(typeof(MVAvatarLocal), "FixedUpdate")]
public static class MVAvatarLocal_FixedUpdate_Patch
{
    public static bool Prefix(MVAvatarLocal __instance)
    {
        try
        {
            var trv = Traverse.Create(__instance);
            var avatarLocalModes = trv.Field("avatarLocalModes").GetValue();
            
            // If avatarLocalModes is null, skip the update to prevent NRE since the game is too retarde to register these on it's own (Fuck you Michal)
            if (avatarLocalModes is null)
            {
                return false; 
            }
        }
        catch { }

        return true; 
    }
}

[HarmonyPatch(typeof(MVAvatar), "UpdateControllerLateUpdate")]
public static class MVAvatar_UpdateControllerLateUpdate_Patch
{
    public static bool Prefix(MVAvatar __instance)
    {
        try
        {
            var trv = Traverse.Create(__instance);
            var limbManager = trv.Field("limbManager").GetValue();
            var avatarPickupOwner = trv.Field("avatarPickupOwner").GetValue();
            var body = trv.Field("body").GetValue();

            if (limbManager is null || avatarPickupOwner is null || body is null)
            {
                return false; 
            }
        }
        catch { }

        return true; 
    }
}

[HarmonyPatch(typeof(MVGameControllerDesktop), "UpdateInternal")]
public static class MVGameControllerDesktop_UpdateInternal_Patch
{
    public static bool Prefix(MVGameControllerDesktop __instance)
    {
        try
        {
            if (!MVGameControllerBase.IsInitialized)
            {
                if (MVGameControllerBase.Game is null || MVGameControllerBase.Game.LocalPlayer is null)
                {
                    return false; 
                }
            }
        }
        catch { }

        return true; 
    }
}

[HarmonyPatch(typeof(AwayMonitor), "Update")]
public static class AwayMonitor_Update_Patch
{
    public static bool Prefix()
    {
        var instanceField = AccessTools.Field(typeof(AwayMonitor), "instance");
        if (instanceField is object)
        {
            var instance = instanceField.GetValue(null);
            if (instance is null)
            {
                return false; 
            }
        }
        return true; 
    }
}
}

    public static class PhotonInProcessStub
    {
        [HarmonyPatch(typeof(PhotonPeer), "SendOperation")]
        public static class PhotonPeer_SendOperation_Patch
        {
            public static bool Prefix(PhotonPeer __instance, byte operationCode, Dictionary<byte, object> operationParameters, SendOptions sendOptions)
            {
                try
                {
                    IPhotonPeerListener listener = __instance.Listener;
                    if (listener == null) return false;

                    switch (operationCode)
                    {
                        case 248:
                            listener.OnOperationResponse(new OperationResponse
                            {
                                OperationCode = 248,
                                ReturnCode = 0,
                                DebugMessage = "",
                                Parameters = new Dictionary<byte, object>()
                            });
                            break;

                        case 255:
                            var joinParams = new Dictionary<byte, object>
                            {
                                { 254, 1 },
                                { 252, new int[] { 1 } }
                            };

                            listener.OnOperationResponse(new OperationResponse
                            {
                                OperationCode = 255,
                                ReturnCode = 0,
                                DebugMessage = "",
                                Parameters = joinParams
                            });

                            listener.OnEvent(new EventData
                            {
                                Code = 255,
                                Parameters = new Dictionary<byte, object> { { 254, 1 } }
                            });

                            listener.OnEvent(new EventData
                            {
                                Code = 63,
                                Parameters = new Dictionary<byte, object> { { 254, 1 } }
                            });
                            break;

                        case 113:
                            listener.OnOperationResponse(new OperationResponse
                            {
                                OperationCode = 113,
                                ReturnCode = 0,
                                DebugMessage = "",
                                Parameters = new Dictionary<byte, object>()
                            });
                            break;

                        case 47:
                            int seatOwnerWoID = 0;
                            int worldObjectID = 0;
                            byte seatID = 0;

                            if (operationParameters.ContainsKey(72))
                            {
                                var dict = (Dictionary<object, object>)operationParameters[72];
                                if (dict.ContainsKey((byte)4)) seatOwnerWoID = (int)dict[(byte)4];
                                if (dict.ContainsKey((byte)0)) worldObjectID = (int)dict[(byte)0];
                            }
                            if (operationParameters.ContainsKey(141)) seatID = (byte)operationParameters[141];

                            listener.OnOperationResponse(new OperationResponse
                            {
                                OperationCode = 47,
                                ReturnCode = 0,
                                DebugMessage = "",
                                Parameters = new Dictionary<byte, object>()
                            });

                            var eventParams = new Dictionary<byte, object>();
                            eventParams[254] = 1; // ActorNr (MVPlayer C# Assembly)
                            eventParams[141] = seatID;
                            
                            var innerDict = new Dictionary<object, object>();
                            innerDict[(byte)4] = seatOwnerWoID;
                            innerDict[(byte)0] = worldObjectID;
                            eventParams[72] = innerDict;

                            listener.OnEvent(new EventData
                            {
                                Code = (byte)MVEventCodes.AttachWorldObjectToSeat,
                                Parameters = eventParams
                            });
                            break;

                        case 48: // Leave Vehicle (Doesn't work, couldn't make it)
                            listener.OnOperationResponse(new OperationResponse
                            {
                                OperationCode = 48,
                                ReturnCode = 0,
                                DebugMessage = "",
                                Parameters = new Dictionary<byte, object>()
                            });
                            break;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PhotonStub] CRASHED: {ex}");
                }

                return false; 
            }
        }
    }

    public static class UrlUtility
    {
        public static string ExtractPathAndQuery(string url)
        {
            try
            {
                var uri = new System.Uri(url);
                return uri.PathAndQuery;
            }
            catch
            {
                int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
                if (schemeEnd < 0) return url;
                int pathStart = url.IndexOf('/', schemeEnd + 3);
                if (pathStart < 0) return "/";
                return url.Substring(pathStart);
            }
        }
    }
