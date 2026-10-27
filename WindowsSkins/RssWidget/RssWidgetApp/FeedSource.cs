using System.Runtime.Serialization;

namespace RssWidgetApp;

[DataContract]
internal sealed class FeedSource
{
	[DataMember]
	public string Name { get; set; }

	[DataMember]
	public string Url { get; set; }

	[DataMember]
	public string Category { get; set; }

	[DataMember]
	public string Short { get; set; }
}
