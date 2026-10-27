using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopMonitor;

internal static class PlaceCatalog
{
	private sealed class ScoredPlace
	{
		public PlaceEntry Entry { get; }

		public int Score { get; }

		public ScoredPlace(PlaceEntry entry, int score)
		{
			Entry = entry;
			Score = score;
		}
	}

	private const string ResourceName = "Places.txt";

	private static readonly Lazy<List<PlaceEntry>> Entries = new Lazy<List<PlaceEntry>>(Load, isThreadSafe: true);

	private static readonly char[] TrimmableSuffixes = new char[6] { '市', '县', '区', '镇', '乡', '盟' };

	public static int Count => Entries.Value.Count;

	public static string Normalize(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		string text2 = text.Trim();
		while (text2.Length > 2 && Array.IndexOf(TrimmableSuffixes, text2[text2.Length - 1]) >= 0)
		{
			text2 = text2.Substring(0, text2.Length - 1);
		}
		return text2;
	}

	public static string Shorten(string text)
	{
		return Normalize(text);
	}

	public static List<CityInfo> Search(string keyword, int limit)
	{
		List<ScoredPlace> list = new List<ScoredPlace>();
		string text = Normalize(keyword);
		if (text.Length == 0)
		{
			return new List<CityInfo>();
		}
		foreach (PlaceEntry item2 in Entries.Value)
		{
			int num = Score(item2, text);
			if (num >= 0)
			{
				list.Add(new ScoredPlace(item2, num));
			}
		}
		list.Sort(delegate(ScoredPlace left, ScoredPlace right)
		{
			int num2 = left.Score.CompareTo(right.Score);
			if (num2 != 0)
			{
				return num2;
			}
			num2 = left.Entry.Priority.CompareTo(right.Entry.Priority);
			if (num2 != 0)
			{
				return num2;
			}
			num2 = right.Entry.Population.CompareTo(left.Entry.Population);
			return (num2 == 0) ? string.Compare(left.Entry.Name, right.Entry.Name, StringComparison.Ordinal) : num2;
		});
		List<CityInfo> list2 = new List<CityInfo>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		foreach (ScoredPlace item3 in list)
		{
			if (list2.Count >= limit)
			{
				break;
			}
			string item = item3.Entry.Province + "|" + item3.Entry.NormalizedName;
			if (hashSet.Add(item))
			{
				list2.Add(item3.Entry.ToCityInfo());
			}
		}
		return list2;
	}

	private static int Score(PlaceEntry entry, string query)
	{
		string normalizedName = entry.NormalizedName;
		if (normalizedName.Length == 0)
		{
			return -1;
		}
		if (normalizedName == query)
		{
			return 0;
		}
		if (normalizedName.StartsWith(query, StringComparison.Ordinal))
		{
			return 1;
		}
		if (normalizedName.Contains(query))
		{
			return 2;
		}
		if (entry.NormalizedArea.Length > 0 && entry.NormalizedArea.Contains(query))
		{
			return 3;
		}
		return -1;
	}

	private static List<PlaceEntry> Load()
	{
		List<PlaceEntry> list = new List<PlaceEntry>(20000);
		using Stream stream = typeof(PlaceCatalog).Assembly.GetManifestResourceStream("Places.txt");
		if (stream == null)
		{
			return list;
		}
		using StreamReader streamReader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		string text;
		while ((text = streamReader.ReadLine()) != null)
		{
			if (text.Length != 0)
			{
				string[] array = text.Split(new char[1] { '\t' });
				if (array.Length >= 7 && int.TryParse(array[3], out var result) && int.TryParse(array[4], out var result2) && double.TryParse(array[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var result3) && double.TryParse(array[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
				{
					list.Add(new PlaceEntry(array[0], array[1], array[2], result, result2, result3, result4));
				}
			}
		}
		return list;
	}
}
