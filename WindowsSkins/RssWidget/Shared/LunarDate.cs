namespace Shared;

internal struct LunarDate
{
	public int Year;

	public int Month;

	public int Day;

	public bool IsLeapMonth;

	public int MonthDays => Lunar.MonthDays(Year, Month);

	public string MonthText
	{
		get
		{
			string text = Lunar.MonthName(Month);
			return (IsLeapMonth ? "闰" : string.Empty) + text + "月";
		}
	}

	public string DayText => Lunar.DayName(Day);

	public string YearText
	{
		get
		{
			string text = Lunar.Gan[((Year - 4) % 10 + 10) % 10];
			string text2 = Lunar.Zhi[((Year - 4) % 12 + 12) % 12];
			return text + text2 + "年（" + Lunar.Animals[((Year - 4) % 12 + 12) % 12] + "年）";
		}
	}

	public string ShortYearText
	{
		get
		{
			string obj = Lunar.Gan[((Year - 4) % 10 + 10) % 10];
			string text = Lunar.Zhi[((Year - 4) % 12 + 12) % 12];
			return obj + text + Lunar.Animals[((Year - 4) % 12 + 12) % 12] + "年";
		}
	}

	public override string ToString()
	{
		return MonthText + DayText;
	}
}
