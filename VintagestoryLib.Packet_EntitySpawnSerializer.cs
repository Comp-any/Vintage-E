using System;

public class Packet_EntitySpawnSerializer
{
	private const int field = 8;

	public static Packet_EntitySpawn DeserializeLengthDelimitedNew(CitoMemoryStream stream)
	{
		Packet_EntitySpawn packet_EntitySpawn = new Packet_EntitySpawn();
		DeserializeLengthDelimited(stream, packet_EntitySpawn);
		return packet_EntitySpawn;
	}

	public static Packet_EntitySpawn DeserializeBuffer(byte[] buffer, int length, Packet_EntitySpawn instance)
	{
		Deserialize(new CitoMemoryStream(buffer, length), instance);
		return instance;
	}

	public static Packet_EntitySpawn Deserialize(CitoMemoryStream stream, Packet_EntitySpawn instance)
	{
		instance.InitializeValues();
		int num;
		while (true)
		{
			num = stream.ReadByte();
			if ((num & 0x80) != 0)
			{
				num = ProtocolParser.ReadKeyAsInt(num, stream);
				if ((num & 0x4000) != 0)
				{
					break;
				}
			}
			switch (num)
			{
			case 0:
				return null;
			case 10:
				instance.EntityAdd(Packet_EntitySerializer.DeserializeLengthDelimitedNew(stream));
				break;
			default:
				ProtocolParser.SkipKey(stream, Key.Create(num));
				break;
			}
		}
		if (num >= 0)
		{
			return null;
		}
		return instance;
	}

	public static Packet_EntitySpawn DeserializeLengthDelimited(CitoMemoryStream stream, Packet_EntitySpawn instance)
	{
		int num = ProtocolParser.ReadUInt32(stream);
		int length = stream.GetLength();
		stream.SetLength(stream.Position() + num);
		Packet_EntitySpawn result = Deserialize(stream, instance);
		stream.SetLength(length);
		return result;
	}

	public static void Serialize(CitoStream stream, Packet_EntitySpawn instance)
	{
		if (instance.Entity != null)
		{
			Packet_Entity[] entity = instance.Entity;
			int entityCount = instance.EntityCount;
			for (int i = 0; i < entity.Length && i < entityCount; i++)
			{
				stream.WriteByte(10);
				Packet_EntitySerializer.SerializeWithSize(stream, entity[i]);
			}
		}
	}

	public static int GetSize(Packet_EntitySpawn instance)
	{
		int num = 0;
		if (instance.Entity != null)
		{
			for (int i = 0; i < instance.EntityCount; i++)
			{
				int size = Packet_EntitySerializer.GetSize(instance.Entity[i]);
				num += size + ProtocolParser.GetSize(size) + 1;
			}
		}
		instance.size = num;
		return num;
	}

	public static void SerializeWithSize(CitoStream stream, Packet_EntitySpawn instance)
	{
		ProtocolParser.WriteUInt32_(stream, instance.size);
		int num = stream.Position();
		Serialize(stream, instance);
		int num2 = stream.Position() - num;
		if (num2 != instance.size)
		{
			throw new Exception("Sizing mismatch: " + instance.size + " != " + num2);
		}
	}

	public static byte[] SerializeToBytes(Packet_EntitySpawn instance)
	{
		CitoMemoryStream citoMemoryStream = new CitoMemoryStream();
		Serialize(citoMemoryStream, instance);
		return citoMemoryStream.ToArray();
	}

	public static void SerializeLengthDelimited(CitoStream stream, Packet_EntitySpawn instance)
	{
		byte[] array = SerializeToBytes(instance);
		ProtocolParser.WriteUInt32_(stream, array.Length);
		stream.Write(array, 0, array.Length);
	}
}
