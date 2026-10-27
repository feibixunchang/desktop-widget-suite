using System;
using System.Runtime.Serialization;

namespace RssWidgetApp;

[DataContract]
internal sealed class FeedItem
{
	[DataMember]
	public string Title { get; set; }

	[DataMember]
	public string Link { get; set; }

	[DataMember]
	public string Source { get; set; }

	[DataMember]
	public string Category { get; set; }

	[DataMember]
	public string Summary { get; set; }

	[DataMember]
	public long Published { get; set; }

	public DateTime PublishedTime
	{
		get
		{
			if (Published != 0L)
			{
				return new DateTime(Published);
			}
			return DateTime.MinValue;
		}
	}
}
