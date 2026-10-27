using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public enum RoomTurnLimit
{
    Unlimited = 0,
    FifteenSeconds = 15,
    ThirtySeconds = 30,
    OneMinute = 60,
    TwoMinutes = 120,
    ThreeMinutes = 180,
    FiveMinutes = 300,
    TenMinutes = 600
}

public static class RelayTransportSetup
{
#if UNITY_WEBGL && !UNITY_EDITOR
    public const string ConnectionType = "wss";
    public const bool WebSockets = true;
#else
    public const string ConnectionType = "dtls";
    public const bool WebSockets = false;
#endif

    public static void Host(Allocation allocation)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.UseWebSockets = WebSockets;
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));
    }

    public static void Client(JoinAllocation allocation)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.UseWebSockets = WebSockets;
        transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, ConnectionType));
    }
}
