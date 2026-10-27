using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WidgetHub;

public static class UpdateService
{
	/// <summary>
	/// 先走 github.com 的 releases/latest 跳转（公开仓库不需要令牌，也不占 REST 配额），
	/// 只有仓库重新变私有、这条走不通时才回落到 API（这时才用令牌）。
	/// </summary>
	public static async Task<UpdateInfo?> CheckAsync(string token)
	{
		UpdateInfo? publicRelease = await CheckPublicAsync().ConfigureAwait(continueOnCapturedContext: false);
		if (publicRelease != null)
		{
			return publicRelease;
		}
		return await CheckApiAsync(token).ConfigureAwait(continueOnCapturedContext: false);
	}

	private const string LatestReleaseUrl = AppInfo.RepoUrl + "/releases/latest";

	private const string TagPathMarker = "/releases/tag/";

	private static async Task<UpdateInfo?> CheckPublicAsync()
	{
		try
		{
			HttpClient client = NewClient(sendApiAcceptHeader: false);
			try
			{
				HttpResponseMessage resp = await client.GetAsync(LatestReleaseUrl).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					if (!resp.IsSuccessStatusCode)
					{
						return null;
					}
					string text = ((resp.RequestMessage != null) ? (resp.RequestMessage.RequestUri?.ToString() ?? "") : "");
					int num = text.IndexOf(TagPathMarker, StringComparison.OrdinalIgnoreCase);
					if (num < 0)
					{
						return null;
					}
					string text2 = text.Substring(num + TagPathMarker.Length).Trim('/');
					if (text2.Length <= "win-".Length || !text2.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
					{
						return null;
					}
					string text3 = text2.Substring("win-".Length).TrimStart('v', 'V');
					if (text3.Length == 0)
					{
						return null;
					}
					return new UpdateInfo
					{
						TagVersion = text3,
						HtmlUrl = text,
						AssetName = AppInfo.AssetName,
						AssetUrl = AppInfo.RepoUrl + "/releases/download/" + text2 + "/" + AppInfo.AssetName
					};
				}
				finally
				{
					((IDisposable)resp)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)client)?.Dispose();
			}
		}
		catch
		{
			return null;
		}
	}

	private static async Task<UpdateInfo?> CheckApiAsync(string token)
	{
		try
		{
			HttpClient client = NewClient(sendApiAcceptHeader: true);
			try
			{
				HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, AppInfo.ReleasesApi);
				try
				{
					if (!string.IsNullOrWhiteSpace(token))
					{
						req.Headers.Authorization = new AuthenticationHeaderValue("token", token.Trim());
					}
					HttpResponseMessage resp = await client.SendAsync(req).ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
						if (!resp.IsSuccessStatusCode)
						{
							return null;
						}
						JsonNode obj = JsonNode.Parse(text, (JsonNodeOptions?)null, default(JsonDocumentOptions));
						JsonArray val = (JsonArray)(object)((obj is JsonArray) ? obj : null);
						if (val == null)
						{
							return null;
						}
						UpdateInfo updateInfo = null;
						Version version = null;
						foreach (JsonNode item in val)
						{
							JsonObject val2 = (JsonObject)(object)((item is JsonObject) ? item : null);
							if (val2 == null || Flag(val2, "draft") || Flag(val2, "prerelease"))
							{
								continue;
							}
							string text2 = ((object)((JsonNode)val2)["tag_name"])?.ToString() ?? "";
							if (text2.Length <= "win-".Length || !text2.StartsWith("win-", StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							string text3 = text2.Substring("win-".Length).TrimStart('v', 'V');
							if (!Version.TryParse(Normalize(text3), out Version result))
							{
								continue;
							}
							UpdateInfo updateInfo2 = new UpdateInfo
							{
								TagVersion = text3,
								HtmlUrl = (((object)((JsonNode)val2)["html_url"])?.ToString() ?? "https://github.com/feibixunchang/desktop-widget-suite")
							};
							JsonNode obj2 = ((JsonNode)val2)["assets"];
							JsonArray val3 = (JsonArray)(object)((obj2 is JsonArray) ? obj2 : null);
							if (val3 != null)
							{
								foreach (JsonNode item2 in val3)
								{
									if (item2 == null)
									{
										continue;
									}
									string text4 = ((object)item2["name"])?.ToString() ?? "";
									if (string.Equals(text4, "WidgetHubSetup.exe", StringComparison.OrdinalIgnoreCase))
									{
										updateInfo2.AssetName = text4;
										updateInfo2.AssetUrl = ((object)item2["browser_download_url"])?.ToString() ?? "";
										if (item2["id"] != null && long.TryParse(((object)item2["id"]).ToString(), out var result2))
										{
											updateInfo2.AssetId = result2;
										}
										break;
									}
								}
							}
							if (updateInfo2.AssetId > 0 && (!(version != null) || !(result <= version)))
							{
								updateInfo = updateInfo2;
								version = result;
							}
						}
						return updateInfo ?? new UpdateInfo();
					}
					finally
					{
						((IDisposable)resp)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)req)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)client)?.Dispose();
			}
		}
		catch
		{
			return null;
		}
	}

	private static bool Flag(JsonObject o, string key)
	{
		JsonNode val = ((JsonNode)o)[key];
		if (val == null)
		{
			return false;
		}
		return string.Equals(((object)val).ToString(), "true", StringComparison.OrdinalIgnoreCase);
	}

	public static async Task<string?> DownloadAsync(UpdateInfo info, string token, Action<int>? progress = null)
	{
		string text = info.AssetApiUrl;
		bool flag = !string.IsNullOrEmpty(text);
		if (!flag)
		{
			text = info.AssetUrl;
		}
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		string text2 = Path.Combine(Path.GetTempPath(), "WidgetHubUpdate");
		Directory.CreateDirectory(text2);
		string path = Path.Combine(text2, "WidgetHubSetup.exe");
		try
		{
			HttpClient client = NewClient(sendApiAcceptHeader: false);
			try
			{
				HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, text);
				try
				{
					if (flag)
					{
						req.Headers.Accept.ParseAdd("application/octet-stream");
					}
					// 令牌只发给 api.github.com：browser_download_url 会 302 到 CDN，
					// 带上令牌等于把 PAT 投给第三方主机。
					if (!string.IsNullOrWhiteSpace(token) && string.Equals(req.RequestUri?.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
					{
						req.Headers.Authorization = new AuthenticationHeaderValue("token", token.Trim());
					}
					HttpResponseMessage resp = await client.SendAsync(req, (HttpCompletionOption)1).ConfigureAwait(continueOnCapturedContext: false);
					try
					{
						if (!resp.IsSuccessStatusCode)
						{
							return null;
						}
						long total = resp.Content.Headers.ContentLength.GetValueOrDefault();
						using Stream input = await resp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false);
						using FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
						byte[] buffer = new byte[81920];
						long done = 0L;
						while (true)
						{
							int num;
							int read = (num = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(continueOnCapturedContext: false));
							if (num <= 0)
							{
								break;
							}
							await output.WriteAsync(buffer, 0, read).ConfigureAwait(continueOnCapturedContext: false);
							done += read;
							if (total > 0)
							{
								progress?.Invoke((int)Math.Min(100L, done * 100 / total));
							}
						}
						return path;
					}
					finally
					{
						((IDisposable)resp)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)req)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)client)?.Dispose();
			}
		}
		catch
		{
			return null;
		}
	}

	private static HttpClient NewClient(bool sendApiAcceptHeader)
	{
		HttpClient val = new HttpClient
		{
			Timeout = TimeSpan.FromMinutes(5.0)
		};
		val.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetHub/" + AppInfo.Version);
		if (sendApiAcceptHeader)
		{
			val.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		}
		return val;
	}

	public static bool IsNewer(string a, string b)
	{
		if (!Version.TryParse(Normalize(a), out Version result))
		{
			return false;
		}
		if (!Version.TryParse(Normalize(b), out Version result2))
		{
			return false;
		}
		return result > result2;
	}

	private static string Normalize(string v)
	{
		string[] array = (v ?? "").Trim().TrimStart('v', 'V').Split(new char[1] { '.' });
		List<int> list = new List<int>();
		string[] array2 = array;
		foreach (string s in array2)
		{
			list.Add(int.TryParse(s, out var result) ? result : 0);
		}
		while (list.Count < 2)
		{
			list.Add(0);
		}
		return string.Join(".", list.Take(3));
	}
}
