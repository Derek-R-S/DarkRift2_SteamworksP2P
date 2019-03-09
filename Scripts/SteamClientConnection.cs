using System;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DarkRift;
using DarkRift.Client;
using Steamworks;

public class SteamClientConnection : NetworkClientConnection
{

    public override ConnectionState ConnectionState => Connected ? ConnectionState.Connected : ConnectionState.Disconnected;
    public override IEnumerable<IPEndPoint> RemoteEndPoints => new IPEndPoint[1] { DummyEndPoint };
    private IPEndPoint DummyEndPoint = new IPEndPoint(1, 1);
    private bool NoDelay;
    private bool Connected = false;
    private int DataSize = 1400;
    private ulong LobbyID = 0;
    private CSteamID LobbyOwner;
    private byte[] SendReliableData;
    private byte[] SendUnreliableData;
    private byte[] RecvData;
    // Callbacks
    private Callback<LobbyChatUpdate_t> m_LobbyChatUpdate;
    private Callback<LobbyEnter_t> m_LobbyEnter;

    public SteamClientConnection(ulong lobbyID, int dataSize = 1400, bool noDelay = false){
        NoDelay = noDelay;
        DataSize = dataSize;
        LobbyID = lobbyID;
        Connected = false;
        LobbyID = lobbyID;
        SendReliableData = new byte[DataSize];
        SendUnreliableData = new byte[DataSize];
        RecvData = new byte[DataSize];
        m_LobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
        m_LobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        SteamListenerUnity.Initialize(this);
    }

    public override IPEndPoint GetRemoteEndPoint(string name){
        return DummyEndPoint;
    }

    public override void Connect(){
        if(SteamManager.Initialized)
            SteamMatchmaking.JoinLobby(new CSteamID(LobbyID));
        else
            Debug.Log("Error - Steamworks is not initialized!");
    }

    public void Update(){
        uint Length;
        int Channel;
        while(PacketAvailable(out Length, out Channel)){
            CSteamID Sender;
            uint RealLength;
            if(SteamNetworking.ReadP2PPacket(RecvData, Length, out RealLength, out Sender, Channel)){
                if(Sender.m_SteamID == LobbyOwner.m_SteamID){
                    using (MessageBuffer buffer = MessageBuffer.Create((int)RealLength))
                    {
                        Buffer.BlockCopy(RecvData, 0, buffer.Buffer, buffer.Offset, (int)RealLength);
                        buffer.Count = (int)RealLength;
                        HandleMessageReceived(buffer, Channel == 1 ? SendMode.Reliable : SendMode.Unreliable);
                    }
                }
            }
        }
    }

    void OnLobbyEnter(LobbyEnter_t pCallback) {
        Debug.Log(pCallback.m_ulSteamIDLobby + " : " + pCallback.m_EChatRoomEnterResponse);
		if(pCallback.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess){
            Connected = true;
            LobbyOwner = SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyID));
            SteamNetworking.SendP2PPacket(LobbyOwner, new byte[1], 1, EP2PSend.k_EP2PSendReliable, 0);
        }
	}

    public override bool SendMessageReliable(MessageBuffer message){
        if(LobbyOwner == null)
            return false;

        if(message.Count > DataSize)
            throw new Exception("Data Size on SteamListener is too small for a message! Increase the size in the constructor.");

        Buffer.BlockCopy(message.Buffer, message.Offset, SendReliableData, 0, message.Count);
        return SteamNetworking.SendP2PPacket(LobbyOwner, SendReliableData, (uint)message.Count, NoDelay ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendReliableWithBuffering, 1);
    }

    public override bool SendMessageUnreliable(MessageBuffer message){
        if(LobbyOwner == null)
            return false;

        if(message.Count > DataSize)
            throw new Exception("Data Size on SteamListener is too small for a message! Increase the size in the constructor.");

        Buffer.BlockCopy(message.Buffer, message.Offset, SendUnreliableData, 0, message.Count);
        return SteamNetworking.SendP2PPacket(LobbyOwner, SendUnreliableData, (uint)message.Count, NoDelay ? EP2PSend.k_EP2PSendUnreliableNoDelay : EP2PSend.k_EP2PSendUnreliable, 0);
    }

    public override bool Disconnect(){
        CSteamID lobbyOwner = SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyID));
        SteamMatchmaking.LeaveLobby(new CSteamID(LobbyID));
        HandleDisconnection();
        Connected = false;
        return SteamNetworking.CloseP2PSessionWithUser(lobbyOwner);
    }

    bool PacketAvailable(out uint Length, out int Channel)
    {
        if (SteamNetworking.IsP2PPacketAvailable(out Length, 0))
        {
            Channel = 0;
            return true;
        }

        if (SteamNetworking.IsP2PPacketAvailable(out Length, 1))
        {
            Channel = 1;
            return true;
        }

        Length = 0;
        Channel = 0;
        return false;
    }

    void OnLobbyChatUpdate(LobbyChatUpdate_t pCallback) {
		if(pCallback.m_ulSteamIDLobby == LobbyID){
            if(pCallback.m_ulSteamIDUserChanged == LobbyOwner.m_SteamID){
                if(pCallback.m_rgfChatMemberStateChange != (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered)
                    Disconnect();
            }
        }
    }
}
