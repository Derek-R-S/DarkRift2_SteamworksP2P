using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using DarkRift;
using DarkRift.Server;
using System.Net;
using Steamworks;

public class SteamServerConnection : NetworkServerConnection
{
    public override ConnectionState ConnectionState => LobbyHasUser() ? ConnectionState.Connected : ConnectionState.Disconnected;
    public override IEnumerable<IPEndPoint> RemoteEndPoints => new IPEndPoint[1] { DummyEndPoint };
    private IPEndPoint DummyEndPoint;
    public CSteamID RemoteID;
    private SteamListener Listener;
    private byte[] SendUnreliableData;
    private byte[] SendReliableData;

    public SteamServerConnection(ulong steamID, SteamListener listener, int endPointCount, out long userAddress){
        Listener = listener;
        RemoteID = new CSteamID(steamID);
        SendReliableData = new byte[Listener.DataSize];
        SendUnreliableData = new byte[Listener.DataSize];

        byte[] fakeAddress = new byte[4];
        fakeAddress[0] = (byte)(endPointCount > 255 ? 255 : endPointCount);
        endPointCount -= fakeAddress[0];
        fakeAddress[1] = (byte)(endPointCount > 255 ? 255 : endPointCount);
        endPointCount -= fakeAddress[1];
        fakeAddress[2] = (byte)(endPointCount > 255 ? 255 : endPointCount);
        endPointCount -= fakeAddress[2];
        fakeAddress[3] = (byte)(endPointCount > 255 ? 255 : endPointCount);
        endPointCount -= fakeAddress[3];

        DummyEndPoint = new IPEndPoint(new IPAddress(fakeAddress), 0);
        userAddress = DummyEndPoint.Address.Address;
    }

    bool LobbyHasUser(){
        for(int i = 0; i < SteamMatchmaking.GetNumLobbyMembers(Listener.CLobbyID); i++){
            if(SteamMatchmaking.GetLobbyMemberByIndex(Listener.CLobbyID, i).m_SteamID == RemoteID.m_SteamID)
                return true;
        }
        return false;
    }

    public override IPEndPoint GetRemoteEndPoint(string name){
        return DummyEndPoint;
    }

    public override void StartListening(){ }

    public void ProcessData(byte[] MessageData, int Length, bool reliable)
    {
        using (MessageBuffer buffer = MessageBuffer.Create(Length))
        {
            Buffer.BlockCopy(MessageData, 0, buffer.Buffer, 0, Length);
            buffer.Count = Length;
            HandleMessageReceived(buffer, reliable ? SendMode.Reliable : SendMode.Unreliable);
        }
    }

    public override bool SendMessageReliable(MessageBuffer message)
    {
        if(message.Count > Listener.DataSize)
            throw new Exception("Data Size on SteamListener is too small for message!");

        Buffer.BlockCopy(message.Buffer, 0, SendReliableData, 0, message.Count);

        return SteamNetworking.SendP2PPacket(RemoteID, SendReliableData, (uint)message.Count, Listener.NoDelay ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendReliableWithBuffering, 1);
    }

    public override bool SendMessageUnreliable(MessageBuffer message)
    {
        if(message.Count > Listener.DataSize)
            throw new Exception("Data Size on SteamListener is too small for message!");

        Buffer.BlockCopy(message.Buffer, 0, SendUnreliableData, 0, message.Count);

        return SteamNetworking.SendP2PPacket(RemoteID, SendUnreliableData, (uint)message.Count, Listener.NoDelay ? EP2PSend.k_EP2PSendUnreliableNoDelay : EP2PSend.k_EP2PSendUnreliable, 0);
    }

    public override bool Disconnect()
    {
        return SteamNetworking.CloseP2PSessionWithUser(RemoteID);
    }

    public void UserDisconnected(){
        HandleDisconnection();
    }
}
