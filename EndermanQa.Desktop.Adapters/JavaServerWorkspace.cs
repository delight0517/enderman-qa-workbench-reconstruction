namespace EndermanQa.Desktop.Adapters;

public sealed record JavaServerWorkspace(string InstanceId, string RootPath, string EulaPath, string ModsPath, string WorldPath, string ModJarPath, int ServerPort, string? PreviousPauseWhenEmptySeconds = null);
