using System;
using System.Collections.Generic;
using System.Globalization;

namespace Shared;

internal static class Festivals
{
	private sealed class SolarFestival
	{
		public int Month;

		public int Day;

		public string Name;

		public bool Major;
	}

	private sealed class WeekdayFestival
	{
		public int Month;

		public int Week;

		public DayOfWeek DayOfWeek;

		public string Name;

		public bool Major;
	}

	private sealed class LunarFestival
	{
		public int Month;

		public int Day;

		public string Name;

		public bool Major;

		public bool Eve;
	}

	private sealed class Adjustment
	{
		public DateTime Date;

		public DayBadge Badge;
	}

	private static readonly List<SolarFestival> SolarList = BuildSolar();

	private static readonly List<WeekdayFestival> WeekdayList = BuildWeekday();

	private static readonly List<LunarFestival> LunarList = BuildLunar();

	private static readonly Dictionary<int, List<Adjustment>> Adjustments = BuildAdjustments();

	private static Dictionary<int, List<Adjustment>> BuildAdjustments()
	{
		Dictionary<int, List<Adjustment>> dictionary = new Dictionary<int, List<Adjustment>>();
		dictionary[2025] = Parse("2025-01-01,R", "2025-01-28,R", "2025-01-29,R", "2025-01-30,R", "2025-01-31,R", "2025-02-01,R", "2025-02-02,R", "2025-02-03,R", "2025-02-04,R", "2025-04-04,R", "2025-04-05,R", "2025-04-06,R", "2025-05-01,R", "2025-05-02,R", "2025-05-03,R", "2025-05-04,R", "2025-05-05,R", "2025-05-31,R", "2025-06-01,R", "2025-06-02,R", "2025-10-01,R", "2025-10-02,R", "2025-10-03,R", "2025-10-04,R", "2025-10-05,R", "2025-10-06,R", "2025-10-07,R", "2025-10-08,R", "2025-01-26,W", "2025-02-08,W", "2025-04-27,W", "2025-09-28,W", "2025-10-11,W");
		dictionary[2026] = Parse("2026-01-01,R", "2026-01-02,R", "2026-01-03,R", "2026-02-15,R", "2026-02-16,R", "2026-02-17,R", "2026-02-18,R", "2026-02-19,R", "2026-02-20,R", "2026-02-21,R", "2026-02-22,R", "2026-04-04,R", "2026-04-05,R", "2026-04-06,R", "2026-05-01,R", "2026-05-02,R", "2026-05-03,R", "2026-05-04,R", "2026-05-05,R", "2026-06-19,R", "2026-06-20,R", "2026-06-21,R", "2026-09-19,W", "2026-09-25,R", "2026-09-26,R", "2026-09-27,R", "2026-10-01,R", "2026-10-02,R", "2026-10-03,R", "2026-10-04,R", "2026-10-05,R", "2026-10-06,R", "2026-10-07,R", "2026-02-14,W", "2026-02-28,W", "2026-10-10,W");
		dictionary[2027] = Parse("2027-01-01,R", "2027-01-02,R", "2027-01-03,R", "2027-02-06,R", "2027-02-07,R", "2027-02-08,R", "2027-02-09,R", "2027-02-10,R", "2027-02-11,R", "2027-04-03,R", "2027-04-04,R", "2027-04-05,R", "2027-05-01,R", "2027-05-02,R", "2027-05-03,R", "2027-05-04,R", "2027-05-05,R", "2027-06-09,R", "2027-06-10,R", "2027-06-11,R", "2027-09-15,R", "2027-09-16,R", "2027-09-17,R", "2027-10-01,R", "2027-10-02,R", "2027-10-03,R", "2027-10-04,R", "2027-10-05,R", "2027-10-06,R", "2027-10-07,R");
		return dictionary;
	}

	private static List<Adjustment> Parse(params string[] items)
	{
		List<Adjustment> list = new List<Adjustment>();
		for (int i = 0; i < items.Length; i++)
		{
			string[] array = items[i].Split(new char[1] { ',' });
			if (array.Length == 2 && DateTime.TryParseExact(array[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
			{
				list.Add(new Adjustment
				{
					Date = result.Date,
					Badge = ((!string.Equals(array[1].Trim(), "W", StringComparison.OrdinalIgnoreCase)) ? DayBadge.Rest : DayBadge.Work)
				});
			}
		}
		return list;
	}

	private static List<SolarFestival> BuildSolar()
	{
		return new List<SolarFestival>
		{
			new SolarFestival
			{
				Month = 1,
				Day = 1,
				Name = "元旦",
				Major = true
			},
			new SolarFestival
			{
				Month = 2,
				Day = 14,
				Name = "情人节"
			},
			new SolarFestival
			{
				Month = 3,
				Day = 8,
				Name = "妇女节"
			},
			new SolarFestival
			{
				Month = 3,
				Day = 12,
				Name = "植树节"
			},
			new SolarFestival
			{
				Month = 3,
				Day = 15,
				Name = "消费者权益日"
			},
			new SolarFestival
			{
				Month = 4,
				Day = 1,
				Name = "愚人节"
			},
			new SolarFestival
			{
				Month = 4,
				Day = 22,
				Name = "地球日"
			},
			new SolarFestival
			{
				Month = 5,
				Day = 1,
				Name = "劳动节",
				Major = true
			},
			new SolarFestival
			{
				Month = 5,
				Day = 4,
				Name = "青年节"
			},
			new SolarFestival
			{
				Month = 5,
				Day = 12,
				Name = "护士节"
			},
			new SolarFestival
			{
				Month = 6,
				Day = 1,
				Name = "儿童节"
			},
			new SolarFestival
			{
				Month = 6,
				Day = 5,
				Name = "环境日"
			},
			new SolarFestival
			{
				Month = 6,
				Day = 6,
				Name = "爱眼日"
			},
			new SolarFestival
			{
				Month = 7,
				Day = 1,
				Name = "建党节"
			},
			new SolarFestival
			{
				Month = 8,
				Day = 1,
				Name = "建军节"
			},
			new SolarFestival
			{
				Month = 9,
				Day = 3,
				Name = "抗战胜利"
			},
			new SolarFestival
			{
				Month = 9,
				Day = 10,
				Name = "教师节"
			},
			new SolarFestival
			{
				Month = 9,
				Day = 18,
				Name = "九一八"
			},
			new SolarFestival
			{
				Month = 9,
				Day = 20,
				Name = "爱牙日"
			},
			new SolarFestival
			{
				Month = 10,
				Day = 1,
				Name = "国庆节",
				Major = true
			},
			new SolarFestival
			{
				Month = 10,
				Day = 4,
				Name = "动物日"
			},
			new SolarFestival
			{
				Month = 11,
				Day = 8,
				Name = "记者节"
			},
			new SolarFestival
			{
				Month = 12,
				Day = 4,
				Name = "宪法日"
			},
			new SolarFestival
			{
				Month = 12,
				Day = 13,
				Name = "公祭日"
			},
			new SolarFestival
			{
				Month = 12,
				Day = 24,
				Name = "平安夜"
			},
			new SolarFestival
			{
				Month = 12,
				Day = 25,
				Name = "圣诞节"
			}
		};
	}

	private static List<WeekdayFestival> BuildWeekday()
	{
		return new List<WeekdayFestival>
		{
			new WeekdayFestival
			{
				Month = 5,
				Week = 2,
				DayOfWeek = DayOfWeek.Sunday,
				Name = "母亲节"
			},
			new WeekdayFestival
			{
				Month = 6,
				Week = 3,
				DayOfWeek = DayOfWeek.Sunday,
				Name = "父亲节"
			},
			new WeekdayFestival
			{
				Month = 11,
				Week = 4,
				DayOfWeek = DayOfWeek.Thursday,
				Name = "感恩节"
			}
		};
	}

	private static List<LunarFestival> BuildLunar()
	{
		return new List<LunarFestival>
		{
			new LunarFestival
			{
				Month = 1,
				Day = 1,
				Name = "春节",
				Major = true
			},
			new LunarFestival
			{
				Month = 1,
				Day = 15,
				Name = "元宵节",
				Major = true
			},
			new LunarFestival
			{
				Month = 1,
				Day = 3,
				Name = "财神节"
			},
			new LunarFestival
			{
				Month = 2,
				Day = 2,
				Name = "龙抬头"
			},
			new LunarFestival
			{
				Month = 3,
				Day = 3,
				Name = "上巳节"
			},
			new LunarFestival
			{
				Month = 5,
				Day = 5,
				Name = "端午节",
				Major = true
			},
			new LunarFestival
			{
				Month = 7,
				Day = 7,
				Name = "七夕节"
			},
			new LunarFestival
			{
				Month = 7,
				Day = 15,
				Name = "中元节"
			},
			new LunarFestival
			{
				Month = 8,
				Day = 15,
				Name = "中秋节",
				Major = true
			},
			new LunarFestival
			{
				Month = 9,
				Day = 9,
				Name = "重阳节"
			},
			new LunarFestival
			{
				Month = 10,
				Day = 1,
				Name = "寒衣节"
			},
			new LunarFestival
			{
				Month = 10,
				Day = 15,
				Name = "下元节"
			},
			new LunarFestival
			{
				Month = 12,
				Day = 8,
				Name = "腊八节"
			},
			new LunarFestival
			{
				Month = 12,
				Day = 23,
				Name = "小年"
			},
			new LunarFestival
			{
				Month = 12,
				Day = 30,
				Name = "除夕",
				Major = true,
				Eve = true
			}
		};
	}

	public static string SolarFestivalName(int month, int day, out bool major)
	{
		foreach (SolarFestival solar in SolarList)
		{
			if (solar.Month == month && solar.Day == day)
			{
				major = solar.Major;
				return solar.Name;
			}
		}
		major = false;
		return null;
	}

	public static string WeekdayFestivalName(DateTime date)
	{
		foreach (WeekdayFestival weekday in WeekdayList)
		{
			if (weekday.Month == date.Month && weekday.DayOfWeek == date.DayOfWeek && NthWeekday(date) == weekday.Week)
			{
				return weekday.Name;
			}
		}
		return null;
	}

	public static string LunarFestivalName(int lunarMonth, int lunarDay, int daysInMonth, out bool major)
	{
		major = false;
		foreach (LunarFestival lunar in LunarList)
		{
			if (lunar.Eve)
			{
				if (lunarMonth == lunar.Month && lunarDay == daysInMonth)
				{
					major = true;
					return lunar.Name;
				}
			}
			else if (lunar.Month == lunarMonth && lunar.Day == lunarDay)
			{
				major = lunar.Major;
				return lunar.Name;
			}
		}
		return null;
	}

	public static int NthWeekday(DateTime date)
	{
		return (date.Day - 1) / 7 + 1;
	}

	public static DayBadge Badge(DateTime date)
	{
		date = date.Date;
		if (Adjustments.TryGetValue(date.Year, out var value))
		{
			foreach (Adjustment item in value)
			{
				if (item.Date == date)
				{
					return item.Badge;
				}
			}
		}
		if (IsStatutoryRest(date))
		{
			return DayBadge.Rest;
		}
		return DayBadge.None;
	}

	public static bool IsStatutoryRest(DateTime date)
	{
		if (date.Month == 1 && date.Day == 1)
		{
			return true;
		}
		if (date.Month == 5 && date.Day >= 1 && date.Day <= 3)
		{
			return true;
		}
		if (date.Month == 10 && date.Day >= 1 && date.Day <= 3)
		{
			return true;
		}
		if (date.Month == 4 && (date.Day == 4 || date.Day == 5))
		{
			return true;
		}
		LunarDate lunarDate = Lunar.FromSolar(date);
		if (lunarDate.Month == 1 && !lunarDate.IsLeapMonth)
		{
			if (lunarDate.Day >= 1 && lunarDate.Day <= 3)
			{
				return true;
			}
			int monthDays = lunarDate.MonthDays;
			if (lunarDate.Day == monthDays)
			{
				return true;
			}
		}
		if (lunarDate.Month == 5 && !lunarDate.IsLeapMonth && lunarDate.Day == 5)
		{
			return true;
		}
		if (lunarDate.Month == 8 && !lunarDate.IsLeapMonth && lunarDate.Day == 15)
		{
			return true;
		}
		return false;
	}

	public static bool IsWorkWeekend(DateTime date)
	{
		return Badge(date) == DayBadge.Work;
	}

	public static bool IsRestDay(DateTime date)
	{
		switch (Badge(date))
		{
		case DayBadge.Rest:
			return true;
		case DayBadge.Work:
			return false;
		default:
			if (date.DayOfWeek != DayOfWeek.Saturday)
			{
				return date.DayOfWeek == DayOfWeek.Sunday;
			}
			return true;
		}
	}

	public static string DescribeDay(DateTime date)
	{
		List<string> list = new List<string>();
		string text = Lunar.TermName(date);
		if (!string.IsNullOrEmpty(text))
		{
			list.Add(text);
		}
		bool major;
		string text2 = SolarFestivalName(date.Month, date.Day, out major);
		if (string.IsNullOrEmpty(text2))
		{
			text2 = WeekdayFestivalName(date);
		}
		LunarDate lunarDate = Lunar.FromSolar(date);
		bool major2;
		string text3 = LunarFestivalName(lunarDate.Month, lunarDate.Day, lunarDate.MonthDays, out major2);
		if (!string.IsNullOrEmpty(text3))
		{
			list.Add(text3);
		}
		else if (!string.IsNullOrEmpty(text2))
		{
			list.Add(text2);
		}
		else
		{
			list.Add(Lunar.ToChinese(date));
		}
		return string.Join(" ", list.ToArray());
	}

	public static string UpcomingFestival(DateTime from, out DateTime date, out int days)
	{
		for (int i = 0; i < 400; i++)
		{
			DateTime dateTime = from.Date.AddDays(i);
			bool major;
			string text = SolarFestivalName(dateTime.Month, dateTime.Day, out major);
			if (string.IsNullOrEmpty(text))
			{
				text = LunarFestivalName(Lunar.FromSolar(dateTime).Month, Lunar.FromSolar(dateTime).Day, 0, out major);
			}
			if (string.IsNullOrEmpty(text))
			{
				text = Lunar.TermName(dateTime);
			}
			if (!string.IsNullOrEmpty(text))
			{
				date = dateTime;
				days = i;
				return text;
			}
		}
		date = from.Date;
		days = -1;
		return null;
	}
}
