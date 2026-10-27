namespace WidgetHub;

public class UpdateInfo
{
	public string TagVersion = "";

	public string HtmlUrl = "";

	public string AssetUrl = "";

	public string AssetName = "";

	public long AssetId;

	public string AssetApiUrl
	{
		get
		{
			if (AssetId <= 0)
			{
				return "";
			}
			return "https://api.github.com/repos/feibixunchang/desktop-widget-suite/releases/assets/" + AssetId;
		}
	}
}
