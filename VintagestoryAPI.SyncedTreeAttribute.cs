using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vintagestory.API.Util;

namespace Vintagestory.API.Datastructures;

public class SyncedTreeAttribute : TreeAttribute
{
	/// <summary>
	/// Whole tree will be resent
	/// </summary>
	private bool allDirty;

	/// <summary>
	/// Subtrees will be resent
	/// </summary>
	private HashSet<string> attributePathsDirty = new HashSet<string>();

	public List<TreeModifiedListener> OnModified = new List<TreeModifiedListener>();

	public bool AllDirty => allDirty;

	public bool PartialDirty => attributePathsDirty.Count > 0;

	public void RegisterModifiedListener(string path, Action listener)
	{
		OnModified.Add(new TreeModifiedListener
		{
			path = path,
			listener = listener
		});
	}

	public void UnregisterListener(Action listener)
	{
		foreach (TreeModifiedListener item in new List<TreeModifiedListener>(OnModified))
		{
			if (item.listener == listener)
			{
				OnModified.Remove(item);
			}
		}
	}

	/// <summary>
	/// Marks the whole attribute tree as dirty, so that it will be resent to all connected clients. Does not trigger modified listeners (because it makes no sense and breaks things)
	/// </summary>
	public void MarkAllDirty()
	{
		allDirty = true;
	}

	public void MarkClean()
	{
		allDirty = false;
		attributePathsDirty.Clear();
	}

	/// <summary>
	/// Marks part of the attribute tree as dirty, allowing for a partial update of the attribute tree.
	/// If the whole tree is already marked dirty, it will still trigger modified listeners.
	/// </summary>
	/// <param name="path"></param>
	public void MarkPathDirty(string path)
	{
		List<TreeModifiedListener> onModified = OnModified;
		for (int i = 0; i < onModified.Count; i++)
		{
			TreeModifiedListener treeModifiedListener = onModified[i];
			if (treeModifiedListener != null && (treeModifiedListener.path == null || path.StartsWithOrdinal(treeModifiedListener.path)))
			{
				treeModifiedListener.listener();
			}
		}
		if (!allDirty)
		{
			attributePathsDirty.Add(path);
		}
	}

	public override void SetInt(string key, int value)
	{
		base.SetInt(key, value);
		MarkPathDirty(key);
	}

	/// <summary>
	/// Equivalent to i++ within the TreeAttribute, i.e. it increases the stored value but returns the current read value
	/// </summary>
	/// <param name="key"></param>
	/// <param name="defaultValue"></param>
	/// <returns></returns>
	public virtual int GetIntAndIncrement(string key, int defaultValue = 0)
	{
		IntAttribute intAttribute = attributes.TryGetValue(key) as IntAttribute;
		if (intAttribute == null)
		{
			intAttribute = (IntAttribute)(attributes[key] = new IntAttribute(defaultValue));
		}
		int value = intAttribute.value;
		intAttribute.SetValue(value + 1);
		MarkPathDirty(key);
		return value;
	}

	public override void SetLong(string key, long value)
	{
		base.SetLong(key, value);
		MarkPathDirty(key);
	}

	public override void SetFloat(string key, float value)
	{
		base.SetFloat(key, value);
		MarkPathDirty(key);
	}

	public override void SetBool(string key, bool value)
	{
		base.SetBool(key, value);
		MarkPathDirty(key);
	}

	public override void SetBytes(string key, byte[] value)
	{
		base.SetBytes(key, value);
		MarkPathDirty(key);
	}

	public override void SetDouble(string key, double value)
	{
		base.SetDouble(key, value);
		MarkPathDirty(key);
	}

	public override void SetString(string key, string value)
	{
		base.SetString(key, value);
		MarkPathDirty(key);
	}

	public override void SetAttribute(string key, IAttribute value)
	{
		base.SetAttribute(key, value);
		MarkPathDirty(key);
	}

	public override void RemoveAttribute(string key)
	{
		base.RemoveAttribute(key);
		MarkAllDirty();
	}

	public override SyncedTreeAttribute Clone()
	{
		MemoryStream memoryStream = new MemoryStream();
		BinaryWriter stream = new BinaryWriter(memoryStream);
		ToBytes(stream);
		memoryStream.Position = 0L;
		BinaryReader stream2 = new BinaryReader(memoryStream);
		SyncedTreeAttribute syncedTreeAttribute = new SyncedTreeAttribute();
		syncedTreeAttribute.FromBytes(stream2);
		return syncedTreeAttribute;
	}

	public void GetDirtyPathData(out string[] paths, out byte[][] dirtydata)
	{
		FastMemoryStream ms = new FastMemoryStream();
		GetDirtyPathData(ms, out paths, out dirtydata);
	}

	public void GetDirtyPathData(FastMemoryStream ms, out string[] paths, out byte[][] dirtydata)
	{
		try
		{
			paths = attributePathsDirty.ToArray();
		}
		catch
		{
			paths = attributePathsDirty.ToArray();
		}
		dirtydata = new byte[paths.Length][];
		BinaryWriter binaryWriter = new BinaryWriter(ms);
		for (int i = 0; i < paths.Length; i++)
		{
			string path;
			IAttribute attributeByPath;
			if ((path = paths[i]) != null && (attributeByPath = GetAttributeByPath(path)) != null)
			{
				ms.Reset();
				binaryWriter.Write((byte)attributeByPath.GetAttributeId());
				attributeByPath.ToBytes(binaryWriter);
				dirtydata[i] = ms.ToArray();
			}
		}
	}

	public override void FromBytes(BinaryReader stream)
	{
		base.FromBytes(stream);
		foreach (TreeModifiedListener item in OnModified)
		{
			item.listener();
		}
	}

	public void PartialUpdate(string path, byte[] data)
	{
		IAttribute attribute = GetAttributeByPath(path);
		if (data == null)
		{
			DeleteAttributeByPath(path);
			return;
		}
		BinaryReader binaryReader = new BinaryReader(new MemoryStream(data));
		int key = binaryReader.ReadByte();
		if (attribute == null)
		{
			attribute = (IAttribute)Activator.CreateInstance(TreeAttribute.AttributeIdMapping[key]);
			attributes[path] = attribute;
		}
		attribute.FromBytes(binaryReader);
		foreach (TreeModifiedListener item in OnModified)
		{
			if (item.path == null || path.StartsWithOrdinal(item.path))
			{
				item.listener();
			}
		}
	}
}
