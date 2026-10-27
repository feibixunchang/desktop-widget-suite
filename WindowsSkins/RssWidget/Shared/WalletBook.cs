using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shared;

[DataContract]
internal sealed class WalletBook
{
	[DataMember]
	public List<RecordItem> Records { get; set; }

	[DataMember]
	public List<string> ExpenseCategories { get; set; }

	[DataMember]
	public List<string> IncomeCategories { get; set; }

	[DataMember]
	public double MonthlyBudget { get; set; }

	public WalletBook()
	{
		Records = new List<RecordItem>();
		ExpenseCategories = new List<string>
		{
			"餐饮", "交通", "购物", "居住", "通讯", "娱乐", "医疗", "学习", "生活", "杂费",
			"人情", "美容", "服饰", "宠物", "旅行", "数码", "其他支出"
		};
		IncomeCategories = new List<string> { "工资", "奖金", "理财", "红包", "兼职", "退款", "其他收入" };
		MonthlyBudget = 0.0;
	}

	public IEnumerable<RecordItem> InRange(DateTime from, DateTime to)
	{
		string strB = from.ToString("yyyy-MM-dd");
		string strB2 = to.ToString("yyyy-MM-dd");
		List<RecordItem> list = new List<RecordItem>();
		foreach (RecordItem record in Records)
		{
			if (string.Compare(record.Date, strB, StringComparison.Ordinal) >= 0 && string.Compare(record.Date, strB2, StringComparison.Ordinal) <= 0)
			{
				list.Add(record);
			}
		}
		list.Sort(delegate(RecordItem a, RecordItem b)
		{
			int num = string.Compare(b.Date, a.Date, StringComparison.Ordinal);
			return (num != 0) ? num : b.CreatedAt.CompareTo(a.CreatedAt);
		});
		return list;
	}

	public void Add(RecordItem item)
	{
		if (string.IsNullOrEmpty(item.Id))
		{
			item.Id = Guid.NewGuid().ToString("N");
		}
		if (item.CreatedAt == 0L)
		{
			item.CreatedAt = DateTime.Now.Ticks;
		}
		Records.Add(item);
	}

	public void Remove(string id)
	{
		Records.RemoveAll((RecordItem item) => item.Id == id);
	}

	public RecordItem Find(string id)
	{
		foreach (RecordItem record in Records)
		{
			if (record.Id == id)
			{
				return record;
			}
		}
		return null;
	}

	public static double Sum(IEnumerable<RecordItem> items, string type)
	{
		double num = 0.0;
		foreach (RecordItem item in items)
		{
			if (string.Equals(item.Type, type, StringComparison.Ordinal))
			{
				num += item.Amount;
			}
		}
		return num;
	}
}
