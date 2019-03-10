using UnityEngine;
using LogType = DarkRift.LogType;
using DarkRift;
using DarkRift.Server;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Steamworks;
using Version = System.Version;

public class SteamListener : NetworkListener
{

    enum LobbyTypes { Friends = 1, Invisible = 3, Private = 0, Public = 2 }

    public override Version Version => new Version(0, 1, 1);

    public bool NoDelay;
    public static ulong LobbyID = 0;
    public int DataSize = 1400;
    private int MaxUsers = 10;
    private LobbyTypes LobbyType;
    public CSteamID CLobbyID;
    private List<SteamServerConnection> ConnectedUsers;
    private byte[] RecvData;
    // So the currentEndPointCount is a tad bit jank solution to getting data.
    // Theres no way (as of now) to get any extra data from an IClient
    // so we use fake endpoints which correlate to data, such as steamIDs. :P
    private int currentEndPointCount;

    // Calbacks
    private CallResult<LobbyCreated_t> OnLobbyCreatedCallResult;
    private Callback<LobbyChatUpdate_t> m_LobbyChatUpdate;
    private Callback<P2PSessionRequest_t> m_P2PSessionRequest;
    private Callback<P2PSessionConnectFail_t> m_P2PSessionConnectFail;

    public SteamListener(NetworkListenerLoadData Data) : base(Data)
	{
        NoDelay = bool.TryParse(Data.Settings["NoDelay"], out NoDelay) ? NoDelay : false;
        MaxUsers = int.TryParse(Data.Settings["MaxUsers"], out MaxUsers) ? MaxUsers : 10;
        LobbyType = Enum.TryParse(Data.Settings["LobbyType"], out LobbyType) ? LobbyType : LobbyTypes.Public;
        DataSize = int.TryParse(Data.Settings["DataSize"], out DataSize) ? DataSize : 1400; // The size of our byte arrays, increase to your needs.
        RecvData = new byte[DataSize];
        ConnectedUsers = new List<SteamServerConnection>();
        OnLobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        m_LobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
        m_P2PSessionRequest = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
        currentEndPointCount = 0;
	}

    public override void StartListening(){
        if(SteamManager.Initialized){
            SteamListenerUnity.Initialize(this);
            WriteEvent($"Starting a {LobbyType} lobby with {MaxUsers} slots and NoDelay: {NoDelay}.", LogType.Info);
            SteamAPICall_t Handle = SteamMatchmaking.CreateLobby((ELobbyType)LobbyType, MaxUsers);
            OnLobbyCreatedCallResult.Set(Handle);
        }else{
            WriteEvent("Steamworks is not Initialized.", LogType.Info);
        }
    }

    // Lobby Started
    void OnLobbyCreated(LobbyCreated_t pCallback, bool bIOFailure) {
		switch(pCallback.m_eResult){
            case(EResult.k_EResultOK):
                WriteEvent($"Lobby Created Successfully. LobbyID: {pCallback.m_ulSteamIDLobby}", LogType.Info);
                break;
            case(EResult.k_EResultFail):
                WriteEvent("Failed Creating Lobby.", LogType.Info);
                break;
            case(EResult.k_EResultTimeout):
                WriteEvent("Lobby Request Timed Out.", LogType.Info);
                break;
            case(EResult.k_EResultRateLimitExceeded):
                WriteEvent("Lobby was Rate-Limited.", LogType.Info);
                break;
            case(EResult.k_EResultAccessDenied):
                WriteEvent("Game Does not support lobbies or client is not allowed to.", LogType.Info);
                break;
            case(EResult.k_EResultNoConnection):
                WriteEvent("Client does not have connection to steam's backend.", LogType.Info);
                break;
        }

        LobbyID = pCallback.m_ulSteamIDLobby;
        CLobbyID = new CSteamID(LobbyID);
	}

    // User Joined/Left
    void OnLobbyChatUpdate(LobbyChatUpdate_t pCallback) {
		if(pCallback.m_ulSteamIDLobby == LobbyID){
            switch(pCallback.m_rgfChatMemberStateChange){
                case((uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected):
                case((uint)EChatMemberStateChange.k_EChatMemberStateChangeKicked):
                case((uint)EChatMemberStateChange.k_EChatMemberStateChangeBanned):
                case((uint)EChatMemberStateChange.k_EChatMemberStateChangeLeft):
                    SteamNetworking.CloseP2PSessionWithUser(new CSteamID(pCallback.m_ulSteamIDUserChanged));
                    SteamServerConnection ClientConnection = ConnectedUsers.Find(x => x.RemoteID.m_SteamID == pCallback.m_ulSteamIDUserChanged);

                    if(ClientConnection != null){
                        ClientConnection.UserDisconnected();
                        UnregisterUser(ClientConnection);
                    }

                    break;
            }
        }
    }

    // User Started Communication
    void OnP2PSessionRequest(P2PSessionRequest_t pCallback) {
		for(int i = 0; i < SteamMatchmaking.GetNumLobbyMembers(CLobbyID); i++){
            if(SteamMatchmaking.GetLobbyMemberByIndex(CLobbyID, i).m_SteamID == pCallback.m_steamIDRemote.m_SteamID){
                if(SteamNetworking.AcceptP2PSessionWithUser(pCallback.m_steamIDRemote)){
                    WriteEvent($"Accepted P2P Connection With User: {pCallback.m_steamIDRemote.m_SteamID}", LogType.Info);
                    long fakeAddress = 0;
                    SteamServerConnection NewClient = new SteamServerConnection(pCallback.m_steamIDRemote.m_SteamID, this, Interlocked.Increment(ref currentEndPointCount), out fakeAddress);
                    SteamMatchmaking.SetLobbyData(CLobbyID, fakeAddress.ToString(), pCallback.m_steamIDRemote.m_SteamID.ToString());
                    RegisterUser(NewClient);
                    RegisterConnection(NewClient);
                }
                else
                    WriteEvent($"Failed P2P Connection With User: {pCallback.m_steamIDRemote.m_SteamID}", LogType.Info);
                return;
            }
        }
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

    public void Update(){
        uint Length;
        int Channel;
        while(PacketAvailable(out Length, out Channel)){
            CSteamID UserID;
            uint RealLength;
            if(SteamNetworking.ReadP2PPacket(RecvData, Length, out RealLength, out UserID, Channel)){
                SteamServerConnection ClientConnection = ConnectedUsers.Find(x => x.RemoteID.m_SteamID == UserID.m_SteamID);

                if(ClientConnection != null)
                    ClientConnection.ProcessData(RecvData, (int)RealLength, Channel != 0);
            }
        }
    }

    public void RegisterUser(SteamServerConnection user){
        lock(ConnectedUsers)
            ConnectedUsers.Add(user);
    }

    public void UnregisterUser(SteamServerConnection user){
        lock(ConnectedUsers)
            ConnectedUsers.Remove(user);
    }

    public static ulong GetUsersSteamID(IClient client){
        ulong steamID = ulong.TryParse(SteamMatchmaking.GetLobbyData(new CSteamID(LobbyID), client.GetRemoteEndPoint("").Address.Address.ToString()), out steamID) ? steamID : 0;
        return steamID;
    }
}

public class SteamListenerUnity : MonoBehaviour {

    public static SteamListenerUnity Instance;
    private SteamListener Listener;
    private SteamClientConnection Connection;

    void Awake(){
        if(Instance == null){
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else{
            Destroy(this);
        }
    }

    void Update(){
        Listener?.Update();
        Connection?.Update();
    }

    public static void Initialize(SteamListener listener){
        if(Instance == null){
            Instance = new GameObject("Steam Listener").AddComponent<SteamListenerUnity>();
        }
        
        Instance.Listener = listener;
    }

    public static void Initialize(SteamClientConnection connection){
        if(Instance == null){
            Instance = new GameObject("Steam Listener").AddComponent<SteamListenerUnity>();
        }
        
        Instance.Connection = connection;
    }
}