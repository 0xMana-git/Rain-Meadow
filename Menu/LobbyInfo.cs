using Steamworks;
using System.Net;

namespace RainMeadow
{
    // trimmed down version for listing lobbies in menus
    public class LobbyInfo
    {
        public CSteamID id;
        public string name;
        public string mode;
        public int playerCount;
        public bool hasPassword;
        public int maxPlayerCount;

        public IPEndPoint? ipEndpoint;
        public PeerBase.PeerID peerId;

        public LobbyInfo(CSteamID id, string name, string mode, int playerCount, bool hasPassword, int? maxPlayerCount)
        {
            this.id = id;
            this.peerId = default;
            this.name = name;
            this.mode = mode;
            this.playerCount = playerCount;
            this.hasPassword = hasPassword;
            this.maxPlayerCount = (int)maxPlayerCount;
        }

        public LobbyInfo(IPEndPoint ipEndpoint, string name, string mode, int playerCount, bool hasPassword, int? maxPlayerCount)
        {
            this.ipEndpoint = ipEndpoint;

            this.id = default;
            this.peerId = default;
            this.name = name;
            this.mode = mode;
            this.playerCount = playerCount;
            this.hasPassword = hasPassword;
            this.maxPlayerCount = (int)maxPlayerCount;
        }
        public LobbyInfo(PeerBase.PeerID peerId, string name, string mode, int playerCount, bool hasPassword, int? maxPlayerCount)
        {
            this.peerId = peerId;

            this.id = default;
            this.name = name;
            this.mode = mode;
            this.playerCount = playerCount;
            this.hasPassword = hasPassword;
            this.maxPlayerCount = (int)maxPlayerCount;
        }
    }
}
