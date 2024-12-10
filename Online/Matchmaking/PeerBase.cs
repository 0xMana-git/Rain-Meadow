using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using static RainMeadow.Serializer;

namespace RainMeadow
{

    //P2P connection base class for any lossy network
    public abstract class PeerBase
    {
        [Serializable]
        public struct PeerID : ICustomSerializable
        {
            [DataMember]
            public int peer;

            public static implicit operator PeerID(int i)
            {
                return new PeerID { peer = i };
            }

            public static implicit operator int(PeerID p)
            {
                return p.peer;
            }
            public void CustomSerialize(Serializer serializer)
            {
                serializer.Serialize(ref peer);
            }

        }

        public enum PacketType : byte
        {
            Reliable,
            Unreliable,
            UnreliableOrdered,
            Acknowledge,
            Termination,
            Heartbeat,
        }

        protected const int TIMEOUT_TICKS = 40 * 30; // about 30 seconds
        protected const int HEARTBEAT_TICKS = 40 * 5; // about 5 seconds
        protected const int RESEND_TICKS = 4; // about 0.1 seconds

        


        public class SequencedPacket
        {
            public ulong index;
            public byte[] packet; // Raw packet data
            public Action OnAcknowledged;
            public Action OnFailed;
            public int attemptsLeft;

            public SequencedPacket(ulong index, byte[] data, int length, int attempts = -1, bool termination = false)
            {
                this.index = index;
                this.attemptsLeft = attempts;

                packet = new byte[9 + length];
                MemoryStream stream = new MemoryStream(packet);
                BinaryWriter writer = new BinaryWriter(stream);

                if (termination)
                {
                    WriteTerminationHeader(writer, index);
                }
                else
                {
                    WriteReliableHeader(writer, index);
                }
                writer.Write(data, 0, length);
            }
        }


        public class RemotePeer
        {
            public ulong packetIndex { get; set; } // Increments for each reliable packet sent
            public ulong unreliablePacketIndex; // Increments for each unreliable ordered packet sent
            public Queue<SequencedPacket> outgoingPackets = new Queue<SequencedPacket>(); // Keep track of packets we want to send while we wait for responses
            public SequencedPacket latestOutgoingPacket { get; set; }
            public int ticksSinceLastPacketReceived;
            public int ticksSinceLastPacketSent;
            public int ticksToResend = RESEND_TICKS;
            public ulong lastAckedPacketIndex;
            public ulong lastUnreliablePacketIndex;
            internal bool loss;
        }


        public System.Random random = new System.Random();
        public bool waitingForTermination;
        protected Dictionary<PeerID, RemotePeer> peers;

        //stubs
        public abstract void Send(PeerID peerId, byte[] data, int length, PacketType packetType);
        public abstract void Startup();
        public abstract void Shutdown();
        protected abstract void CleanUp();
        public abstract void Update();
        public abstract bool Read(out BinaryReader netReader, out PeerID peerId);
        public abstract bool IsPacketAvailable();
        protected abstract void Send(byte[] packet, PeerID peerId);
        protected abstract void SendAcknowledge(PeerID peerId, ulong index);



        protected static void WriteReliableHeader(BinaryWriter writer, ulong index)
        {
            writer.Write((byte)PacketType.Reliable);
            writer.Write(index);
        }

        protected static ulong ReadReliableHeader(BinaryReader reader)
        {
            // Ignore type
            ulong index = reader.ReadUInt64();
            return index;
        }

        //

        /// <summary>Writes 1 byte</summary>
        protected static void WriteUnreliableHeader(BinaryWriter writer)
        {
            writer.Write((byte)PacketType.Unreliable);
        }

        protected static void ReadUnreliableHeader(BinaryReader reader)
        {
            // Ignore type
        }

        //

        /// <summary>Writes 9 bytes</summary>
        protected static void WriteUnreliableOrderedHeader(BinaryWriter writer, ulong index)
        {
            writer.Write((byte)PacketType.UnreliableOrdered);
            writer.Write(index);
        }

        protected static ulong ReadUnreliableOrderedHeader(BinaryReader reader)
        {
            // Ignore type
            ulong index = reader.ReadUInt64();
            return index;
        }

        //

        /// <summary>Writes 9 bytes</summary>
        protected static void WriteAcknowledge(BinaryWriter writer, ulong index)
        {
            writer.Write((byte)PacketType.Acknowledge);
            writer.Write(index);
        }

        protected static ulong ReadAcknowledge(BinaryReader reader)
        {
            // Ignore type
            ulong index = reader.ReadUInt64();
            return index;
        }

        //

        /// <summary>Writes 9 bytes</summary>
        protected static void WriteTerminationHeader(BinaryWriter writer, ulong index)
        {
            writer.Write((byte)PacketType.Termination);
            writer.Write(index);
        }

        protected static ulong ReadTerminationHeader(BinaryReader reader)
        {
            // Ignore type
            ulong index = reader.ReadUInt64();
            return index;
        }

        protected void SendTermination()
        {
            RainMeadow.Debug($"Sending all known peers a final message!");

            foreach (var peer in peers)
            {
                var peerIP = peer.Key;
                var peerData = peer.Value;

                peerData.outgoingPackets.Clear();

                peerData.latestOutgoingPacket = new SequencedPacket(++peerData.packetIndex, new byte[0], 0, 10, true);
                peerData.outgoingPackets.Enqueue(peerData.latestOutgoingPacket);

                peerData.latestOutgoingPacket.OnAcknowledged += CleanUp;
                peerData.latestOutgoingPacket.OnFailed += CleanUp;

                byte[] packetData = peerData.outgoingPackets.Peek().packet;
                Send(packetData, peerIP);
            }

            waitingForTermination = true;
        }


    }


}
