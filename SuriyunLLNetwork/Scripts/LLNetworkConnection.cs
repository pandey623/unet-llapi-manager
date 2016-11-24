using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class LLNetworkConnection
{
    public int ConnectionId { get; protected set; }
    public int SocketId { get; protected set; }

    public LLNetworkConnection(int connectionId, int socketId)
    {
        ConnectionId = connectionId;
        SocketId = socketId;
    }

    public virtual byte Disconnect()
    {
        byte error;
        NetworkTransport.Disconnect(SocketId, ConnectionId, out error);
        return error;
    }
}
