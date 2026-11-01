namespace EndermanQa.Desktop.Integrations;

public sealed record JiraConnectionSettings(string Site, string Email, string ProjectKey, string ApiToken, string IssueType = "Bug");
