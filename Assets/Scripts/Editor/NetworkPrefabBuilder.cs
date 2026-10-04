// NetworkPrefabBuilder.cs
// GoF2 > Build > Network Prefabs: the multiplayer MVP's network prefabs in Resources/GoF2Net (NetGame registers them at
// runtime): NetPlayer, NetProxy, NetState and NetCrate, each a NetworkObject with its behaviour. Existing prefabs are kept, so
// their GlobalObjectIdHash (which host and clients must agree on) stays the same. Also switches Android's internet
// permission on (Unity Transport's sockets need it; Unity doesn't detect them).

using GoF2Remake.Multiplayer;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    public static class NetworkPrefabBuilder
    {
        [MenuItem("GoF2/Build/Network Prefabs", priority = 205)]
        public static void Build()
        {
            string folder = "Assets/Resources/" + NetGame.PrefabFolder;
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources", NetGame.PrefabFolder);
            Make<NetPlayer>(folder, "NetPlayer");
            Make<NetProxy>(folder, "NetProxy");
            Make<NetState>(folder, "NetState");
            Make<NetCrate>(folder, "NetCrate");
            // A new prefab is saved with the hash its scene object had (the same for all of them); loaded as an asset it
            // computes its own (NetworkObject.OnValidate), which only reaches the file once it is saved again.
            foreach (var name in NetGame.PrefabNames)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{name}.prefab");
                if (prefab != null) EditorUtility.SetDirty(prefab.GetComponent<NetworkObject>());
            }
            PlayerSettings.Android.forceInternetPermission = true;
            AssetDatabase.SaveAssets();
        }

        static void Make<T>(string folder, string name) where T : Component
        {
            string path = $"{folder}/{name}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<T>();
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"NetworkPrefabBuilder: {path}");
        }
    }
}
