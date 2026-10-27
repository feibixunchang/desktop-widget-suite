using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace Shared;

[DataContract]
internal sealed class NoteBook
{
	[DataMember]
	public List<NoteItem> Notes { get; set; }

	public NoteBook()
	{
		Notes = new List<NoteItem>();
	}

	public List<NoteItem> OnDay(DateTime date)
	{
		string b2 = date.ToString("yyyy-MM-dd");
		List<NoteItem> list = new List<NoteItem>();
		foreach (NoteItem note in Notes)
		{
			if (string.Equals(note.Date, b2, StringComparison.Ordinal))
			{
				list.Add(note);
			}
		}
		list.Sort(delegate(NoteItem a, NoteItem b)
		{
			int num = ((!string.IsNullOrEmpty(a.Time)) ? ParseTime(a.Time) : 0);
			int num2 = ((!string.IsNullOrEmpty(b.Time)) ? ParseTime(b.Time) : 0);
			return (num != num2) ? num.CompareTo(num2) : a.CreatedAt.CompareTo(b.CreatedAt);
		});
		return list;
	}

	public bool HasNote(DateTime date)
	{
		string b = date.ToString("yyyy-MM-dd");
		foreach (NoteItem note in Notes)
		{
			if (string.Equals(note.Date, b, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	public void Add(NoteItem item)
	{
		if (string.IsNullOrEmpty(item.Id))
		{
			item.Id = Guid.NewGuid().ToString("N");
		}
		if (item.CreatedAt == 0L)
		{
			item.CreatedAt = DateTime.Now.Ticks;
		}
		Notes.Add(item);
	}

	public void Remove(string id)
	{
		Notes.RemoveAll((NoteItem item) => item.Id == id);
	}

	public NoteItem Find(string id)
	{
		foreach (NoteItem note in Notes)
		{
			if (note.Id == id)
			{
				return note;
			}
		}
		return null;
	}

	private static int ParseTime(string time)
	{
		if (DateTime.TryParseExact(time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
		{
			return result.Hour * 60 + result.Minute;
		}
		return 0;
	}
}
