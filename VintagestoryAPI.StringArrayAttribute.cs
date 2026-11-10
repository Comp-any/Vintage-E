using System;
using System.IO;
using System.Text;

namespace Vintagestory.API.Datastructures;

public class StringArrayAttribute : ArrayAttribute<string>, IAttribute
{
	public StringArrayAttribute()
	{
	}

	public StringArrayAttribute(string[] value)
	{
		base.value = value;
	}

	public void ToBytes(BinaryWriter stream)
	{
		stream.Write(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			stream.Write(value[i]);
		}
	}

	public void FromBytes(BinaryReader stream)
	{
		int num = stream.ReadInt32();
		value = new string[num];
		for (int i = 0; i < num; i++)
		{
			value[i] = stream.ReadString();
		}
	}

	public int GetAttributeId()
	{
		return 10;
	}

	public override string ToJsonToken()
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("[");
		for (int i = 0; i < value.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append("\"" + value[i] + "\"");
		}
		stringBuilder.Append("]");
		return stringBuilder.ToString();
	}

	public IAttribute Clone()
	{
		return new StringArrayAttribute((string[])value.Clone());
	}

	Type IAttribute.GetType()
	{
		return GetType();
	}
}
