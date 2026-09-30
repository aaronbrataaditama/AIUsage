using AIUsage.Terminal;

namespace AIUsage.Tests;

/// <summary>Claude Code agents are commonly organised into subfolders under .claude/agents (one
/// per feature/sub-project); the catalog must find them, not just files directly in that folder.</summary>
public class AgentCatalogTests
{
    [Fact]
    public void List_finds_agents_nested_in_subfolders()
    {
        var root = Directory.CreateTempSubdirectory("aiusage-agentcatalog-");
        try
        {
            var nested = Path.Combine(root.FullName, ".claude", "agents", "docassembler");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "docassembler-developer.md"),
                "---\nname: docassembler-developer\ndescription: Full-lifecycle developer\n---\nBody");

            var agents = AgentCatalog.List(root.FullName);

            Assert.Contains(agents, a => a.Name == "docassembler-developer" && a.Scope == "project");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void List_still_finds_top_level_agents()
    {
        var root = Directory.CreateTempSubdirectory("aiusage-agentcatalog-");
        try
        {
            var agentsDir = Path.Combine(root.FullName, ".claude", "agents");
            Directory.CreateDirectory(agentsDir);
            File.WriteAllText(Path.Combine(agentsDir, "top-level.md"),
                "---\nname: top-level\ndescription: d\n---\nBody");

            var agents = AgentCatalog.List(root.FullName);

            Assert.Contains(agents, a => a.Name == "top-level" && a.Scope == "project");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
