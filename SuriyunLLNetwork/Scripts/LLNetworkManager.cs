using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;

public class LLNetworkManager : MonoBehaviour
{
    public bool useWebSockets;
    public string networkAddress;
    public int networkPort;
    public QosType[] qosChannels;
    public int maxConnections = 4;
    public float stopConnectionDuration = 10f;
    protected int socketId = -1;
    protected int connectionId = -1;
    protected float countConnectingTime = 0;
    protected bool isClientConnectCalled = false;
    protected byte[] msgBuffer;
    public bool IsClientConnecting { get; protected set; }
    public bool IsClientConnected { get; protected set; }
    public bool IsServer { get; protected set; }
    public int SocketId { get { return socketId; } }
    // Connection to server id
    public int ConnectionId { get { return connectionId; } }
    public LLNetworkConnection ServerConnection { get; protected set; }
    public Dictionary<int, LLNetworkConnection> ClientConnections { get; protected set; }

    protected virtual HostTopology CreateTopology()
    {
        ConnectionConfig config = new ConnectionConfig();
        foreach (QosType qosChannel in qosChannels)
            config.AddChannel(qosChannel);
        
        return new HostTopology(config, maxConnections);
    }

    public void StartServer()
    {
        NetworkTransport.Init();
        socketId = useWebSockets ? NetworkTransport.AddWebsocketHost(CreateTopology(), networkPort) :  NetworkTransport.AddHost(CreateTopology(), networkPort);
        msgBuffer = new byte[NetworkMessage.MaxMessageSize];
        ClientConnections = new Dictionary<int, LLNetworkConnection>();
        IsServer = true;
    }

    public void Connect()
    {
        Connect(networkAddress, networkPort);
    }

    public void Connect(string address, int port)
    {
        NetworkTransport.Init();
        networkAddress = address;
        networkPort = port;
        IsClientConnecting = true;
        socketId = useWebSockets ? NetworkTransport.AddWebsocketHost(CreateTopology(), networkPort) :  NetworkTransport.AddHost(CreateTopology(), networkPort);
        msgBuffer = new byte[NetworkMessage.MaxMessageSize];
        IsServer = false;
    }

    public void SetClientConnectingStates()
    {
        IsClientConnecting = true;
        IsClientConnected = false;
        isClientConnectCalled = false;
    }

    public void SetClientConnectedStates()
    {
        IsClientConnecting = false;
        IsClientConnected = true;
        isClientConnectCalled = false;
    }

    public void SetClientDisconnectStates()
    {
        IsClientConnecting = false;
        IsClientConnected = false;
        isClientConnectCalled = false;
        socketId = -1;
        connectionId = -1;
    }

    public void StopServer()
    {
        NetworkTransport.RemoveHost(SocketId);
    }

    public void Disconnect()
    {
        if (IsServer)
            return;
        
        byte error;
        NetworkTransport.Disconnect(SocketId, ConnectionId, out error);
        NetworkTransport.RemoveHost(SocketId);
        HandleDisconnect(ConnectionId, error);
    }

    public void Reconnect()
    {
        Disconnect();
        Connect();
    }

    protected virtual void Update()
    {

        if (socketId == -1)
            return;

        byte error;

        // Update client connection
        if (IsClientConnecting && !IsClientConnected)
        {
            // Try connecting
            countConnectingTime += Time.unscaledDeltaTime;
            if (countConnectingTime > stopConnectionDuration)
            {
                // Timeout reached
                SetClientDisconnectStates();
                countConnectingTime = 0;
                return;
            }

            if (!isClientConnectCalled)
            {
                connectionId = NetworkTransport.Connect(socketId, networkAddress, networkPort, 0, out error);
                isClientConnectCalled = true;

                if (error != (int)NetworkError.Ok)
                {
                    SetClientDisconnectStates();
                    return;
                }
            }
        }

        NetworkEventType incomingNetworkEvent;
        int incomingConnectionId;
        int incomingChannelId;
        int incomingReceivedSize;
        incomingNetworkEvent = NetworkTransport.ReceiveFromHost(SocketId, out incomingConnectionId, out incomingChannelId, msgBuffer, msgBuffer.Length, out incomingReceivedSize, out error);

        switch (incomingNetworkEvent)
        {
            case NetworkEventType.ConnectEvent:
                HandleConnect(connectionId, error);
                break;
            case NetworkEventType.DataEvent:
                HandleData(connectionId, incomingChannelId, incomingReceivedSize, error);
                break;
            case NetworkEventType.DisconnectEvent:
                HandleDisconnect(connectionId, error);
                break;
            case NetworkEventType.Nothing:
                break;
        }
    }

    protected virtual void HandleConnect(int clientConnectionId, byte error)
    {
        if (IsServer)
        {
            if (ConnectionId != clientConnectionId)
            {
                LLNetworkConnection newConnection = new LLNetworkConnection(clientConnectionId, SocketId);
                ClientConnections.Add(clientConnectionId, newConnection);
                OnClientConnect(newConnection);
            }
            else
            {
                ServerConnection = new LLNetworkConnection(clientConnectionId, SocketId);
                OnServerConnect(ServerConnection);
                SetClientConnectedStates();
            }
        }
        else
        {
            if (ConnectionId != clientConnectionId)
                return;

            ServerConnection = new LLNetworkConnection(clientConnectionId, SocketId);
            OnServerConnect(ServerConnection);
            SetClientConnectedStates();
        }
    }

    protected virtual void HandleData(int clientConnectionId, int channelId, int receivedSize, byte error)
    {
        if (IsServer)
        {
            if (ConnectionId != clientConnectionId)
            {
                LLNetworkConnection connection;
                ClientConnections.TryGetValue(clientConnectionId, out connection);

                if (connection != null)
                    connection.HandleDataReceived(msgBuffer, 0);
            }
            else
            {
                if (ServerConnection == null)
                    return;

                ServerConnection.HandleDataReceived(msgBuffer, 0);
            }
        }
        else
        {
            if (ServerConnection == null)
                return;

            ServerConnection.HandleDataReceived(msgBuffer, 0);
        }
    }

    protected virtual void HandleDisconnect(int connectionId, byte error)
    {
        if (IsServer)
        {
            LLNetworkConnection connection;
            ClientConnections.TryGetValue(connectionId, out connection);

            if (connection == null)
                return;

            // Disconnect all client
            connection.Disconnect();
            OnClientDisconnect(connection);
            ClientConnections.Remove(connectionId);
            // Disconnect local client
            ServerConnection.Disconnect();
            OnServerDisconnect(ServerConnection);
            ServerConnection = null;
            SetClientDisconnectStates();
        }
        else
        {
            if (ConnectionId != connectionId)
                return;
            
            ServerConnection.Disconnect();
            OnServerDisconnect(ServerConnection);
            ServerConnection = null;
            SetClientDisconnectStates();
        }
    }

    protected virtual void OnClientConnect(LLNetworkConnection connection)
    {
        Debug.Log("OnClientConnect " + connection.ConnectionId);
    }

    protected virtual void OnServerConnect(LLNetworkConnection connection)
    {
        Debug.Log("OnServerConnect " + connection.ConnectionId);
    }

    protected virtual void OnClientDisconnect(LLNetworkConnection connection)
    {
        Debug.Log("OnClientDisconnect " + connection.ConnectionId);
    }

    protected virtual void OnServerDisconnect(LLNetworkConnection connection)
    {
        Debug.Log("OnServerDisconnect " + connection.ConnectionId);
    }
}
