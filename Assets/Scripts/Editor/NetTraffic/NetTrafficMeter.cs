// NetTrafficMeter.cs
// Editor-only: what a multiplayer session sends and receives, per message (docs/notes/multiplayer-sync.md "Bandwidth").
// Hooks Netcode's message pipeline (INetworkHooks: internal to Netcode, reached through this assembly's name, one that
// Netcode's InternalsVisibleTo lists) and counts every message the Editor's game sends or receives: a NetworkVariable delta
// by its component (NetProxy, NetPlayer...), an RPC by its component and method, anything else by Netcode's message type;
// and the batches (one per client and tick and delivery, Netcode's own header included). Netcode's payloads only: Unity
// Transport's and Relay's headers come on top (about 30-40 bytes a packet).
// Use it from an eval while the Editor hosts (or joins) a session:
//   GoF2Remake.EditorTools.NetTrafficMeter.Start();   ... later:   NetTrafficMeter.Report()   (the table, also logged)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetTrafficMeter
    {
        sealed class Row
        {
            public long messages, bytes;
        }

        static readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();
        static Hooks hooks;
        static NetworkManager hooked;
        static float startedAt;
        static long batchesOut, batchBytesOut, batchesIn, batchBytesIn;
        static readonly HashSet<ulong> peers = new HashSet<ulong>();

        /// <summary>Counting from now (the counts start over).</summary>
        public static string Start()
        {
            Stop();
            var manager = NetworkManager.Singleton;
            if (manager == null || manager.MessageManager == null) return "No session.";
            rows.Clear();
            peers.Clear();
            batchesOut = batchBytesOut = batchesIn = batchBytesIn = 0;
            startedAt = Time.realtimeSinceStartup;
            hooks = new Hooks();
            manager.MessageManager.Hook(hooks);
            hooked = manager;
            return "Counting.";
        }

        public static void Stop()
        {
            if (hooked != null && hooks != null && hooked.MessageManager != null) hooked.MessageManager.Unhook(hooks);
            hooked = null;
            hooks = null;
        }

        public static float Seconds => hooks != null ? Time.realtimeSinceStartup - startedAt : 0f;

        /// <summary>The table since Start: per message kind and direction, the count, rate and bytes; logged too.</summary>
        public static string Report(int top = 60, string appendTo = null)
        {
            float s = Mathf.Max(0.001f, Seconds);
            var sb = new StringBuilder();
            sb.AppendLine($"NetTrafficMeter: {s:0.0} s, peers {string.Join(",", peers)}");
            sb.AppendLine($"  batches out {batchesOut} ({batchesOut / s:0.0}/s) {batchBytesOut} B = {batchBytesOut / s:0} B/s;"
                          + $" in {batchesIn} ({batchesIn / s:0.0}/s) {batchBytesIn} B = {batchBytesIn / s:0} B/s");
            sb.AppendLine("  dir  msgs/s     B/s   avg B  message");
            foreach (var pair in rows.OrderByDescending(p => p.Value.bytes).Take(top))
            {
                var r = pair.Value;
                sb.AppendLine($"  {pair.Key.Substring(0, 3)} {r.messages / s,7:0.0} {r.bytes / s,7:0} {(r.messages > 0 ? r.bytes / (float)r.messages : 0f),7:0.0}  {pair.Key.Substring(4)}");
            }
            string text = sb.ToString();
            Debug.Log(text);
            if (!string.IsNullOrEmpty(appendTo)) System.IO.File.AppendAllText(appendTo, text);
            return text;
        }

        /// <summary>The totals since Start as "out B/s, in B/s" (batches, Netcode's headers included).</summary>
        public static Vector2 Rates
        {
            get
            {
                float s = Mathf.Max(0.001f, Seconds);
                return new Vector2(batchBytesOut / s, batchBytesIn / s);
            }
        }

        static void Count(bool outgoing, string label, int bytes)
        {
            string key = (outgoing ? "out " : "in  ") + label;
            if (!rows.TryGetValue(key, out var row)) rows[key] = row = new Row();
            row.messages++;
            row.bytes += bytes;
        }

        static string BehaviourName(NetworkManager manager, ulong objectId, ushort index)
        {
            if (manager == null || !manager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var obj) || obj == null) return "?";
            var b = obj.GetNetworkBehaviourAtOrderIndex(index);
            return b != null ? b.GetType().Name : "?";
        }

        static string RpcName(NetworkManager manager, RpcMetadata meta)
        {
            if (manager == null || !manager.SpawnManager.SpawnedObjects.TryGetValue(meta.NetworkObjectId, out var obj) || obj == null)
                return "?";
            var b = obj.GetNetworkBehaviourAtOrderIndex(meta.NetworkBehaviourId);
            if (b == null) return "?";
            var type = b.GetType();
            string method = RpcNames(type).TryGetValue(meta.NetworkRpcMethodId, out var n) ? n : meta.NetworkRpcMethodId.ToString();
            return $"{type.Name}.{method}";
        }

        static readonly Dictionary<Type, Dictionary<uint, string>> rpcNames = new Dictionary<Type, Dictionary<uint, string>>();

        /// <summary>A component's RPC ids by name: Netcode's codegen hashes "Module / Cecil's full name" (CodeGenHelpers.Hash;
        /// its own name table needs the Multiplayer Tools package).</summary>
        static Dictionary<uint, string> RpcNames(Type type)
        {
            if (rpcNames.TryGetValue(type, out var table)) return table;
            rpcNames[type] = table = new Dictionary<uint, string>();
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(NetworkBehaviour); t = t.BaseType)
                foreach (var m in t.GetMethods(all))
                {
                    if (!m.Name.EndsWith("Rpc")) continue;
                    string args = string.Join(",", m.GetParameters().Select(p => CecilName(p.ParameterType)));
                    string sig = $"{t.Assembly.GetName().Name}.dll / {CecilName(m.ReturnType)} {CecilName(t)}::{m.Name}({args})";
                    table[XXHash.Hash32(sig)] = m.Name;
                }
            return table;
        }

        static string CecilName(Type t)
        {
            if (t.IsByRef) return CecilName(t.GetElementType()) + "&";
            if (t.IsArray) return CecilName(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                string def = t.GetGenericTypeDefinition().FullName;
                return def + "<" + string.Join(",", t.GetGenericArguments().Select(CecilName)) + ">";
            }
            return (t.FullName ?? t.Name).Replace('+', '/');
        }

        static string Label<T>(ref T message) where T : INetworkMessage
        {
            var manager = hooked;
            switch (message)
            {
                case NetworkVariableDeltaMessage d:
                    return "NV " + (d.NetworkBehaviour != null ? d.NetworkBehaviour.GetType().Name : BehaviourName(manager, d.NetworkObjectId, d.NetworkBehaviourIndex));
                case RpcMessage r: return "RPC " + RpcName(manager, r.Metadata);
                case ServerRpcMessage r: return "RPC " + RpcName(manager, r.Metadata);
                case ClientRpcMessage r: return "RPC " + RpcName(manager, r.Metadata);
                case ProxyMessage p: return "RPC(relay) " + RpcName(manager, p.WrappedMessage.Metadata);
                default: return typeof(T).Name;
            }
        }

        sealed class Hooks : INetworkHooks
        {
            int pendingSize = -1;

            public void OnBeforeSendMessage<T>(ulong clientId, ref T message, NetworkDelivery delivery) where T : INetworkMessage { }

            public void OnAfterSendMessage<T>(ulong clientId, ref T message, NetworkDelivery delivery, int messageSizeBytes) where T : INetworkMessage
            {
                peers.Add(clientId);
                Count(true, Label(ref message), messageSizeBytes);
            }

            public void OnBeforeReceiveMessage(ulong senderId, Type messageType, int messageSizeBytes) => pendingSize = messageSizeBytes;
            public void OnAfterReceiveMessage(ulong senderId, Type messageType, int messageSizeBytes) { }

            public void OnBeforeSendBatch(ulong clientId, int messageCount, int batchSizeInBytes, NetworkDelivery delivery) { }

            public void OnAfterSendBatch(ulong clientId, int messageCount, int batchSizeInBytes, NetworkDelivery delivery)
            {
                batchesOut++;
                batchBytesOut += batchSizeInBytes;
            }

            public void OnBeforeReceiveBatch(ulong senderId, int messageCount, int batchSizeInBytes)
            {
                batchesIn++;
                batchBytesIn += batchSizeInBytes;
            }

            public void OnAfterReceiveBatch(ulong senderId, int messageCount, int batchSizeInBytes) { }
            public bool OnVerifyCanSend(ulong destinationId, Type messageType, NetworkDelivery delivery) => true;
            public bool OnVerifyCanReceive(ulong senderId, Type messageType, FastBufferReader messageContent, ref NetworkContext context) => true;

            public void OnBeforeHandleMessage<T>(ref T message, ref NetworkContext context) where T : INetworkMessage
            {
                peers.Add(context.SenderId);
                Count(false, Label(ref message), pendingSize >= 0 ? pendingSize : (int)context.MessageSize);
                pendingSize = -1;
            }

            public void OnAfterHandleMessage<T>(ref T message, ref NetworkContext context) where T : INetworkMessage { }
        }
    }
}
