using System.Runtime.Serialization;

namespace Shared;

[DataContract]
internal sealed class WidgetSettings
{
	[DataMember]
	public int X { get; set; }

	[DataMember]
	public int Y { get; set; }

	[DataMember]
	public int FontSize { get; set; }

	[DataMember]
	public bool TopMost { get; set; }

	[DataMember]
	public bool Locked { get; set; }

	[DataMember]
	public string FontColor { get; set; }

	[DataMember]
	public string TimeColor { get; set; }

	[DataMember]
	public int Opacity { get; set; }

	[DataMember]
	public bool FirstRun { get; set; }

	[DataMember]
	public bool? DesktopLayer { get; set; }

	[DataMember]
	public bool? HideOnFullscreen { get; set; }
}
