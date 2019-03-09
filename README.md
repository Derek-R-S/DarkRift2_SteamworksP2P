# DarkRift2 - Steamworks P2P listener
A listener that enables communication via steam's P2P networking system.

## Features
- Uses Steam's Relay, no more NAT problems.
- Steam's matchmaking, this uses lobbies so matchmaking is easy to setup.

## Notes

If Steamworks is not initialized, it will fail to create/join a lobby.

Demo Repository for this listener is coming soon.

This does not support dedicated servers at the moment, I am planning on making another listener using their new socket system which will support dedicated servers.

PR are always welcome! ☺

## Prerequisites
- [DarkRift 2](https://assetstore.unity.com/packages/tools/network/darkrift-networking-2-95309)
- [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET/releases)

**DarkRift2 Setup**

DarkRift2 has a bug where it does not find plugins by default in Unity.

To fix this, open the script __Assets/DarkRift/DarkRift/Plugins/Server/UnityServerHelper.cs__
Change line 40 from
```c#
if (type.IsSubclassOf(typeof(Plugin)) && !type.IsAbstract)
```
to
```c#
if (type.IsSubclassOf(typeof(PluginBase)) && !type.IsAbstract)
```

Another DarkRift2 bug is that it will not properly dispose of clients unless you have a callback on your XmlUnityServer.Server.ClientManager.ClientDisconnected. Simply adding the following line to where you have your server will fix this issue.
```c#
GetComponent<XmlUnityServer>().Server.ClientManager.ClientDisconnected += (s, c) => Debug.Log("Client Disconnected.");
```


## Listener Config

All of the settings in the listener have default values, which means you can start it without any settings and it will work fine.

```xml
<listener name="Steamworks Listener" type="SteamListener" />
```

Settings:
- NoDelay - Should it send messages instantly? *Default: False*
- MaxUsers - How many users can the lobby hold. *Default: 10*
- LobbyType - Used for steam matchmaking. Choices: *Friends*, *Invisible*, *Private*, *Public*. *Default: Public*
- DataSize - Used for allocating byte arrays. Change if you send big packets. *Default: 1400*

```xml
<listener name="Steamworks Listener" type="SteamListener">
  <settings 
    NoDelay="false"
    MaxUsers="10"
    LobbyType="Public"
    DataSize="1400" />
</listener>
```

## Client Connecting

To connect a client you will need a lobby ID of the match you want to join. You can obtain one from the steam matchmaking service. The lobbyID is in the type of a ulong (uint64)

Then to connect you need to call
```c#
GetComponent<UnityClient>().Client.ConnectInBackground(new SteamClientConnection(LOBBYIDHERE));
```

Optionally there are also settings for the client like the server.
You can change the size of the allocated byte arrays and NoDelay option.
Example:
```c#
GetComponent<UnityClient>().Client.ConnectInBackground(new SteamClientConnection(LOBBYIDHERE, 1400, false));
```

The listener will automatically join the steam lobby so no need to join it before hand and it will also handle leaving the lobby when you call Disconnect.