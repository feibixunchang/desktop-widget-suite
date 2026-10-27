using System.IO;
using System.Runtime.Serialization.Json;

namespace Shared;

internal static class JsonStore
{
	public static void Save<T>(string path, T value)
	{
		try
		{
			DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(T));
			string text = path + ".tmp";
			using (FileStream stream = File.Create(text))
			{
				dataContractJsonSerializer.WriteObject((Stream)stream, (object)value);
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
		try
		{
			if (!File.Exists(path))
			{
				return default(T);
			}
			DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(T));
			using FileStream stream = File.OpenRead(path);
			return (T)dataContractJsonSerializer.ReadObject((Stream)stream);
		}
		catch
		{
			return default(T);
		}
	}
}
