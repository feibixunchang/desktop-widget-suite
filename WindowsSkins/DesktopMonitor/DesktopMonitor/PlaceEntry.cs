namespace DesktopMonitor;

internal sealed class PlaceEntry
{
	public string Name { get; }

	public string Province { get; }

	public string Area { get; }

	public int Priority { get; }

	public int Population { get; }

	public double Latitude { get; }

	public double Longitude { get; }

	public string NormalizedName => PlaceCatalog.Normalize(Name);

	public string NormalizedArea => PlaceCatalog.Normalize(Area);

	public PlaceEntry(string name, string province, string area, int priority, int population, double latitude, double longitude)
	{
		Name = name;
		Province = province;
		Area = area;
		Priority = priority;
		Population = population;
		Latitude = latitude;
		Longitude = longitude;
	}

	public CityInfo ToCityInfo()
	{
		return new CityInfo
		{
			Name = PlaceCatalog.Shorten(Name),
			Admin1 = Province,
			Admin2 = Area,
			Country = "中国",
			CountryCode = "CN",
			Latitude = Latitude,
			Longitude = Longitude
		};
	}
}
