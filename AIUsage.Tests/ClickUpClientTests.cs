using System.Net;
using System.Text.Json;
using AIUsage.ClickUp;

namespace AIUsage.Tests;

public class ClickUpClientTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    private const string Task = """
        { "id": "86b1abcde", "custom_id": null, "name": "Fix login",
          "text_content": "plain", "markdown_description": "**md** body",
          "status": { "status": "in progress", "type": "custom" },
          "priority": { "priority": "high" },
          "list": { "name": "Sprint 12 (9/22 - 10/5)" }, "folder": { "name": "Sprints" },
          "date_updated": "1758700800000", "custom_item_id": null }
        """;

    private static readonly HashSet<string> NoPrefixes = new();

    [Fact]
    public void ParseTask_maps_native_task()
    {
        var t = ClickUpClient.ParseTask(J(Task), requestedKey: null, NoPrefixes);
        Assert.Equal("CU-86b1abcde", t.Key);
        Assert.Equal("clickup", t.Provider);
        Assert.Equal("Fix login", t.Summary);
        Assert.Equal("in progress", t.Status);
        Assert.Equal("high", t.Priority);
        Assert.Equal("Task", t.IssueType);
        Assert.Equal("Sprints", t.Project);
        Assert.Equal("Sprint 12 (9/22 - 10/5)", t.Sprint);   // list inside a folder named like "Sprint…"
        Assert.Equal("**md** body", t.Description);
        // DateTime("o") round-trip of 1758700800000 ms epoch (UTC), confirmed via
        // `date -u -d @1758700800 +%Y-%m-%dT%H:%M:%S.0000000Z` = 2025-09-24T08:00:00.0000000Z.
        Assert.Equal("2025-09-24T08:00:00.0000000Z", t.Updated);
        Assert.False(t.IsDone);
    }

    [Fact]
    public void ParseTask_prefers_valid_custom_id_and_keeps_requested_key()
    {
        var withCustom = Task.Replace("\"custom_id\": null", "\"custom_id\": \"dev-42\"");
        var prefixes = new HashSet<string> { "DEV" };
        Assert.Equal("DEV-42", ClickUpClient.ParseTask(J(withCustom), null, prefixes).Key);
        // A fetch by native key keeps the key it was asked for, so the linked row is the one updated.
        Assert.Equal("CU-86b1abcde", ClickUpClient.ParseTask(J(withCustom), "CU-86b1abcde", prefixes).Key);
    }

    [Fact]
    public void ParseTask_falls_back_to_CU_id_when_custom_id_prefix_not_configured()
    {
        // DEV-42 is a syntactically valid key, but if the user never configured "DEV" as a
        // ClickUp Custom Task ID prefix, keeping it would make TicketProviders.ProviderIdFor
        // route this ticket to JIRA — wrong tracker, wrong ticket description, and JIRA data
        // upserted over the ClickUp row.
        var withCustom = Task.Replace("\"custom_id\": null", "\"custom_id\": \"dev-42\"");
        Assert.Equal("CU-86b1abcde", ClickUpClient.ParseTask(J(withCustom), null, NoPrefixes).Key);
        Assert.Equal("CU-86b1abcde", ClickUpClient.ParseTask(J(withCustom), null, new HashSet<string> { "OPS" }).Key);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("closed")]
    public void ParseTask_marks_finished_status_types_done(string type) =>
        Assert.True(ClickUpClient.ParseTask(J(Task.Replace("\"type\": \"custom\"", $"\"type\": \"{type}\"")), null, NoPrefixes).IsDone);

    [Fact]
    public void ParseTask_tolerates_missing_optional_fields()
    {
        var t = ClickUpClient.ParseTask(J("""{ "id": "86b1abcde", "name": "x", "priority": null }"""), null, NoPrefixes);
        Assert.Null(t.Priority); Assert.Null(t.Status); Assert.Null(t.Description); Assert.Null(t.Sprint);
    }

    [Fact]
    public void BuildTaskPath_native_needs_no_team() =>
        Assert.Equal("/task/86b1abcde?include_markdown_description=true",
            ClickUpClient.BuildTaskPath("CU-86b1abcde", null));

    [Fact]
    public void BuildTaskPath_custom_uses_team() =>
        Assert.Equal("/task/DEV-42?custom_task_ids=true&team_id=9012&include_markdown_description=true",
            ClickUpClient.BuildTaskPath("DEV-42", "9012"));

    [Fact]
    public void BuildTaskPath_custom_without_team_throws_readable()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ClickUpClient.BuildTaskPath("DEV-42", null));
        Assert.Contains("workspace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "", true)]
    [InlineData(HttpStatusCode.Unauthorized, """{"err":"Task not found","ECODE":"ITEM_013"}""", true)]
    [InlineData(HttpStatusCode.Unauthorized, """{"err":"Token invalid","ECODE":"OAUTH_025"}""", false)]
    public void IsNotFound_separates_dead_keys_from_auth_errors(HttpStatusCode code, string body, bool expected) =>
        Assert.Equal(expected, ClickUpClient.IsNotFound(code, body));

    [Fact]
    public void ParseTaskList_maps_all_valid_tasks()
    {
        var root = J($$"""{ "tasks": [{{Task}}], "last_page": false }""");
        var list = ClickUpClient.ParseTaskList(root, NoPrefixes);
        Assert.Single(list);
        Assert.Equal("CU-86b1abcde", list[0].Key);
        // AssignedAsync results never carry the description (not requested from the list endpoint).
        Assert.Null(list[0].Description);
    }

    [Fact]
    public void ParseTaskList_drops_task_whose_id_folds_into_an_invalid_key()
    {
        // id "x" has no digits, so TicketKey.Normalize("CU-x") isn't a valid native id (needs 6-12
        // chars) and isn't a valid JIRA-style key either ("CU-X" has no digits after the dash) —
        // this must never be persisted or returned, per the controller's ruling.
        var root = J("""{ "tasks": [{ "id": "x", "name": "bad id" }] }""");
        Assert.Empty(ClickUpClient.ParseTaskList(root, NoPrefixes));
    }

    [Fact]
    public void ParseTaskList_ignores_task_missing_id_and_reads_last_page_flag()
    {
        var root = J("""{ "tasks": [{ "name": "no id at all" }] }""");
        Assert.Empty(ClickUpClient.ParseTaskList(root, NoPrefixes));
    }

    [Fact]
    public void ForTest_constructs_a_usable_client_instance() =>
        Assert.NotNull(ClickUpClient.ForTest("pk_test", "9012"));
}
