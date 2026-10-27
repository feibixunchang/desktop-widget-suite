using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RssWidgetApp;

internal static class Feeds
{
	private static readonly Regex TagPattern = new Regex("<[^>]+>", RegexOptions.Compiled);

	private static readonly Regex SpacePattern = new Regex("\\s+", RegexOptions.Compiled);

	public static List<FeedSource> Default()
	{
		return new List<FeedSource>
		{
			new FeedSource
			{
				Name = "少数派",
				Short = "少数派",
				Url = "https://sspai.com/feed",
				Category = "科技"
			},
			new FeedSource
			{
				Name = "V2EX",
				Short = "V2EX",
				Url = "https://www.v2ex.com/index.xml",
				Category = "科技"
			},
			new FeedSource
			{
				Name = "爱范儿",
				Short = "爱范儿",
				Url = "https://www.ifanr.com/feed",
				Category = "科技"
			},
			new FeedSource
			{
				Name = "IT之家",
				Short = "IT之家",
				Url = "https://ithome.com/rss",
				Category = "科技"
			},
			new FeedSource
			{
				Name = "知乎日报",
				Short = "知乎",
				Url = "https://rsshub.rssforever.com/zhihu/daily",
				Category = "科技"
			},
			new FeedSource
			{
				Name = "中新网即时新闻",
				Short = "中即",
				Url = "https://www.chinanews.com.cn/rss/scroll-news.xml",
				Category = "时事"
			},
			new FeedSource
			{
				Name = "中新网社会新闻",
				Short = "中社",
				Url = "https://www.chinanews.com.cn/rss/society.xml",
				Category = "社会"
			},
			new FeedSource
			{
				Name = "人民网社会",
				Short = "社会",
				Url = "http://www.people.com.cn/rss/society.xml",
				Category = "社会"
			},
			new FeedSource
			{
				Name = "小众软件",
				Short = "软件",
				Url = "https://www.appinn.com/feed/",
				Category = "软件"
			}
		};
	}

	public static string Clean(string html)
	{
		if (string.IsNullOrEmpty(html))
		{
			return string.Empty;
		}
		string input = html;
		input = Regex.Replace(input, "(?is)<(script|style)[^>]*>.*?</\\1>", " ");
		input = TagPattern.Replace(input, " ");
		input = input.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
			.Replace("&gt;", ">")
			.Replace("&quot;", "\"")
			.Replace("&#39;", "'")
			.Replace("&apos;", "'")
			.Replace("&ldquo;", "“")
			.Replace("&rdquo;", "”")
			.Replace("&mdash;", "—")
			.Replace("&hellip;", "…");
		return SpacePattern.Replace(input, " ").Trim();
	}

	private static string Decode(byte[] bytes, string contentType)
	{
		Encoding encoding = null;
		try
		{
			if (!string.IsNullOrEmpty(contentType))
			{
				Match match = Regex.Match(contentType, "charset=([^\";]+)", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					encoding = ResolveEncoding(match.Groups[1].Value.Trim());
				}
			}
			if (encoding == null)
			{
				Match match2 = Regex.Match(Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 200)), "encoding=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
				if (match2.Success)
				{
					encoding = ResolveEncoding(match2.Groups[1].Value.Trim());
				}
			}
		}
		catch
		{
		}
		return (encoding ?? Encoding.UTF8).GetString(bytes);
	}

	private static Encoding ResolveEncoding(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		try
		{
			switch (name.ToLowerInvariant().Replace("-", ""))
			{
			case "gb2312":
			case "gbk":
			case "gb18030":
			case "cp936":
			case "windows936":
				return Encoding.GetEncoding("GB18030");
			case "big5":
				return Encoding.GetEncoding("Big5");
			case "utf8":
				return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			default:
				return Encoding.GetEncoding(name);
			}
		}
		catch
		{
			return null;
		}
	}

	private static byte[] Download(string url, int timeoutMs, out string contentType)
	{
		HttpWebRequest obj = (HttpWebRequest)WebRequest.Create(url);
		obj.Method = "GET";
		obj.Timeout = timeoutMs;
		obj.ReadWriteTimeout = timeoutMs;
		obj.UserAgent = "RssWidget/1.0 (+desktop widget)";
		obj.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		obj.AllowAutoRedirect = true;
		obj.KeepAlive = false;
		obj.Proxy = WebRequest.GetSystemWebProxy();
		obj.Proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
		HttpWebResponse httpWebResponse = (HttpWebResponse)obj.GetResponse();
		try
		{
			contentType = httpWebResponse.Headers["Content-Type"];
			using Stream stream = httpWebResponse.GetResponseStream();
			if (stream == null)
			{
				return new byte[0];
			}
			using MemoryStream memoryStream = new MemoryStream();
			byte[] array = new byte[16384];
			int count;
			while ((count = stream.Read(array, 0, array.Length)) > 0)
			{
				memoryStream.Write(array, 0, count);
				if (memoryStream.Length > 4194304)
				{
					break;
				}
			}
			return memoryStream.ToArray();
		}
		finally
		{
			httpWebResponse.Close();
		}
	}

	private static string ChildText(XElement element, params string[] names)
	{
		XNamespace @namespace = element.Name.Namespace;
		foreach (string text in names)
		{
			XElement xElement = element.Element(@namespace + text) ?? element.Element(text);
			if (xElement != null && !string.IsNullOrEmpty(xElement.Value))
			{
				return xElement.Value;
			}
		}
		return null;
	}

	public static List<FeedItem> Fetch(FeedSource source, int maxItems, int timeoutMs)
	{
		List<FeedItem> list = new List<FeedItem>();
		string contentType;
		byte[] array = Download(source.Url, timeoutMs, out contentType);
		if (array.Length == 0)
		{
			return list;
		}
		string text = Decode(array, contentType);
		XDocument xDocument;
		try
		{
			xDocument = XDocument.Parse(text);
		}
		catch
		{
			return list;
		}
		XElement root = xDocument.Root;
		if (root == null)
		{
			return list;
		}
		IEnumerable<XElement> enumerable = from e in root.Descendants()
			where e.Name.LocalName == "item"
			select e;
		if (!enumerable.Any())
		{
			enumerable = from e in root.Descendants()
				where e.Name.LocalName == "entry"
				select e;
		}
		foreach (XElement item in enumerable)
		{
			string text2 = Clean(ChildText(item, "title"));
			if (text2.Length == 0)
			{
				continue;
			}
			string text3 = ChildText(item, "link");
			if (string.IsNullOrEmpty(text3))
			{
				XElement xElement = item.Elements().FirstOrDefault((XElement e) => e.Name.LocalName == "link" && e.Attribute("href") != null);
				if (xElement != null)
				{
					text3 = xElement.Attribute("href").Value;
				}
			}
			if (string.IsNullOrEmpty(text3))
			{
				text3 = source.Url;
			}
			list.Add(new FeedItem
			{
				Title = text2,
				Link = text3.Trim(),
				Source = source.Name,
				Category = source.Category,
				Summary = Clean(ChildText(item, "description", "summary", "content")),
				Published = ParseDate(ChildText(item, "pubDate", "published", "updated", "date"))
			});
			if (list.Count >= maxItems)
			{
				break;
			}
		}
		return list;
	}

	private static long ParseDate(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return 0L;
		}
		if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
		{
			return result.Ticks;
		}
		return 0L;
	}
}
