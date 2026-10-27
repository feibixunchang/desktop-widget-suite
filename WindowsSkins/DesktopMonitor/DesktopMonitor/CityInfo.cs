using System.Collections.Generic;

namespace DesktopMonitor;

internal sealed class CityInfo
{
	public string Name { get; set; } = string.Empty;


	public string Admin1 { get; set; } = string.Empty;


	public string Admin2 { get; set; } = string.Empty;


	public string Country { get; set; } = string.Empty;


	public string CountryCode { get; set; } = string.Empty;


	public double Latitude { get; set; }

	public double Longitude { get; set; }

	public string DetailText
	{
		get
		{
			List<string> list = new List<string>();
			if (!string.IsNullOrEmpty(Name))
			{
				list.Add(Name);
			}
			if (!string.IsNullOrEmpty(Admin1))
			{
				list.Add(Admin1);
			}
			if (!string.IsNullOrEmpty(Admin2))
			{
				list.Add(Admin2);
			}
			return string.Join(" · ", list.ToArray());
		}
	}

	public string FullName
	{
		get
		{
			List<string> list = new List<string>();
			if (!string.IsNullOrEmpty(Country))
			{
				list.Add(Country);
			}
			if (!string.IsNullOrEmpty(Admin1))
			{
				list.Add(Admin1);
			}
			if (!string.IsNullOrEmpty(Admin2))
			{
				list.Add(Admin2);
			}
			if (!string.IsNullOrEmpty(Name))
			{
				list.Add(Name);
			}
			return string.Join(string.Empty, list.ToArray());
		}
	}
}
