using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace RainMeadow
{
    public class RelayedPeer : PeerBase
    {

        public class RelayClient : UdpClient
        {
            public PeerID ownId;
            private static int headerLength = 1 + 4 + 4;
            private IPAddress relayServerAddress;
            private int relayListenPort;
            private IPEndPoint relayServerEndPoint, localEndpoint;
            //client sends to serverPort, recieves from listenPort
            public RelayClient(IPAddress relayServerAddress, int relayServerPort, int relayListenPort) : base() {
                this.relayServerAddress = relayServerAddress;
                this.relayListenPort = relayListenPort;
                this.relayServerEndPoint = new IPEndPoint(relayServerAddress, relayServerPort);
                Init();
            }



            private void Init()
            {
                // With this set, it will be truely connectionless
                Client.IOControl(
                    (IOControlCode)(-1744830452), // SIO_UDP_CONNRESET
                    new byte[] { 0, 0, 0, 0 },
                    null
                );

                int port = relayListenPort;
                for (int i = 0; i < 16; i++)
                {
                    //16 tries
                    bool alreadyinuse = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(p => p.Port == port);
                    if (!alreadyinuse)
                        break;

                    RainMeadow.Debug($"Port {port} is already being used, incrementing...");

                    port++;
                }
                relayListenPort = port;

                localEndpoint = new IPEndPoint(IPAddress.Any, relayListenPort);
                Client.Bind(localEndpoint);


            }
            //Wrapper
            public int Send(byte[] dgram, int length, PeerID peer) {
                //construct header
                byte[] fullPayload = new byte[length + headerLength];
                MemoryStream stream = new MemoryStream(fullPayload);
                BinaryWriter writer = new BinaryWriter(stream);

                //forward
                writer.Write((byte)0x01);
                //dst
                writer.Write((int)peer);
                //src
                writer.Write((int)ownId);
                //content
                writer.Write(dgram);
                writer.Close();
                stream.Close();
                return base.Send(fullPayload, fullPayload.Length, relayServerEndPoint);

            }
            public byte[] Receive(out PeerID peer)
            {
                // it should only be the remote server but eh
                IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, this.relayListenPort);
                MemoryStream netStream = new MemoryStream(base.Receive(ref remoteEndpoint));
                var netReader = new BinaryReader(netStream);
                // parse
                // TODO: implement types
                // drop first byte(lol)
                //netReader.ReadByte();
                // get peer
                peer = (PeerID)netReader.ReadInt32();
                byte[] buf = netReader.ReadBytes(int.MaxValue);
                netReader.Close();
                netStream.Close();
                return buf;
            }
        }



        private static Queue<DelayedPacket> delayedPackets;

        public static RelayedPeer instance = new RelayedPeer();
        private static IPAddress relayServerAddress = IPAddress.Parse("66.78.40.133");
        private const int LISTEN_PORT = 18720;
        private const int SERVER_PORT = 18720;
        public RelayClient relayClient;
        public int port;

        public bool waitingForTermination;
        

        public PeerID ownId;
        override public void Send(PeerID peerId, byte[] data, int length, PacketType packetType) {
            

            if (relayClient == null || waitingForTermination)
                return;



            if (!peers.TryGetValue(peerId, out RemotePeer peerData))
            {
                RainMeadow.Debug($"Communicating with new peer {peerId}");
                peerData = new RemotePeer();
                peers[peerId] = peerData;
            }

            peerData.ticksSinceLastPacketSent = 0;

            byte[] buffer = null;
            byte[] packetData;
            switch (packetType)
            {
                case PacketType.Reliable:
                    peerData.latestOutgoingPacket = new SequencedPacket(++peerData.packetIndex, data, length);
                    peerData.outgoingPackets.Enqueue(peerData.latestOutgoingPacket);

                    SequencedPacket outgoingPacket = peerData.outgoingPackets.Peek();
                    buffer = outgoingPacket.packet;
                    break;

                case PacketType.Unreliable:
                    buffer = new byte[1 + length];
                    MemoryStream stream = new MemoryStream(buffer);
                    BinaryWriter writer = new BinaryWriter(stream);

                    WriteUnreliableHeader(writer);
                    writer.Write(data, 0, length);

                    break;

                case PacketType.UnreliableOrdered:
                    buffer = new byte[9 + length];
                    stream = new MemoryStream(9 + length);
                    writer = new BinaryWriter(stream);

                    WriteUnreliableOrderedHeader(writer, ++peerData.unreliablePacketIndex);
                    writer.Write(data, 0, length);
                    break;
                default: throw new ArgumentException("UNHANDLED PACKETTYPE");
            }

            if (buffer == null)
                return;

            Send(buffer, peerId);
        }
        override public void Startup()
        {
            if (relayClient != null)
                return;

            // Create udp client for local connection
            relayClient = new RelayClient(relayServerAddress, SERVER_PORT, LISTEN_PORT);


            peers = new Dictionary<PeerID, RemotePeer>();
            delayedPackets = new Queue<DelayedPacket>();

            //
        }


        
        

        //
        private class DelayedPacket
        {
            private DateTime timeToSend;
            private PeerID destination;
            public byte[] packet;

            public bool willSend => DateTime.Now > timeToSend;

            public DelayedPacket(PeerID destination, byte[] data, TimeSpan delay)
            {
                this.destination = destination;
                this.packet = data;
                timeToSend = DateTime.Now + delay;
            }

            public void Send()
            {
                // i dont like this
                instance.relayClient.Send(packet, packet.Length, destination);
                //RainMeadow.Debug("Sent: " + packet.Length);
            }
        }

        override public void Shutdown()
        {
            RainMeadow.DebugMe();
            SendTermination();
        }

        override protected void CleanUp()
        {
            if (!relayClient.Client.Connected || peers.Any(peer => peer.Value.outgoingPackets.Count > 0))
                return;

            RainMeadow.DebugMe();
            relayClient.Client.Shutdown(SocketShutdown.Both);
            relayClient = null;
            peers = null;
            delayedPackets = null;
            waitingForTermination = false;
        }
        override public void Update()
        {
            if (relayClient == null)
                return;

            while (delayedPackets.Count > 0 && delayedPackets.Peek().willSend)
            {
                delayedPackets.Dequeue().Send();
            }

            List<PeerID> timedoutEndpoints = new List<PeerID>();
            foreach (var peer in peers)
            {
                var peerIP = peer.Key;
                var peerData = peer.Value;

                peerData.ticksSinceLastPacketSent++;
                if (peerData.ticksSinceLastPacketSent > HEARTBEAT_TICKS)
                {
                    // Send to heartbeat, do not need an acknowledge if remote is doing the same
                    Send(peerIP, new byte[0], 0, PacketType.Unreliable);
                }

                peerData.ticksSinceLastPacketReceived++;
                if (peerData.ticksSinceLastPacketReceived > TIMEOUT_TICKS)
                {
                    // Peer timed out and assume disconnected
                    RainMeadow.Debug($"Peer {peerIP} timed out :c");
                    timedoutEndpoints.Add(peerIP);
                    continue;
                }

                // Try to resend packets that have not been acknowledge on the other end
                if (peerData.outgoingPackets.Count > 0)
                {
                    peerData.ticksToResend--;
                    if (peerData.ticksToResend <= 0)
                    {
                        SequencedPacket outgoingPacket = peerData.outgoingPackets.Peek();
                        byte[] packetData = outgoingPacket.packet;

                        RainMeadow.Debug($"Resending packet #{outgoingPacket.index}");
                        Send(packetData, peerIP);

                        outgoingPacket.attemptsLeft--;
                        if (outgoingPacket.attemptsLeft == 0)
                            peerData.outgoingPackets.Dequeue().OnFailed?.Invoke();

                        peerData.ticksToResend = RESEND_TICKS;
                    }
                }
            }

            foreach (PeerID endPoint in timedoutEndpoints)
            {
                peers.Remove(endPoint);
            }
        }
        override public bool Read(out BinaryReader netReader, out PeerID peerId)
        {

            MemoryStream netStream = new MemoryStream(relayClient.Receive(out peerId));
            netReader = new BinaryReader(netStream);

            PacketType type = (PacketType)netReader.ReadByte();
            //RainMeadow.Debug("Got packet meta-type: " + type);

            if (!peers.TryGetValue(peerId, out RemotePeer peerData))
            {
                RainMeadow.Debug($"Communicating with new peer {peerId}");
                peerData = new RemotePeer();
                peers[peerId] = peerData;
            }

            //if (simulatedLoss > 0)
            //{
            //    if (simulatedLoss > random.NextDouble() || (peerData.loss && simulatedChainLoss > random.NextDouble()))
            //    {
            //        // packet loss
            //        peerData.loss = true;
            //        return false;
            //    }
            //    peerData.loss = false;
            //}
            peerData.loss = false;

            peerData.ticksSinceLastPacketReceived = 0;

            ulong receivedPacketIndex;
            switch (type)
            {
                case PacketType.Reliable:
                    receivedPacketIndex = ReadReliableHeader(netReader);
                    SendAcknowledge(peerId, receivedPacketIndex); // Return Message

                    // Process data if it is new
                    if (receivedPacketIndex > peerData.lastAckedPacketIndex)
                    {
                        peerData.lastAckedPacketIndex = receivedPacketIndex;
                        return true;
                    }
                    break;

                case PacketType.Acknowledge:
                    if (peerData.outgoingPackets.Count == 0)
                        // Nothing left to acknowledge
                        return false;

                    receivedPacketIndex = ReadAcknowledge(netReader);
                    if (peerData.outgoingPackets.Peek().index == receivedPacketIndex)
                    {
                        SequencedPacket acknowledgedPacket = peerData.outgoingPackets.Dequeue();
                        acknowledgedPacket.OnAcknowledged?.Invoke();

                        if (acknowledgedPacket == peerData.latestOutgoingPacket)
                        {
                            peerData.latestOutgoingPacket = null;
                        }

                        // Attempt to send the next reliable one if any
                        if (peerData.outgoingPackets.Count > 0)
                        {
                            byte[] packetData = peerData.outgoingPackets.Peek().packet;
                            Send(packetData, peerId);
                            peerData.ticksToResend = RESEND_TICKS;
                        }
                    }
                    break;

                case PacketType.Unreliable:
                    return true;

                case PacketType.UnreliableOrdered:
                    receivedPacketIndex = ReadUnreliableOrderedHeader(netReader);

                    // Process data if it is latest
                    if (receivedPacketIndex > peerData.lastUnreliablePacketIndex)
                    {
                        peerData.lastUnreliablePacketIndex = receivedPacketIndex;
                        return true;
                    }
                    break;

                case PacketType.Termination:
                    receivedPacketIndex = ReadTerminationHeader(netReader);
                    SendAcknowledge(peerId, receivedPacketIndex); // Return Message

                    // Do not need to check for order if peer really wants to leave now
                    peers.Remove(peerId);
                    RainMeadow.Debug($"Peer {peerId} terminated connection");
                    return false;

                case PacketType.Heartbeat:
                    break; // Do nothing
            }

            return false;
        }
        override public bool IsPacketAvailable()
        {
            return relayClient != null && relayClient.Available > 0;
        }
        override protected void Send(byte[] packet, PeerID peerId)
        {
            //if (simulatedLatency > 0)
            //{
            //    delayedPackets.Enqueue(new DelayedPacket(endPoint, packet, TimeSpan.FromMilliseconds(simulatedLatency + simulatedJitter * Mathf.Pow((float)random.NextDouble(), simulatedJitterPower))));
            //}
            //else
            //{
                relayClient.Send(packet, packet.Length, peerId);
                //RainMeadow.Debug("sent: " + packet.Length);
            //}
        }
        override protected void SendAcknowledge(PeerID peerId, ulong index)
        {
            RainMeadow.Debug($"Sending acknowledge for packet #{index}");

            byte[] buffer = new byte[9];
            MemoryStream stream = new MemoryStream(buffer);
            BinaryWriter writer = new BinaryWriter(stream);

            WriteAcknowledge(writer, index);
            Send(buffer, peerId);
        }
    }
}
