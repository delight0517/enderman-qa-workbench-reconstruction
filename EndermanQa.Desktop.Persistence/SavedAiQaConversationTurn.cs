using System;

namespace EndermanQa.Desktop.Persistence;

public sealed record SavedAiQaConversationTurn(string Role, string Content, string? ProviderModel, DateTimeOffset AddedAt);
