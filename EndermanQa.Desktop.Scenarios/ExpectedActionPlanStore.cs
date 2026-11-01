using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EndermanQa.Desktop.Scenarios;

public sealed class ExpectedActionPlanStore
{
	public const int RequiredCaseCount = 100;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

	public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Scenarios", "enderman-death-suite-100.json");

	public IReadOnlyList<ExpectedActionPlanCase> Load(string? path = null)
	{
		string fullPath = Path.GetFullPath(path ?? DefaultPath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("The complete Enderman 100-case expected-action plan is missing.", fullPath);
		}
		using FileStream utf8Json = File.OpenRead(fullPath);
		ExpectedActionPlanCase[]? obj = JsonSerializer.Deserialize<ExpectedActionPlanCase[]>(utf8Json, JsonOptions) ?? throw new InvalidDataException("The expected-action plan was empty or malformed.");
		Validate(obj);
		return obj;
	}

	public static void Validate(IReadOnlyList<ExpectedActionPlanCase> cases)
	{
		if (cases.Count != 100)
		{
			throw new InvalidDataException($"Expected exactly {100} Enderman cases; found {cases.Count}.");
		}
		HashSet<int> ids = cases.Select((ExpectedActionPlanCase item) => item.SourceId).ToHashSet();
		if (ids.Count != 100 || Enumerable.Range(1, 100).Any((int id) => !ids.Contains(id)))
		{
			throw new InvalidDataException("Expected-action plan IDs must be unique and cover 001–100.");
		}
		foreach (ExpectedActionPlanCase @case in cases)
		{
			if (@case.CaseId != $"enderman.death.idea-{@case.SourceId:000}" || string.IsNullOrWhiteSpace(@case.Title) || string.IsNullOrWhiteSpace(@case.Preconditions) || @case.ExpectedActions.Count == 0 || @case.ExpectedActions.Any(string.IsNullOrWhiteSpace) || string.IsNullOrWhiteSpace(@case.ExpectedObservation) || string.IsNullOrWhiteSpace(@case.PassCriteria) || string.IsNullOrWhiteSpace(@case.RepeatCriteria) || string.IsNullOrWhiteSpace(@case.EvidenceCriteria) || string.IsNullOrWhiteSpace(@case.Capability))
			{
				throw new InvalidDataException($"Expected-action plan case {@case.SourceId:000} has missing or mismatched required fields.");
			}
		}
	}
}
