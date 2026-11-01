using System;

namespace EndermanQa.Desktop.Integrations;

public sealed record AiQaConversationReply(string Content, string ProviderModel, DateTimeOffset ReceivedAt);
