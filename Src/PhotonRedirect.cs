using MelonLoader;
using HarmonyLib;
using ExitGames.Client.Photon;

namespace KogamaOfflinePatch
{
    [HarmonyPatch(typeof(PhotonPeer), "Connect", new[] { typeof(string), typeof(string) })]
    public static class PhotonRedirect_Connect2
    {
        public static void Prefix(ref string serverAddress, ref string applicationName)
        {
            MelonLogger.Msg($"[PhotonRedirect] Redirecting Photon (2 args): {serverAddress} -> 127.0.0.1");
            serverAddress = "127.0.0.1";
            applicationName = "any";
        }
    }

    [HarmonyPatch(typeof(PhotonPeer), "Connect", new[] { typeof(string), typeof(string), typeof(object) })]
    public static class PhotonRedirect_Connect3
    {
        public static void Prefix(ref string serverAddress, ref string applicationName)
        {
            MelonLogger.Msg($"[PhotonRedirect] Redirecting Photon (3 args): {serverAddress} -> 127.0.0.1");
            serverAddress = "127.0.0.1";
            applicationName = "any";
        }
    }
}