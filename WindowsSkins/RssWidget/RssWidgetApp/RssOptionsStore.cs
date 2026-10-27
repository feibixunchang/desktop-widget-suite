using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace RssWidgetApp;

internal static class RssOptionsStore
{
	private static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RssWidget", "rss.json");

	public static RssOptions Load()
	{
		try
		{
			if (!System.IO.File.Exists(File))
			{
				return new RssOptions();
			}
			DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(RssOptions));
			using FileStream stream = System.IO.File.OpenRead(File);
			return ((RssOptions)dataContractJsonSerializer.ReadObject((Stream)stream)) ?? new RssOptions();
		}
		catch
		{
			return new RssOptions();
		}
	}

	public static void Save(RssOptions options)
	{
		try
		{
			string directoryName = Path.GetDirectoryName(File);
			if (!Directory.Exists(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			string text = File + ".tmp";
			DataContractJsonSerializer dataContractJsonSerializer = new DataContractJsonSerializer(typeof(RssOptions));
			using (FileStream stream = System.IO.File.Create(text))
			{
				dataContractJsonSerializer.WriteObject((Stream)stream, (object)options);
			}
			if (System.IO.File.Exists(File))
			{
				System.IO.File.Delete(File);
			}
			System.IO.File.Move(text, File);
		}
		catch
		{
		}
	}
}
