using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EndermanQa.Desktop.Persistence;

public sealed class AiQaConversationStore
{
	private const int MaximumLoadedTurns = 1000;

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	private readonly string _directory;

	private readonly int _maximumMessageLength;

	private readonly SemaphoreSlim _appendGate = new SemaphoreSlim(1, 1);

	private readonly object _draftGate = new object();

	private string TurnsPath => Path.Combine(_directory, "conversation.jsonl");

	private string DraftPath => Path.Combine(_directory, "draft.txt");

	public AiQaConversationStore(string projectDataRoot, string conversationId = "qa")
	{
		if (string.IsNullOrWhiteSpace(conversationId) || conversationId.Length > 32 || conversationId.Any((char c) => !char.IsAsciiLetterOrDigit(c) && c != '-'))
		{
			throw new ArgumentException("대화 저장소 ID는 영문, 숫자, 하이픈만 사용할 수 있습니다.", "conversationId");
		}
		_directory = Path.GetFullPath(Path.Combine(projectDataRoot, (conversationId == "qa") ? "ai-chat" : ("ai-chat-" + conversationId)));
		_maximumMessageLength = ((conversationId == "qa") ? 5000 : 32000);
	}

	public async Task<IReadOnlyList<SavedAiQaConversationTurn>> LoadRecentAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		string turnsPath = TurnsPath;
		if (!File.Exists(turnsPath))
		{
			return Array.Empty<SavedAiQaConversationTurn>();
		}
		if (new FileInfo(turnsPath).Length > 67108864)
		{
			throw new InvalidDataException("AI 대화 기록이 너무 커서 안전하게 읽지 못했습니다. 원본은 보존했습니다.");
		}
		Queue<SavedAiQaConversationTurn> recent = new Queue<SavedAiQaConversationTurn>(1000);
		await foreach (string item in File.ReadLinesAsync(turnsPath, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!string.IsNullOrWhiteSpace(item))
			{
				if (item.Length > (long)_maximumMessageLength * 6L + 2000)
				{
					throw new InvalidDataException("AI 대화 기록에 허용 길이를 넘는 항목이 있습니다. 원본은 보존했습니다.");
				}
				SavedAiQaConversationTurn savedAiQaConversationTurn = JsonSerializer.Deserialize<SavedAiQaConversationTurn>(item, Options) ?? throw new InvalidDataException("AI 대화 기록에 빈 항목이 있습니다. 원본은 보존했습니다.");
				Validate(savedAiQaConversationTurn.Role, savedAiQaConversationTurn.Content, savedAiQaConversationTurn.ProviderModel);
				if (recent.Count == 1000)
				{
					recent.Dequeue();
				}
				recent.Enqueue(savedAiQaConversationTurn);
			}
		}
		return recent.ToArray();
	}

	public async Task AppendAsync(string role, string content, string? providerModel = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		Validate(role, content, providerModel);
		Directory.CreateDirectory(_directory);
		string line = JsonSerializer.Serialize(new SavedAiQaConversationTurn(role, content, providerModel, DateTimeOffset.UtcNow), Options) + Environment.NewLine;
		await _appendGate.WaitAsync(cancellationToken);
		try
		{
			await File.AppendAllTextAsync(TurnsPath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
		}
		finally
		{
			_appendGate.Release();
		}
	}

	public string LoadDraft()
	{
		string draftPath = DraftPath;
		if (!File.Exists(draftPath))
		{
			return string.Empty;
		}
		string text = File.ReadAllText(draftPath, Encoding.UTF8);
		if (text.Length > _maximumMessageLength)
		{
			return text.Substring(0, _maximumMessageLength);
		}
		return text;
	}

	public void SaveDraft(string text)
	{
		ArgumentNullException.ThrowIfNull(text, "text");
		if (text.Length > _maximumMessageLength)
		{
			throw new ArgumentException($"초안은 {_maximumMessageLength:N0}자를 넘을 수 없습니다.", "text");
		}
		lock (_draftGate)
		{
			Directory.CreateDirectory(_directory);
			string text2 = DraftPath + ".tmp";
			File.WriteAllText(text2, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.Move(text2, DraftPath, overwrite: true);
		}
	}

	private void Validate(string role, string content, string? providerModel)
	{
		if ((!(role == "user") && !(role == "assistant")) || 1 == 0)
		{
			throw new ArgumentException("대화 기록은 user 또는 assistant 역할만 허용합니다.", "role");
		}
		if (string.IsNullOrWhiteSpace(content) || content.Length > _maximumMessageLength)
		{
			throw new ArgumentException($"대화 메시지는 비어 있지 않은 {_maximumMessageLength:N0}자 이하여야 합니다.", "content");
		}
		if (providerModel != null && providerModel.Length > 200)
		{
			throw new ArgumentException("공급자 모델 표시가 너무 깁니다.", "providerModel");
		}
	}
}
