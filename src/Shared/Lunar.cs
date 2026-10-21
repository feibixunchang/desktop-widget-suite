using System;
using System.Globalization;

namespace Shared
{
    internal struct LunarDate
    {
        public int Year;
        public int Month;
        public int Day;
        public bool IsLeapMonth;

        public int MonthDays
        {
            get { return Lunar.MonthDays(Year, Month); }
        }

        public string MonthText
        {
            get
            {
                string name = Lunar.MonthName(Month);
                return (IsLeapMonth ? "闰" : string.Empty) + name + "月";
            }
        }

        public string DayText
        {
            get { return Lunar.DayName(Day); }
        }

        public string YearText
        {
            get
            {
                string gan = Lunar.Gan[((Year - 4) % 10 + 10) % 10];
                string zhi = Lunar.Zhi[((Year - 4) % 12 + 12) % 12];
                return gan + zhi + "年（" + Lunar.Animals[((Year - 4) % 12 + 12) % 12] + "年）";
            }
        }

        /// <summary>干支生肖缩写，例如「丙午马年」，省掉括号省空间。</summary>
        public string ShortYearText
        {
            get
            {
                string gan = Lunar.Gan[((Year - 4) % 10 + 10) % 10];
                string zhi = Lunar.Zhi[((Year - 4) % 12 + 12) % 12];
                return gan + zhi + Lunar.Animals[((Year - 4) % 12 + 12) % 12] + "年";
            }
        }

        public override string ToString()
        {
            return MonthText + DayText;
        }
    }

    internal static class Lunar
    {
        private static readonly int[] Info =
        {
            0x04bd8, 0x04ae0, 0x0a570, 0x054d5, 0x0d260, 0x0d950, 0x16554, 0x056a0, 0x09ad0, 0x055d2,
            0x04ae0, 0x0a5b6, 0x0a4d0, 0x0d250, 0x1d255, 0x0b540, 0x0d6a0, 0x0ada2, 0x095b0, 0x14977,
            0x04970, 0x0a4b0, 0x0b4b5, 0x06a50, 0x06d40, 0x1ab54, 0x02b60, 0x09570, 0x052f2, 0x04970,
            0x06566, 0x0d4a0, 0x0ea50, 0x06e95, 0x05ad0, 0x02b60, 0x186e3, 0x092e0, 0x1c8d7, 0x0c950,
            0x0d4a0, 0x1d8a6, 0x0b550, 0x056a0, 0x1a5b4, 0x025d0, 0x092d0, 0x0d2b2, 0x0a950, 0x0b557,
            0x06ca0, 0x0b550, 0x15355, 0x04da0, 0x0a5b0, 0x14573, 0x052b0, 0x0a9a8, 0x0e950, 0x06aa0,
            0x0aea6, 0x0ab50, 0x04b60, 0x0aae4, 0x0a570, 0x05260, 0x0f263, 0x0d950, 0x05b57, 0x056a0,
            0x096d0, 0x04dd5, 0x04ad0, 0x0a4d0, 0x0d4d4, 0x0d250, 0x0d558, 0x0b540, 0x0b6a0, 0x195a6,
            0x095b0, 0x049b0, 0x0a974, 0x0a4b0, 0x0b27a, 0x06a50, 0x06d40, 0x0af46, 0x0ab60, 0x09570,
            0x04af5, 0x04970, 0x064b0, 0x074a3, 0x0ea50, 0x06b58, 0x055c0, 0x0ab60, 0x096d5, 0x092e0,
            0x0c960, 0x0d954, 0x0d4a0, 0x0da50, 0x07552, 0x056a0, 0x0abb7, 0x025d0, 0x092d0, 0x0cab5,
            0x0a950, 0x0b4a0, 0x0baa4, 0x0ad50, 0x055d9, 0x04ba0, 0x0a5b0, 0x15176, 0x052b0, 0x0a930,
            0x07954, 0x06aa0, 0x0ad50, 0x05b52, 0x04b60, 0x0a6e6, 0x0a4e0, 0x0d260, 0x0ea65, 0x0d530,
            0x05aa0, 0x076a3, 0x096d0, 0x04afb, 0x04ad0, 0x0a4d0, 0x1d0b6, 0x0d250, 0x0d520, 0x0dd45,
            0x0b5a0, 0x056d0, 0x055b2, 0x049b0, 0x0a577, 0x0a4b0, 0x0aa50, 0x1b255, 0x06d20, 0x0ada0,
            0x14b63, 0x09370, 0x049f8, 0x04970, 0x064b0, 0x168a6, 0x0ea50, 0x06b20, 0x1a6c4, 0x0aae0,
            0x0a2e0, 0x0d2e3, 0x0c960, 0x0d557, 0x0d4a0, 0x0da50, 0x05d55, 0x056a0, 0x0a6d0, 0x055d4,
            0x052d0, 0x0a9b8, 0x0a950, 0x0b4a0, 0x0b6a6, 0x0ad50, 0x055a0, 0x0aba4, 0x0a5b0, 0x052b0,
            0x0b273, 0x06930, 0x07337, 0x06aa0, 0x0ad50, 0x14b55, 0x04b60, 0x0a570, 0x054e4, 0x0d160,
            0x0e968, 0x0d520, 0x0daa0, 0x16aa6, 0x056d0, 0x04ae0, 0x0a9d4, 0x0a2d0, 0x0d150, 0x0f252,
            0x0d520
        };

        private static readonly string[] Terms =
        {
            "小寒", "大寒", "立春", "雨水", "惊蛰", "春分", "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
            "小暑", "大暑", "立秋", "处暑", "白露", "秋分", "寒露", "霜降", "立冬", "小雪", "大雪", "冬至"
        };

        private static readonly int[] TermInfo =
        {
            0, 21208, 42467, 63836, 85337, 107014, 128867, 150921, 173149, 195551, 218072, 240693,
            263343, 285989, 308563, 331033, 353350, 375494, 397447, 419210, 440795, 462224, 483532, 504758
        };

        public static readonly string[] Gan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
        public static readonly string[] Zhi = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        public static readonly string[] Animals = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };
        public static readonly string[] MonthNames = { "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };
        public static readonly string[] DayNames =
        {
            "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
        };

        public const int MinYear = 1900;
        public const int MaxYear = 2100;

        public static int LeapMonth(int year)
        {
            return Info[year - MinYear] & 0xf;
        }

        public static int LeapDays(int year)
        {
            if (LeapMonth(year) == 0) { return 0; }
            return (Info[year - MinYear] & 0x10000) != 0 ? 30 : 29;
        }

        public static int MonthDays(int year, int month)
        {
            if (month < 1 || month > 12) { return 30; }
            return (Info[year - MinYear] & (0x10000 >> month)) != 0 ? 30 : 29;
        }

        public static string MonthName(int month)
        {
            if (month < 1 || month > 12) { return string.Empty; }
            return MonthNames[month - 1];
        }

        public static string DayName(int day)
        {
            if (day < 1 || day > 30) { return string.Empty; }
            return DayNames[day - 1];
        }

        public static LunarDate FromSolar(DateTime date)
        {
            LunarDate result = new LunarDate { Year = MinYear, Month = 1, Day = 1, IsLeapMonth = false };
            if (date < new DateTime(MinYear, 1, 31) || date > new DateTime(MaxYear, 12, 31))
            {
                return result;
            }

            int offset = (int)(date.Date - new DateTime(MinYear, 1, 31)).TotalDays;
            int year = MinYear;
            int days = 0;
            for (; year < MaxYear + 1 && offset > 0; year++)
            {
                days = YearDays(year);
                if (offset < days) { break; }
                offset -= days;
            }

            if (offset < 0)
            {
                year--;
                offset += YearDays(year);
            }

            int leap = LeapMonth(year);
            bool leapUsed = false;
            int month = 1;
            for (; month < 13 && offset >= 0; month++)
            {
                if (leap > 0 && month == leap + 1 && !leapUsed)
                {
                    month--;
                    leapUsed = true;
                    days = LeapDays(year);
                }
                else
                {
                    days = MonthDays(year, month);
                }

                if (leapUsed && month == leap + 1) { leapUsed = false; }
                if (offset < days) { break; }
                offset -= days;
            }

            if (month > 12)
            {
                month = 1;
                year++;
            }

            result.Year = year;
            result.Month = month;
            result.IsLeapMonth = leapUsed;
            result.Day = offset + 1;
            if (result.Day < 1) { result.Day = 1; }
            return result;
        }

        public static int YearDays(int year)
        {
            int days = 348;
            for (int month = 1; month <= 12; month++)
            {
                if ((Info[year - MinYear] & (0x10000 >> month)) != 0) { days++; }
            }

            return days + LeapDays(year);
        }

        public static DateTime ToSolar(int lunarYear, int lunarMonth, int lunarDay, bool leapMonth)
        {
            if (lunarYear < MinYear || lunarYear > MaxYear) { lunarYear = 2024; }
            int offset = 0;
            for (int year = MinYear; year < lunarYear; year++)
            {
                offset += YearDays(year);
            }

            int leap = LeapMonth(lunarYear);
            for (int month = 1; month < lunarMonth; month++)
            {
                offset += MonthDays(lunarYear, month);
                if (leap > 0 && month == leap && leapMonth) { break; }
            }

            if (leapMonth && leap == lunarMonth)
            {
                offset += MonthDays(lunarYear, lunarMonth);
            }
            else if (leap > 0 && leap > 0 && leap < lunarMonth)
            {
                offset += LeapDays(lunarYear);
            }

            offset += lunarDay - 1;
            return new DateTime(MinYear, 1, 31).AddDays(offset);
        }

        public static string TermName(DateTime date)
        {
            int year = date.Year;
            int month = date.Month;
            if (year < 1900 || year > 2100) { return null; }
            for (int index = (month - 1) * 2; index <= (month - 1) * 2 + 1; index++)
            {
                if (TermDay(year, index) == date.Day) { return Terms[index]; }
            }

            return null;
        }

        public static int TermDay(int year, int termIndex)
        {
            double milliseconds = 31556925974.7 * (year - 1900) + TermInfo[termIndex] * 60000.0
                + new DateTime(1900, 1, 6, 2, 5, 0, DateTimeKind.Utc).Subtract(DateTime.MinValue).TotalMilliseconds;
            long unix = (long)milliseconds - 62135596800000L;
            DateTime utc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(unix);
            return utc.Day;
        }

        public static string TermOfMonth(int year, int month, out int day)
        {
            day = TermDay(year, (month - 1) * 2);
            return Terms[(month - 1) * 2];
        }

        public static string NextTermName(DateTime from, out DateTime date)
        {
            date = from.Date.AddDays(1);
            for (int step = 0; step < 40; step++)
            {
                string name = TermName(date);
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }

                date = date.AddDays(1);
            }

            date = from.Date.AddDays(1);
            return null;
        }

        public static string FormatSolarTermForMenu(DateTime date)
        {
            string name = TermName(date);
            return string.IsNullOrEmpty(name) ? null : name;
        }

        public static string ToChinese(DateTime date)
        {
            LunarDate lunar = FromSolar(date);
            return lunar.MonthText + lunar.DayText;
        }

        public static string Describe(DateTime date)
        {
            LunarDate lunar = FromSolar(date);
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", lunar.YearText, lunar.MonthText, lunar.DayText);
        }

        public static readonly string[] WeekdayNames = { "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };

        public static string WeekName(DateTime date)
        {
            int index = ((int)date.DayOfWeek + 6) % 7;
            return WeekdayNames[index];
        }

        public static string WeekNameShort(DateTime date)
        {
            int index = ((int)date.DayOfWeek + 6) % 7;
            return MonthNames[0] == "正" ? new[] { "一", "二", "三", "四", "五", "六", "日" }[index] : string.Empty;
        }
    }
}
