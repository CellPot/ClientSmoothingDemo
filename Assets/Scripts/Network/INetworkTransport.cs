namespace Network
{
    public interface INetworkTransport
    {
        void Send(NetworkSimulator.Snapshot snapshot);
        void RegisterListener(System.Action<NetworkSimulator.Snapshot> listener);
        void UnregisterListener(System.Action<NetworkSimulator.Snapshot> listener);
    }
}