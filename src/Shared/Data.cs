using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Shared
{
    [DataContract]
    internal sealed class WidgetSettings
    {
        [DataMember] public int X { get; set; }
        [DataMember] public int Y { get; set; }
        [DataMember] public int FontSize { get; set; }
        [DataMember] public bool TopMost { get; set; }
        [DataMember] public bool Locked { get; set; }
          [DataMember] public int Opacity { get; set; }          [DataMember] public bool FirstRun { get; set; }

          /// <summary>贴在桌面底层（桌面图标后面），只在桌面层上显示。</summary>
          [DataMember] public bool? DesktopLayer { get; set; }

          /// <summary>
          /// 有全屏/最大化窗口在前台时自动隐藏，不挡着别人干活。
          /// 用可空类型：老设置里没这一项（null）时按"开启"处理，符合"挂件不遮挡全屏窗口"的原则。
          /// </summary>
          [DataMember] public bool? HideOnFullscreen { get; set; }
    }

    internal static class AppPaths
    {
        private static string appId = "DesktopWidget";

        public static string AppId
        {
            get { return appId; }
            set { appId = string.IsNullOrEmpty(value) ? "DesktopWidget" : value; }
        }

        public static string DataDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string path = Path.Combine(root, AppId);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                return path;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(DataDirectory, "settings.json"); }
        }
    }

    internal static class JsonStore
    {
        public static void Save<T>(string path, T value)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                string temp = path + ".tmp";
                using (var stream = File.Create(temp))
                {
                    serializer.WriteObject(stream, value);
                }

                if (File.Exists(path)) { File.Delete(path); }
                File.Move(temp, path);
            }
            catch
            {
            }
        }

        public static T Load<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) { return default(T); }
                var serializer = new DataContractJsonSerializer(typeof(T));
                using (var stream = File.OpenRead(path))
                {
                    return (T)serializer.ReadObject(stream);
                }
            }
            catch
            {
                return default(T);
            }
        }
    }

    internal sealed class WidgetSettingsStore
    {
        private readonly string path;

        public WidgetSettingsStore(string filePath)
        {
            path = filePath;
            Settings = JsonStore.Load<WidgetSettings>(path) ?? new WidgetSettings();
        }

        public WidgetSettings Settings { get; private set; }

        public void Save()
        {
            JsonStore.Save(path, Settings);
        }
    }

    [DataContract]
    internal sealed class NoteItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Date { get; set; }
        [DataMember] public string Time { get; set; }
        [DataMember] public string Content { get; set; }
        [DataMember] public string Color { get; set; }
        [DataMember] public long CreatedAt { get; set; }
    }

    [DataContract]
    internal sealed class NoteBook
    {
        [DataMember] public List<NoteItem> Notes { get; set; }

        public NoteBook()
        {
            Notes = new List<NoteItem>();
        }

        public List<NoteItem> OnDay(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            var result = new List<NoteItem>();
            foreach (NoteItem item in Notes)
            {
                if (string.Equals(item.Date, key, StringComparison.Ordinal))
                {
                    result.Add(item);
                }
            }

            result.Sort(delegate(NoteItem a, NoteItem b)
            {
                int left = string.IsNullOrEmpty(a.Time) ? 0 : ParseTime(a.Time);
                int right = string.IsNullOrEmpty(b.Time) ? 0 : ParseTime(b.Time);
                if (left != right) { return left.CompareTo(right); }
                return a.CreatedAt.CompareTo(b.CreatedAt);
            });
            return result;
        }

        public bool HasNote(DateTime date)
        {
            string key = date.ToString("yyyy-MM-dd");
            foreach (NoteItem item in Notes)
            {
                if (string.Equals(item.Date, key, StringComparison.Ordinal)) { return true; }
            }

            return false;
        }

        public void Add(NoteItem item)
        {
            if (string.IsNullOrEmpty(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            if (item.CreatedAt == 0)
            {
                item.CreatedAt = DateTime.Now.Ticks;
            }

            Notes.Add(item);
        }

        public void Remove(string id)
        {
            Notes.RemoveAll(delegate(NoteItem item) { return item.Id == id; });
        }

        public NoteItem Find(string id)
        {
            foreach (NoteItem item in Notes)
            {
                if (item.Id == id) { return item; }
            }

            return null;
        }

        private static int ParseTime(string time)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out parsed))
            {
                return parsed.Hour * 60 + parsed.Minute;
            }

            return 0;
        }
    }

    [DataContract]
    internal sealed class RecordItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Date { get; set; }
        [DataMember] public string Type { get; set; }
        [DataMember] public string Category { get; set; }
        [DataMember] public double Amount { get; set; }
        [DataMember] public string Note { get; set; }
        [DataMember] public long CreatedAt { get; set; }
    }

    [DataContract]
    internal sealed class WalletBook
    {
        [DataMember] public List<RecordItem> Records { get; set; }
        [DataMember] public List<string> ExpenseCategories { get; set; }
        [DataMember] public List<string> IncomeCategories { get; set; }
        [DataMember] public double MonthlyBudget { get; set; }

        public WalletBook()
        {
            Records = new List<RecordItem>();
            ExpenseCategories = new List<string>
            {
                "餐饮", "交通", "购物", "居住", "通讯", "娱乐", "医疗", "学习",
                "生活", "杂费", "人情", "美容", "服饰", "宠物", "旅行", "数码", "其他支出"
            };
            IncomeCategories = new List<string>
            {
                "工资", "奖金", "理财", "红包", "兼职", "退款", "其他收入"
            };
            MonthlyBudget = 0d;
        }

        public IEnumerable<RecordItem> InRange(DateTime from, DateTime to)
        {
            string start = from.ToString("yyyy-MM-dd");
            string end = to.ToString("yyyy-MM-dd");
            var result = new List<RecordItem>();
            foreach (RecordItem item in Records)
            {
                if (string.Compare(item.Date, start, StringComparison.Ordinal) >= 0 &&
                    string.Compare(item.Date, end, StringComparison.Ordinal) <= 0)
                {
                    result.Add(item);
                }
            }

            result.Sort(delegate(RecordItem a, RecordItem b)
            {
                int compared = string.Compare(b.Date, a.Date, StringComparison.Ordinal);
                if (compared != 0) { return compared; }
                return b.CreatedAt.CompareTo(a.CreatedAt);
            });
            return result;
        }

        public void Add(RecordItem item)
        {
            if (string.IsNullOrEmpty(item.Id)) { item.Id = Guid.NewGuid().ToString("N"); }
            if (item.CreatedAt == 0) { item.CreatedAt = DateTime.Now.Ticks; }
            Records.Add(item);
        }

        public void Remove(string id)
        {
            Records.RemoveAll(delegate(RecordItem item) { return item.Id == id; });
        }

        public RecordItem Find(string id)
        {
            foreach (RecordItem item in Records)
            {
                if (item.Id == id) { return item; }
            }

            return null;
        }

        public static double Sum(IEnumerable<RecordItem> items, string type)
        {
            double total = 0d;
            foreach (RecordItem item in items)
            {
                if (string.Equals(item.Type, type, StringComparison.Ordinal)) { total += item.Amount; }
            }

            return total;
        }
    }
}
