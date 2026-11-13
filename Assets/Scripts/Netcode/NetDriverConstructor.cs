using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;

/// <summary>
/// The network drivers a world gets (set by NetBootstrap):
/// - A host: UDP while NetSession.Listen (development, join by IP) or when there's no online platform, and the platform's
///   own driver when it hosts online (Steam: SteamNetworkInterface). NetStartSystem listens on all of them.
/// - A client: the platform's driver when it joins through the platform (NetSession.ConnectTo is a SteamID), else UDP.
/// </summary>
public class NetDriverConstructor : INetworkStreamDriverConstructor
{
    public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
    {
#if STEAMWORKS_NET
        if (SteamNetworkInterface.TryGetSteamId(NetSession.ConnectTo, out _) && Platform.Current.HostsOnline)
        {
            var steam = DefaultDriverBuilder.CreateClientNetworkDriver(new SteamNetworkInterface(), DefaultDriverBuilder.GetNetworkClientSettings());
            driverStore.RegisterDriver(TransportType.Socket, steam);
            return;
        }
#endif
        DefaultDriverBuilder.RegisterClientDriver(world, ref driverStore, netDebug);
    }

    public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug)
    {
        bool online = Platform.Current.HostsOnline;
        if (NetSession.Listen || !online) DefaultDriverBuilder.RegisterServerDriver(world, ref driverStore, netDebug);
#if STEAMWORKS_NET
        if (online)
        {
            var steam = DefaultDriverBuilder.CreateServerNetworkDriver(new SteamNetworkInterface(), DefaultDriverBuilder.GetNetworkServerSettings());
            driverStore.RegisterDriver(TransportType.Socket, steam);
        }
#endif
    }
}
