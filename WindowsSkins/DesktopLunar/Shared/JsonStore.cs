using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Shared;

internal static class JsonStore
{
	public static void Save<T>(string path, T value)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Expected O, but got Unknown
		try
		{
			DataContractJsonSerializer val = new DataContractJsonSerializer(typeof(T));
			string text = path + ".tmp";
			using (FileStream fileStream = File.Create(text))
			{
				((XmlObjectSerializer)val).WriteObject((Stream)fileStream, (object)value);
			}
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(text, path);
		}
		catch
		{
		}
	}

	public static T Load<T>(string path)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Expected O, but got Unknown
		try
		{
			if (!File.Exists(path))
			{
				return default(T);
			}
			DataContractJsonSerializer val = new DataContractJsonSerializer(typeof(T));
			using FileStream fileStream = File.OpenRead(path);
			return (T)((XmlObjectSerializer)val).ReadObject((Stream)fileStream);
		}
		catch
		{
			return default(T);
		}
	}
}
