using System;
using System.IO;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.Common;

namespace Vintagestory.Client.NoObf;

public class NetworkChannel : NetworkChannelBase, IClientNetworkChannel, INetworkChannel
{
	protected NetworkAPI api;

	internal Action<Packet_CustomPacket>[] handlers = new Action<Packet_CustomPacket>[128];

	public bool Connected { get; set; }

	bool IClientNetworkChannel.Connected => Connected;

	public NetworkChannel(NetworkAPI api, int channelId, string channelName)
		: base(channelId, channelName)
	{
		this.api = api;
	}

	public void OnPacket(Packet_CustomPacket p)
	{
		if (p.MessageId < handlers.Length)
		{
			handlers[p.MessageId]?.Invoke(p);
		}
	}

	public new IClientNetworkChannel RegisterMessageType(Type type)
	{
		messageTypes[type] = nextHandlerId++;
		return this;
	}

	public new IClientNetworkChannel RegisterMessageType<T>()
	{
		messageTypes[typeof(T)] = nextHandlerId++;
		return this;
	}

	public virtual IClientNetworkChannel SetMessageHandler<T>(NetworkServerMessageHandler<T> handler)
	{
		if (!messageTypes.TryGetValue(typeof(T), out var value))
		{
			throw new Exception("No such message type " + typeof(T)?.ToString() + " registered. Did you forgot to call RegisterMessageType?");
		}
		if (typeof(T).IsArray)
		{
			throw new ArgumentException("Please do not use array messages, they seem to cause serialization problems in rare cases. Pack that array into its own class.");
		}
		Serializer.PrepareSerializer<T>();
		handlers[value] = (Packet_CustomPacket p) =>
		{
			T packet = default;
			if (p.Data != null)
			{
				using MemoryStream source = new MemoryStream(p.Data);
				packet = Serializer.Deserialize<T>(source);
			}
			handler(packet);
		};
		return this;
	}

	public virtual void SendPacket<T>(T message)
	{
		if (!Connected)
		{
			throw new Exception("Attempting to send data to a not connected channel. For optionally dependent network channels test if your channel is Connected before sending data.");
		}
		if (!messageTypes.TryGetValue(typeof(T), out var value))
		{
			throw new Exception("No such message type " + typeof(T)?.ToString() + " registered. Did you forgot to call RegisterMessageType?");
		}
		byte[] data;
		using (MemoryStream memoryStream = new MemoryStream())
		{
			Serializer.Serialize((Stream)memoryStream, message);
			data = memoryStream.ToArray();
		}
		Packet_CustomPacket packet_CustomPacket = new Packet_CustomPacket
		{
			ChannelId = channelId,
			MessageId = value
		};
		packet_CustomPacket.SetData(data);
		api.game.SendPacketClient(new Packet_Client
		{
			Id = 23,
			CustomPacket = packet_CustomPacket
		});
	}
}
