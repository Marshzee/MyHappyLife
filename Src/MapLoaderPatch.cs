using System;
using MelonLoader;
using HarmonyLib;
using MV.Common;

namespace KogamaOfflinePatch
{
    [HarmonyPatch(typeof(Urls), nameof(Urls.Init))]
    public static class MapLoaderPatch_Init
    {
        public static void Prefix(ref string apiUrl, ref string streamingAssetsUrl)
        {
            MelonLogger.Msg($"[MapLoaderPatch] Original URLs: API='{apiUrl}', Assets='{streamingAssetsUrl}'");
            
            apiUrl = "http://127.0.0.1:8080";
            streamingAssetsUrl = "http://127.0.0.1:8080/static";
        }
    }

    [HarmonyPatch(typeof(Urls), "get_API")]
    public static class MapLoaderPatch_GetAPI
    {
        public static bool Prefix(ref string __result)
        {
            __result = "http://127.0.0.1:8080/";
            return false;
        }
    }

    [HarmonyPatch(typeof(Urls), "get_StreamingAssets")]
    public static class MapLoaderPatch_GetAssets
    {
        public static bool Prefix(ref string __result)
        {
            __result = "http://127.0.0.1:8080/static/";
            return false; 
        }
    }
}