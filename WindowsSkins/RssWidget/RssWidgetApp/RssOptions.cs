using System.Runtime.Serialization;

namespace RssWidgetApp;

[DataContract]
internal sealed class RssOptions
{
	[DataMember]
	public int RefreshMinutes { get; set; }

	[DataMember]
	public int MaxItemsPerFeed { get; set; }

	[DataMember]
	public string Category { get; set; }

	[DataMember]
	public bool ShowHeader { get; set; }

	[DataMember]
	public bool ShowIp { get; set; }

	[DataMember]
	public long LastRefreshTicks { get; set; }

	[DataMember]
	public int SavedWidth { get; set; }

	[DataMember]
	public int SavedHeight { get; set; }

	public RssOptions()
	{
		RefreshMinutes = 15;
		MaxItemsPerFeed = 30;
		Category = "全部";
		ShowHeader = true;
		ShowIp = true;
	}

	public void Normalize()
	{
		if (RefreshMinutes < 1 || RefreshMinutes > 240)
		{
			RefreshMinutes = 15;
		}
		if (MaxItemsPerFeed < 5 || MaxItemsPerFeed > 200)
		{
			MaxItemsPerFeed = 30;
		}
		if (SavedWidth < 380 || SavedWidth > 4000)
		{
			SavedWidth = 0;
		}
		if (SavedHeight < 300 || SavedHeight > 4000)
		{
			SavedHeight = 0;
		}
	}
}
