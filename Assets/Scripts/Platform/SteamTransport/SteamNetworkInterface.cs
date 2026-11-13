#if STEAMWORKS_NET
using System;
using Steamworks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Networking.Transport;

/// <summary>
/// Unity Transport over Steam: packets go to and come from Steam users through Steam's connectionless messages
/// (SteamNetworkingMessages), relayed by Steam's network, so nobody needs an open port or the other's IP address.
/// Unity Transport keeps doing its own connections and reliability on top, as it does over UDP. A Steam user is a
/// custom endpoint holding their SteamID (Endpoint, TryGetSteamId).
/// Steam is called on the main thread: the receive and send "jobs" run when they are scheduled.
/// </summary>
public class SteamNetworkInterface : INetworkInterface
{
    const int Channel = 0;
    const int SendFlags = Constants.k_nSteamNetworkingSend_UnreliableNoDelay | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession;

    readonly IntPtr[] _messages = new IntPtr[64];
    NetworkEndpoint _local;
    Callback<SteamNetworkingMessagesSessionRequest_t> _sessionRequest;

    public NetworkEndpoint LocalEndpoint => _local;

    /// <summary>The endpoint of a Steam user.</summary>
    public static NetworkEndpoint Endpoint(ulong steamId)
    {
        var bytes = new NativeArray<byte>(8, Allocator.Temp);
        for (int i = 0; i < 8; i++) bytes[i] = (byte)(steamId >> (8 * i));
        var endpoint = default(NetworkEndpoint);
        endpoint.SetRawAddressBytes(bytes, NetworkFamily.Custom);
        bytes.Dispose();
        return endpoint;
    }

    /// <summary>The SteamID an endpoint made by Endpoint holds; false for any other endpoint.</summary>
    public static bool TryGetSteamId(NetworkEndpoint endpoint, out ulong steamId)
    {
        steamId = 0;
        if (endpoint.Family != NetworkFamily.Custom) return false;
        var bytes = endpoint.GetRawAddressBytes();
        for (int i = 0; i < 8; i++) steamId |= (ulong)bytes[i] << (8 * i);
        bytes.Dispose();
        return steamId != 0;
    }

    public int Initialize(ref NetworkSettings settings, ref int packetPadding)
    {
        SteamNetworkingUtils.InitRelayNetworkAccess();
        // Anyone who has our SteamID may open a session; Unity Transport and the host's join rules decide the rest
        _sessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(request =>
        {
            var identity = request.m_identityRemote;
            SteamNetworkingMessages.AcceptSessionWithUser(ref identity);
        });
        return 0;
    }

    public int Bind(NetworkEndpoint endpoint)
    {
        _local = Endpoint(SteamUser.GetSteamID().m_SteamID);
        return 0;
    }

    public int Listen() => 0;

    public unsafe JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
    {
        dep.Complete();
        var queue = arguments.ReceiveQueue;
        while (true)
        {
            int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(Channel, _messages, _messages.Length);
            if (count <= 0) break;
            for (int i = 0; i < count; i++)
            {
                var message = SteamNetworkingMessage_t.FromIntPtr(_messages[i]);
                // A full queue drops the packet, as a full socket buffer would; Unity Transport sends it again if it matters
                if (queue.EnqueuePacket(out var packet))
                {
                    int size = Math.Min(message.m_cbSize, packet.BytesAvailableAtEnd);
                    packet.EndpointRef = Endpoint(message.m_identityPeer.GetSteamID().m_SteamID);
                    packet.AppendToPayload((void*)message.m_pData, size);
                }
                SteamNetworkingMessage_t.Release(_messages[i]);
            }
            if (count < _messages.Length) break;
        }
        return default;
    }

    public unsafe JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
    {
        dep.Complete();
        var queue = arguments.SendQueue;
        for (int i = 0; i < queue.Count; i++)
        {
            var packet = queue[i];
            if (packet.Length == 0 || !TryGetSteamId(packet.EndpointRef, out var steamId)) continue;
            var buffer = new NativeArray<byte>(packet.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var bufferPtr = (IntPtr)buffer.GetUnsafePtr();
            int length = packet.CopyPayload((void*)bufferPtr, buffer.Length);
            var identity = new SteamNetworkingIdentity();
            identity.SetSteamID(new CSteamID(steamId));
            SteamNetworkingMessages.SendMessageToUser(ref identity, bufferPtr, (uint)length, SendFlags, Channel);
            buffer.Dispose();
        }
        return default;
    }

    public void Dispose()
    {
        _sessionRequest?.Dispose();
        _sessionRequest = null;
    }
}
#endif
