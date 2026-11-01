using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Integrations;

public sealed class JiraCloudClient
{
	public sealed record JiraProject(string Id, string Key, string Name);

	[CompilerGenerated]
	private HttpClient _003Chttp_003EP;

	public JiraCloudClient(HttpClient http)
	{
		_003Chttp_003EP = http;
		
	}

	public async Task<IReadOnlyList<JiraProject>> ListProjectsAsync(Uri site, string email, string apiToken, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidateSite(site);
		using HttpRequestMessage request = CreateRequest(HttpMethod.Get, new Uri(site, "/rest/api/3/project/search?maxResults=100"), email, apiToken);
		using HttpResponseMessage response = await _003Chttp_003EP.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw ApiError("Jira 프로젝트 조회", response.StatusCode);
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
		if (!jsonDocument.RootElement.TryGetProperty("values", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			throw new JsonException("Jira 프로젝트 응답 형식이 예상과 다릅니다.");
		}
		JsonElement value2;
		JsonElement value3;
		JsonElement value4;
		return (from x in value.EnumerateArray()
			select new JiraProject(x.TryGetProperty("id", out value2) ? value2.ToString() : string.Empty, x.TryGetProperty("key", out value3) ? (value3.GetString() ?? string.Empty) : string.Empty, x.TryGetProperty("name", out value4) ? (value4.GetString() ?? string.Empty) : string.Empty) into x
			where !string.IsNullOrWhiteSpace(x.Key)
			select x).ToArray();
	}

	public async Task<string> CreateIssueAsync(Uri site, string email, string apiToken, string projectKey, string issueType, string summary, string description, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidateSite(site);
		if (string.IsNullOrWhiteSpace(projectKey) || string.IsNullOrWhiteSpace(issueType) || string.IsNullOrWhiteSpace(summary))
		{
			throw new ArgumentException("Jira 프로젝트 키, 이슈 유형, 제목이 필요합니다.");
		}
		using HttpRequestMessage request = CreateRequest(HttpMethod.Post, new Uri(site, "/rest/api/3/issue"), email, apiToken);
		var value = new
		{
			fields = new
			{
				project = new
				{
					key = projectKey
				},
				summary = summary,
				description = ToAdf(description),
				issuetype = new
				{
					name = issueType
				},
				labels = new string[2] { "enderman-qa", "qa-workbench" }
			}
		};
		request.Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
		using HttpResponseMessage response = await _003Chttp_003EP.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw ApiError("Jira 이슈 생성", response.StatusCode);
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
		JsonElement value2;
		return jsonDocument.RootElement.TryGetProperty("key", out value2) ? (value2.GetString() ?? "created") : "created";
	}

	public async Task UploadAttachmentAsync(Uri site, string email, string apiToken, string issueKey, string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidateSite(site);
		if (!File.Exists(filePath))
		{
			throw new FileNotFoundException("Jira 첨부 파일을 찾을 수 없습니다.", filePath);
		}
		using HttpRequestMessage request = CreateRequest(HttpMethod.Post, new Uri(site, "/rest/api/3/issue/" + Uri.EscapeDataString(issueKey) + "/attachments"), email, apiToken);
		request.Headers.TryAddWithoutValidation("X-Atlassian-Token", "no-check");
		FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
		try
		{
			using MultipartFormDataContent content = new MultipartFormDataContent();
			StreamContent streamContent = new StreamContent(stream);
			streamContent.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(filePath));
			content.Add(streamContent, "file", Path.GetFileName(filePath));
			request.Content = content;
			using HttpResponseMessage httpResponseMessage = await _003Chttp_003EP.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				throw ApiError("Jira 증거 첨부", httpResponseMessage.StatusCode);
			}
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
	}

	private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string email, string apiToken)
	{
		if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(apiToken))
		{
			throw new ArgumentException("Jira 이메일과 API 토큰이 필요합니다.");
		}
		string parameter = Convert.ToBase64String(Encoding.UTF8.GetBytes(email + ":" + apiToken));
		HttpRequestMessage httpRequestMessage = new HttpRequestMessage(method, uri);
		httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", parameter);
		httpRequestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		return httpRequestMessage;
	}

	private static void ValidateSite(Uri site)
	{
		if (site.Scheme != Uri.UriSchemeHttps || !site.IsDefaultPort || !site.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase) || site.UserInfo.Length != 0 || !string.IsNullOrWhiteSpace(site.Query) || !string.IsNullOrWhiteSpace(site.Fragment))
		{
			throw new ArgumentException("Jira Cloud 사이트는 HTTPS *.atlassian.net 원본 주소여야 합니다.");
		}
	}

	private static object ToAdf(string text)
	{
		return new
		{
			type = "doc",
			version = 1,
			content = (from line in text.Replace("\r", string.Empty).Split('\n')
				select new
				{
					type = "paragraph",
					content = (string.IsNullOrEmpty(line) ? Array.Empty<object>() : new object[1]
					{
						new
						{
							type = "text",
							text = line
						}
					})
				}).ToArray()
		};
	}

	private static string ContentTypeFor(string path)
	{
		string text = Path.GetExtension(path).ToLowerInvariant();
		if (text != null)
		{
			int length = text.Length;
			if (length != 4)
			{
				if (length == 5)
				{
					switch (text[4])
					{
					case 'g':
						break;
					case 'p':
						if (!(text == ".webp"))
						{
							goto IL_0184;
						}
						return "image/webp";
					case 'm':
						if (!(text == ".webm"))
						{
							goto IL_0184;
						}
						return "video/webm";
					case 'n':
						if (!(text == ".json"))
						{
							goto IL_0184;
						}
						return "application/json";
					default:
						goto IL_0184;
					}
					if (text == ".jpeg")
					{
						goto IL_0144;
					}
				}
			}
			else
			{
				switch (text[2])
				{
				case 'n':
					if (!(text == ".png"))
					{
						goto IL_0184;
					}
					return "image/png";
				case 'p':
					break;
				case 'i':
					if (!(text == ".gif"))
					{
						goto IL_0184;
					}
					return "image/gif";
				case 'o':
					if (!(text == ".mov"))
					{
						goto IL_0184;
					}
					return "video/quicktime";
				case 'k':
					if (!(text == ".mkv"))
					{
						goto IL_0184;
					}
					return "video/x-matroska";
				default:
					goto IL_0184;
				}
				if (text == ".jpg")
				{
					goto IL_0144;
				}
				if (text == ".mp4")
				{
					return "video/mp4";
				}
			}
		}
		goto IL_0184;
		IL_0144:
		return "image/jpeg";
		IL_0184:
		return "text/plain";
	}

	private static HttpRequestException ApiError(string operation, HttpStatusCode status)
	{
		return new HttpRequestException($"{operation} 실패 (HTTP {status}). Jira 응답 본문은 계정 정보 보호를 위해 기록하지 않았습니다.");
	}
}
