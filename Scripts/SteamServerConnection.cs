﻿using System.Collections;
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
    private IPEndPoint DummyEndPoint = new IPEndPoint(0, 0);
    public CSteamID RemoteID;
    private SteamListener Listener;
    private byte[] SendUnreliableData;
    private byte[] SendReliableData;

    public SteamServerConnection(ulong steamID, SteamListener listener){
        Listener = listener;
        RemoteID = new CSteamID(steamID);
        SendReliableData = new byte[Listener.DataSize];
        SendUnreliableData = new byte[Listener.DataSize];
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
            Buffer.BlockCopy(MessageData, 0, buffer.Buffer, buffer.Offset, Length);
            buffer.Count = Length;
            HandleMessageReceived(buffer, reliable ? SendMode.Reliable : SendMode.Unreliable);
        }
    }

    public override bool SendMessageReliable(MessageBuffer message)
    {
        if(message.Count > Listener.DataSize)
            throw new Exception("Data Size on SteamListener is too small for message!");

        Buffer.BlockCopy(message.Buffer, message.Offset, SendReliableData, 0, message.Count);

        return SteamNetworking.SendP2PPacket(RemoteID, SendReliableData, (uint)message.Count, Listener.NoDelay ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendReliableWithBuffering, 1);
    }

    public override bool SendMessageUnreliable(MessageBuffer message)
    {
        if(message.Count > Listener.DataSize)
            throw new Exception("Data Size on SteamListener is too small for message!");

        Buffer.BlockCopy(message.Buffer, message.Offset, SendUnreliableData, 0, message.Count);

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