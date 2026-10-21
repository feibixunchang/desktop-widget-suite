using System;
using System.Collections.Generic;

namespace Shared
{
    internal enum DayBadge
    {
        None,
        Rest,
        Work
    }

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

        private static readonly List<SolarFestival> SolarList = BuildSolar();
        private static readonly List<WeekdayFestival> WeekdayList = BuildWeekday();
        private static readonly List<LunarFestival> LunarList = BuildLunar();

        // 法定节假日（由《全国年节及纪念日放假办法》确定，算法生成，永远正确）：
        //   元旦 1/1；春节 除夕+正月初一至初三；清明 4/4或4/5；劳动节 5/1-5/3；
        //   端午 农历五月初五；中秋 农历八月十五；国庆 10/1-10/3。
        //
        // 连休与调休（放几天、周末哪天补班）由国务院每年11月单独公布，无法用公式推导，
        // 因此放在下面的表里，需要每年更新一次。格式：日期, 类型(R=休息, W=补班上班)
        private static readonly Dictionary<int, List<Adjustment>> Adjustments = BuildAdjustments();

        private sealed class Adjustment
        {
            public DateTime Date;
            public DayBadge Badge;
        }

        private static Dictionary<int, List<Adjustment>> BuildAdjustments()
        {
            var result = new Dictionary<int, List<Adjustment>>();
            result[2025] = Parse(
                "2025-01-01,R", "2025-01-28,R", "2025-01-29,R", "2025-01-30,R", "2025-01-31,R",
                "2025-02-01,R", "2025-02-02,R", "2025-02-03,R", "2025-02-04,R",
                "2025-04-04,R", "2025-04-05,R", "2025-04-06,R",
                "2025-05-01,R", "2025-05-02,R", "2025-05-03,R", "2025-05-04,R", "2025-05-05,R",
                "2025-05-31,R", "2025-06-01,R", "2025-06-02,R",
                "2025-10-01,R", "2025-10-02,R", "2025-10-03,R", "2025-10-04,R", "2025-10-05,R",
                "2025-10-06,R", "2025-10-07,R", "2025-10-08,R",
                "2025-01-26,W", "2025-02-08,W", "2025-04-27,W", "2025-09-28,W", "2025-10-11,W");
            result[2026] = Parse(
                "2026-01-01,R", "2026-01-02,R", "2026-01-03,R",
                "2026-02-15,R", "2026-02-16,R", "2026-02-17,R", "2026-02-18,R", "2026-02-19,R",
                "2026-02-20,R", "2026-02-21,R", "2026-02-22,R",
                "2026-04-04,R", "2026-04-05,R", "2026-04-06,R",
                "2026-05-01,R", "2026-05-02,R", "2026-05-03,R", "2026-05-04,R", "2026-05-05,R",
                "2026-06-19,R", "2026-06-20,R", "2026-06-21,R",
                "2026-09-19,W", "2026-09-25,R", "2026-09-26,R", "2026-09-27,R",
                "2026-10-01,R", "2026-10-02,R", "2026-10-03,R", "2026-10-04,R", "2026-10-05,R",
                "2026-10-06,R", "2026-10-07,R",
                "2026-02-14,W", "2026-02-28,W", "2026-10-10,W");
            result[2027] = Parse(
                "2027-01-01,R", "2027-01-02,R", "2027-01-03,R",
                "2027-02-06,R", "2027-02-07,R", "2027-02-08,R", "2027-02-09,R", "2027-02-10,R", "2027-02-11,R",
                "2027-04-03,R", "2027-04-04,R", "2027-04-05,R",
                "2027-05-01,R", "2027-05-02,R", "2027-05-03,R", "2027-05-04,R", "2027-05-05,R",
                "2027-06-09,R", "2027-06-10,R", "2027-06-11,R",
                "2027-09-15,R", "2027-09-16,R", "2027-09-17,R",
                "2027-10-01,R", "2027-10-02,R", "2027-10-03,R", "2027-10-04,R", "2027-10-05,R",
                "2027-10-06,R", "2027-10-07,R");
            return result;
        }

        private static List<Adjustment> Parse(params string[] items)
        {
            var list = new List<Adjustment>();
            foreach (string item in items)
            {
                string[] parts = item.Split(',');
                if (parts.Length != 2) { continue; }
                DateTime date;
                if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out date))
                {
                    continue;
                }

                list.Add(new Adjustment
                {
                    Date = date.Date,
                    Badge = string.Equals(parts[1].Trim(), "W", StringComparison.OrdinalIgnoreCase) ? DayBadge.Work : DayBadge.Rest
                });
            }

            return list;
        }

        private static List<SolarFestival> BuildSolar()
        {
            return new List<SolarFestival>
            {
                new SolarFestival { Month = 1, Day = 1, Name = "元旦", Major = true },
                new SolarFestival { Month = 2, Day = 14, Name = "情人节" },
                new SolarFestival { Month = 3, Day = 8, Name = "妇女节" },
                new SolarFestival { Month = 3, Day = 12, Name = "植树节" },
                new SolarFestival { Month = 3, Day = 15, Name = "消费者权益日" },
                new SolarFestival { Month = 4, Day = 1, Name = "愚人节" },
                new SolarFestival { Month = 4, Day = 22, Name = "地球日" },
                new SolarFestival { Month = 5, Day = 1, Name = "劳动节", Major = true },
                new SolarFestival { Month = 5, Day = 4, Name = "青年节" },
                new SolarFestival { Month = 5, Day = 12, Name = "护士节" },
                new SolarFestival { Month = 6, Day = 1, Name = "儿童节" },
                new SolarFestival { Month = 6, Day = 5, Name = "环境日" },
                new SolarFestival { Month = 6, Day = 6, Name = "爱眼日" },
                new SolarFestival { Month = 7, Day = 1, Name = "建党节" },
                new SolarFestival { Month = 8, Day = 1, Name = "建军节" },
                new SolarFestival { Month = 9, Day = 3, Name = "抗战胜利" },
                new SolarFestival { Month = 9, Day = 10, Name = "教师节" },
                new SolarFestival { Month = 9, Day = 18, Name = "九一八" },
                new SolarFestival { Month = 9, Day = 20, Name = "爱牙日" },
                new SolarFestival { Month = 10, Day = 1, Name = "国庆节", Major = true },
                new SolarFestival { Month = 10, Day = 4, Name = "动物日" },
                new SolarFestival { Month = 11, Day = 8, Name = "记者节" },
                new SolarFestival { Month = 12, Day = 4, Name = "宪法日" },
                new SolarFestival { Month = 12, Day = 13, Name = "公祭日" },
                new SolarFestival { Month = 12, Day = 24, Name = "平安夜" },
                new SolarFestival { Month = 12, Day = 25, Name = "圣诞节" }
            };
        }

        private static List<WeekdayFestival> BuildWeekday()
        {
            return new List<WeekdayFestival>
            {
                new WeekdayFestival { Month = 5, Week = 2, DayOfWeek = DayOfWeek.Sunday, Name = "母亲节" },
                new WeekdayFestival { Month = 6, Week = 3, DayOfWeek = DayOfWeek.Sunday, Name = "父亲节" },
                new WeekdayFestival { Month = 11, Week = 4, DayOfWeek = DayOfWeek.Thursday, Name = "感恩节" }
            };
        }

        private static List<LunarFestival> BuildLunar()
        {
            return new List<LunarFestival>
            {
                new LunarFestival { Month = 1, Day = 1, Name = "春节", Major = true },
                new LunarFestival { Month = 1, Day = 15, Name = "元宵节", Major = true },
                new LunarFestival { Month = 1, Day = 3, Name = "财神节" },
                new LunarFestival { Month = 2, Day = 2, Name = "龙抬头" },
                new LunarFestival { Month = 3, Day = 3, Name = "上巳节" },
                new LunarFestival { Month = 5, Day = 5, Name = "端午节", Major = true },
                new LunarFestival { Month = 7, Day = 7, Name = "七夕节" },
                new LunarFestival { Month = 7, Day = 15, Name = "中元节" },
                new LunarFestival { Month = 8, Day = 15, Name = "中秋节", Major = true },
                new LunarFestival { Month = 9, Day = 9, Name = "重阳节" },
                new LunarFestival { Month = 10, Day = 1, Name = "寒衣节" },
                new LunarFestival { Month = 10, Day = 15, Name = "下元节" },
                new LunarFestival { Month = 12, Day = 8, Name = "腊八节" },
                new LunarFestival { Month = 12, Day = 23, Name = "小年" },
                new LunarFestival { Month = 12, Day = 30, Name = "除夕", Major = true, Eve = true }
            };
        }

        public static string SolarFestivalName(int month, int day, out bool major)
        {
            foreach (SolarFestival item in SolarList)
            {
                if (item.Month == month && item.Day == day)
                {
                    major = item.Major;
                    return item.Name;
                }
            }

            major = false;
            return null;
        }

        public static string WeekdayFestivalName(DateTime date)
        {
            foreach (WeekdayFestival item in WeekdayList)
            {
                if (item.Month != date.Month || item.DayOfWeek != date.DayOfWeek) { continue; }
                if (NthWeekday(date) == item.Week)
                {
                    return item.Name;
                }
            }

            return null;
        }

        public static string LunarFestivalName(int lunarMonth, int lunarDay, int daysInMonth, out bool major)
        {
            major = false;
            foreach (LunarFestival item in LunarList)
            {
                if (item.Eve)
                {
                    if (lunarMonth == item.Month && lunarDay == daysInMonth)
                    {
                        major = true;
                        return item.Name;
                    }

                    continue;
                }

                if (item.Month == lunarMonth && item.Day == lunarDay)
                {
                    major = item.Major;
                    return item.Name;
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
            List<Adjustment> list;
            if (Adjustments.TryGetValue(date.Year, out list))
            {
                foreach (Adjustment item in list)
                {
                    if (item.Date == date) { return item.Badge; }
                }
            }

            if (IsStatutoryRest(date)) { return DayBadge.Rest; }
            return DayBadge.None;
        }

        public static bool IsStatutoryRest(DateTime date)
        {
            if (date.Month == 1 && date.Day == 1) { return true; }
            if (date.Month == 5 && date.Day >= 1 && date.Day <= 3) { return true; }
            if (date.Month == 10 && date.Day >= 1 && date.Day <= 3) { return true; }
            if (date.Month == 4 && (date.Day == 4 || date.Day == 5)) { return true; }

            LunarDate lunar = Lunar.FromSolar(date);
            if (lunar.Month == 1 && !lunar.IsLeapMonth)
            {
                if (lunar.Day >= 1 && lunar.Day <= 3) { return true; }
                int days = lunar.MonthDays;
                if (lunar.Day == days) { return true; }
            }

            if (lunar.Month == 5 && !lunar.IsLeapMonth && lunar.Day == 5) { return true; }
            if (lunar.Month == 8 && !lunar.IsLeapMonth && lunar.Day == 15) { return true; }
            return false;
        }

        public static bool IsWorkWeekend(DateTime date)
        {
            return Badge(date) == DayBadge.Work;
        }

        public static bool IsRestDay(DateTime date)
        {
            DayBadge badge = Badge(date);
            if (badge == DayBadge.Rest) { return true; }
            if (badge == DayBadge.Work) { return false; }
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }

        public static string DescribeDay(DateTime date)
        {
            var parts = new List<string>();
            bool major;
            string term = Lunar.TermName(date);
            if (!string.IsNullOrEmpty(term)) { parts.Add(term); }
            string solar = SolarFestivalName(date.Month, date.Day, out major);
            if (string.IsNullOrEmpty(solar)) { solar = WeekdayFestivalName(date); }
            LunarDate lunar = Lunar.FromSolar(date);
            bool lunarMajor;
            string lunarFestival = LunarFestivalName(lunar.Month, lunar.Day, lunar.MonthDays, out lunarMajor);
            if (!string.IsNullOrEmpty(lunarFestival)) { parts.Add(lunarFestival); }
            else if (!string.IsNullOrEmpty(solar)) { parts.Add(solar); }
            else { parts.Add(Lunar.ToChinese(date)); }
            return string.Join(" ", parts.ToArray());
        }

        public static string UpcomingFestival(DateTime from, out DateTime date, out int days)
        {
            for (int step = 0; step < 400; step++)
            {
                DateTime probe = from.Date.AddDays(step);
                bool major;
                string name = SolarFestivalName(probe.Month, probe.Day, out major);
                if (string.IsNullOrEmpty(name)) { name = LunarFestivalName(Lunar.FromSolar(probe).Month, Lunar.FromSolar(probe).Day, 0, out major); }
                if (string.IsNullOrEmpty(name)) { name = Lunar.TermName(probe); }
                if (!string.IsNullOrEmpty(name))
                {
                    date = probe;
                    days = step;
                    return name;
                }
            }

            date = from.Date;
            days = -1;
            return null;
        }
    }
}
