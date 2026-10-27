using System;
using System.Globalization;

namespace Shared;

internal static class Lunar
{
	private static readonly int[] Info = new int[201]
	{
		19416, 19168, 42352, 21717, 53856, 55632, 91476, 22176, 39632, 21970,
		19168, 42422, 42192, 53840, 119381, 46400, 54944, 44450, 38320, 84343,
		18800, 42160, 46261, 27216, 27968, 109396, 11104, 38256, 21234, 18800,
		25958, 54432, 59984, 28309, 23248, 11104, 100067, 37600, 116951, 51536,
		54432, 120998, 46416, 22176, 107956, 9680, 37584, 53938, 43344, 46423,
		27808, 46416, 86869, 19872, 42416, 83315, 21168, 43432, 59728, 27296,
		44710, 43856, 19296, 43748, 42352, 21088, 62051, 55632, 23383, 22176,
		38608, 19925, 19152, 42192, 54484, 53840, 54616, 46400, 46752, 103846,
		38320, 18864, 43380, 42160, 45690, 27216, 27968, 44870, 43872, 38256,
		19189, 18800, 25776, 29859, 59984, 27480, 21952, 43872, 38613, 37600,
		51552, 55636, 54432, 55888, 30034, 22176, 43959, 9680, 37584, 51893,
		43344, 46240, 47780, 44368, 21977, 19360, 42416, 86390, 21168, 43312,
		31060, 27296, 44368, 23378, 19296, 42726, 42208, 53856, 60005, 54576,
		23200, 30371, 38608, 19195, 19152, 42192, 118966, 53840, 54560, 56645,
		46496, 22224, 21938, 18864, 42359, 42160, 43600, 111189, 27936, 44448,
		84835, 37744, 18936, 18800, 25776, 92326, 59984, 27424, 108228, 43744,
		41696, 53987, 51552, 54615, 54432, 55888, 23893, 22176, 42704, 21972,
		21200, 43448, 43344, 46240, 46758, 44368, 21920, 43940, 42416, 21168,
		45683, 26928, 29495, 27296, 44368, 84821, 19296, 42352, 21732, 53600,
		59752, 54560, 55968, 92838, 22224, 19168, 43476, 41680, 53584, 62034,
		54560
	};

	private static readonly string[] Terms = new string[24]
	{
		"小寒", "大寒", "立春", "雨水", "惊蛰", "春分", "清明", "谷雨", "立夏", "小满",
		"芒种", "夏至", "小暑", "大暑", "立秋", "处暑", "白露", "秋分", "寒露", "霜降",
		"立冬", "小雪", "大雪", "冬至"
	};

	private static readonly int[] TermInfo = new int[24]
	{
		0, 21208, 42467, 63836, 85337, 107014, 128867, 150921, 173149, 195551,
		218072, 240693, 263343, 285989, 308563, 331033, 353350, 375494, 397447, 419210,
		440795, 462224, 483532, 504758
	};

	public static readonly string[] Gan = new string[10] { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };

	public static readonly string[] Zhi = new string[12]
	{
		"子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉",
		"戌", "亥"
	};

	public static readonly string[] Animals = new string[12]
	{
		"鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡",
		"狗", "猪"
	};

	public static readonly string[] MonthNames = new string[12]
	{
		"正", "二", "三", "四", "五", "六", "七", "八", "九", "十",
		"冬", "腊"
	};

	public static readonly string[] DayNames = new string[30]
	{
		"初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
		"十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
		"廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
	};

	public const int MinYear = 1900;

	public const int MaxYear = 2100;

	public static readonly string[] WeekdayNames = new string[7] { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };

	public static int LeapMonth(int year)
	{
		return Info[year - 1900] & 0xF;
	}

	public static int LeapDays(int year)
	{
		if (LeapMonth(year) == 0)
		{
			return 0;
		}
		if ((Info[year - 1900] & 0x10000) == 0)
		{
			return 29;
		}
		return 30;
	}

	public static int MonthDays(int year, int month)
	{
		if (month < 1 || month > 12)
		{
			return 30;
		}
		if ((Info[year - 1900] & (65536 >> month)) == 0)
		{
			return 29;
		}
		return 30;
	}

	public static string MonthName(int month)
	{
		if (month < 1 || month > 12)
		{
			return string.Empty;
		}
		return MonthNames[month - 1];
	}

	public static string DayName(int day)
	{
		if (day < 1 || day > 30)
		{
			return string.Empty;
		}
		return DayNames[day - 1];
	}

	public static LunarDate FromSolar(DateTime date)
	{
		LunarDate lunarDate = default(LunarDate);
		lunarDate.Year = 1900;
		lunarDate.Month = 1;
		lunarDate.Day = 1;
		lunarDate.IsLeapMonth = false;
		LunarDate result = lunarDate;
		if (date < new DateTime(1900, 1, 31) || date > new DateTime(2100, 12, 31))
		{
			return result;
		}
		int num = (int)(date.Date - new DateTime(1900, 1, 31)).TotalDays;
		int i = 1900;
		int num2 = 0;
		for (; i < 2101; i++)
		{
			if (num <= 0)
			{
				break;
			}
			num2 = YearDays(i);
			if (num < num2)
			{
				break;
			}
			num -= num2;
		}
		if (num < 0)
		{
			i--;
			num += YearDays(i);
		}
		int num3 = LeapMonth(i);
		bool flag = false;
		int j;
		for (j = 1; j < 13; j++)
		{
			if (num < 0)
			{
				break;
			}
			if (num3 > 0 && j == num3 + 1 && !flag)
			{
				j--;
				flag = true;
				num2 = LeapDays(i);
			}
			else
			{
				num2 = MonthDays(i, j);
			}
			if (flag && j == num3 + 1)
			{
				flag = false;
			}
			if (num < num2)
			{
				break;
			}
			num -= num2;
		}
		if (j > 12)
		{
			j = 1;
			i++;
		}
		result.Year = i;
		result.Month = j;
		result.IsLeapMonth = flag;
		result.Day = num + 1;
		if (result.Day < 1)
		{
			result.Day = 1;
		}
		return result;
	}

	public static int YearDays(int year)
	{
		int num = 348;
		for (int i = 1; i <= 12; i++)
		{
			if ((Info[year - 1900] & (65536 >> i)) != 0)
			{
				num++;
			}
		}
		return num + LeapDays(year);
	}

	public static DateTime ToSolar(int lunarYear, int lunarMonth, int lunarDay, bool leapMonth)
	{
		if (lunarYear < 1900 || lunarYear > 2100)
		{
			lunarYear = 2024;
		}
		int num = 0;
		for (int i = 1900; i < lunarYear; i++)
		{
			num += YearDays(i);
		}
		int num2 = LeapMonth(lunarYear);
		for (int j = 1; j < lunarMonth; j++)
		{
			num += MonthDays(lunarYear, j);
			if (num2 > 0 && j == num2 && leapMonth)
			{
				break;
			}
		}
		if (leapMonth && num2 == lunarMonth)
		{
			num += MonthDays(lunarYear, lunarMonth);
		}
		else if (num2 > 0 && num2 > 0 && num2 < lunarMonth)
		{
			num += LeapDays(lunarYear);
		}
		num += lunarDay - 1;
		return new DateTime(1900, 1, 31).AddDays(num);
	}

	public static string TermName(DateTime date)
	{
		int year = date.Year;
		int month = date.Month;
		if (year < 1900 || year > 2100)
		{
			return null;
		}
		for (int i = (month - 1) * 2; i <= (month - 1) * 2 + 1; i++)
		{
			if (TermDay(year, i) == date.Day)
			{
				return Terms[i];
			}
		}
		return null;
	}

	public static int TermDay(int year, int termIndex)
	{
		long num = (long)(31556925974.7 * (double)(year - 1900) + (double)TermInfo[termIndex] * 60000.0 + new DateTime(1900, 1, 6, 2, 5, 0, DateTimeKind.Utc).Subtract(DateTime.MinValue).TotalMilliseconds) - 62135596800000L;
		return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(num).Day;
	}

	public static string TermOfMonth(int year, int month, out int day)
	{
		day = TermDay(year, (month - 1) * 2);
		return Terms[(month - 1) * 2];
	}

	public static string NextTermName(DateTime from, out DateTime date)
	{
		date = from.Date.AddDays(1.0);
		for (int i = 0; i < 40; i++)
		{
			string text = TermName(date);
			if (!string.IsNullOrEmpty(text))
			{
				return text;
			}
			date = date.AddDays(1.0);
		}
		date = from.Date.AddDays(1.0);
		return null;
	}

	public static string FormatSolarTermForMenu(DateTime date)
	{
		string text = TermName(date);
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		return null;
	}

	public static string ToChinese(DateTime date)
	{
		LunarDate lunarDate = FromSolar(date);
		return lunarDate.MonthText + lunarDate.DayText;
	}

	public static string Describe(DateTime date)
	{
		LunarDate lunarDate = FromSolar(date);
		return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", lunarDate.YearText, lunarDate.MonthText, lunarDate.DayText);
	}

	public static string WeekName(DateTime date)
	{
		int num = (int)(date.DayOfWeek + 6) % 7;
		return WeekdayNames[num];
	}

	public static string WeekNameShort(DateTime date)
	{
		int num = (int)(date.DayOfWeek + 6) % 7;
		if (!(MonthNames[0] == "正"))
		{
			return string.Empty;
		}
		return (new string[7] { "一", "二", "三", "四", "五", "六", "日" })[num];
	}
}
