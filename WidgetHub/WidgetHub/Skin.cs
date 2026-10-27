using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;

namespace WidgetHub;

public class Skin
{
	public string Name { get; set; } = "";


	public string Folder { get; set; } = "";


	public string ExeName { get; set; } = "";


	public bool Enabled { get; set; } = true;


	public bool TopMost { get; set; }

	public bool ClickThrough { get; set; }

	public int Opacity { get; set; } = 255;


	public int X { get; set; } = -1;


	public int Y { get; set; } = -1;


	public int Width { get; set; }

	public int Height { get; set; }

	public bool PositionSaved { get; set; }

	public bool Positionless { get; set; }

	[JsonIgnore]
	public Process? Process { get; set; }

	public string Id
	{
		get
		{
			string text = Folder ?? "";
			int num = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
			if (num >= 0)
			{
				text = text.Substring(num + 1);
			}
			if (string.IsNullOrEmpty(text))
			{
				text = Path.GetFileNameWithoutExtension(ExeName ?? "");
			}
			return text;
		}
	}

	public string FullPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Folder, ExeName);
	}
}
