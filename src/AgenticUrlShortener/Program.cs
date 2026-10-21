using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<UrlStore>();
builder.Services.AddSingleton<WorkflowOrchestrator>();
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok(new {
    service = "Agentic URL Shortener",
    status = "running",
    endpoints = new[] { "POST /api/urls", "GET /r/{code}", "GET /api/urls/{code}/analytics",
                        "POST /api/workflows", "GET /api/workflows/{id}", "POST /api/workflows/{id}/approve" }
}));

app.MapPost("/api/urls", ([FromBody] CreateUrlRequest request, UrlStore store) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        return Results.BadRequest(new { error = "url must be an absolute HTTP or HTTPS URL" });

    var result = store.Create(uri.ToString(), request.CustomCode);
    return result.Error is not null
        ? Results.Conflict(new { error = result.Error })
        : Results.Created($"/api/urls/{result.Record!.Code}", new {
            result.Record.Code,
            shortPath = $"/r/{result.Record.Code}",
            result.Record.OriginalUrl,
            result.Record.CreatedAtUtc
        });
});

app.MapGet("/r/{code}", (string code, HttpContext context, UrlStore store) =>
{
    var record = store.Resolve(code);
    if (record is null) return Results.NotFound(new { error = "short code not found" });
    store.RecordClick(code, context.Request.Headers.UserAgent.ToString());
    return Results.Redirect(record.OriginalUrl);
});

app.MapGet("/api/urls/{code}/analytics", (string code, UrlStore store) =>
{
    var analytics = store.GetAnalytics(code);
    return analytics is null ? Results.NotFound(new { error = "short code not found" }) : Results.Ok(analytics);
});

app.MapPost("/api/workflows", async ([FromBody] WorkflowRequest request, WorkflowOrchestrator orchestrator) =>
{
    if (string.IsNullOrWhiteSpace(request.Requirement))
        return Results.BadRequest(new { error = "requirement is required" });
    var run = await orchestrator.StartAsync(request.Scenario, request.Requirement);
    return Results.Ok(run);
});

app.MapGet("/api/workflows/{id}", (string id, WorkflowOrchestrator orchestrator) =>
{
    var run = orchestrator.Get(id);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.MapPost("/api/workflows/{id}/approve", (string id, WorkflowOrchestrator orchestrator) =>
{
    var run = orchestrator.Approve(id);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

app.Run();

public sealed record CreateUrlRequest([Required] string Url, string? CustomCode);
public sealed record WorkflowRequest(string Scenario, string Requirement);

public sealed class UrlRecord
{
    public required string Code { get; init; }
    public required string OriginalUrl { get; init; }
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public long Clicks { get; set; }
}
public sealed record ClickEvent(DateTime AtUtc, string UserAgent);
public sealed record AnalyticsResponse(string Code, long TotalClicks, DateTime CreatedAtUtc, IReadOnlyList<ClickEvent> RecentClicks);

public sealed class UrlStore
{
    private readonly ConcurrentDictionary<string, UrlRecord> _urls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ClickEvent>> _clicks = new(StringComparer.OrdinalIgnoreCase);
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public (UrlRecord? Record, string? Error) Create(string url, string? customCode)
    {
        var code = string.IsNullOrWhiteSpace(customCode) ? GenerateCode() : customCode.Trim();
        if (code.Length is < 4 or > 32 || code.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            return (null, "customCode must be 4-32 alphanumeric, dash or underscore characters");
        var record = new UrlRecord { Code = code, OriginalUrl = url };
        return _urls.TryAdd(code, record) ? (record, null) : (null, "short code already exists");
    }

    public UrlRecord? Resolve(string code) => _urls.TryGetValue(code, out var record) ? record : null;

    public void RecordClick(string code, string userAgent)
    {
        if (!_urls.TryGetValue(code, out var record)) return;
        Interlocked.Increment(ref record.Clicks);
        var queue = _clicks.GetOrAdd(code, _ => new ConcurrentQueue<ClickEvent>());
        queue.Enqueue(new ClickEvent(DateTime.UtcNow, userAgent));
        while (queue.Count > 100 && queue.TryDequeue(out _)) { }
    }

    public AnalyticsResponse? GetAnalytics(string code)
    {
        if (!_urls.TryGetValue(code, out var record)) return null;
        var events = _clicks.TryGetValue(code, out var queue) ? queue.ToArray() : Array.Empty<ClickEvent>();
        return new AnalyticsResponse(code, Interlocked.Read(ref record.Clicks), record.CreatedAtUtc, events);
    }

    private static string GenerateCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return string.Concat(bytes.ToArray().Select(b => Alphabet[b % Alphabet.Length]));
    }
}

public enum TaskStatus { Pending, Running, Succeeded, Failed, Blocked, AwaitingApproval, Skipped }
public sealed record WorkflowTask(string Id, string Name, string Agent, string[] DependsOn, bool RequiresApproval = false);
public sealed record AuditEvent(DateTime AtUtc, string Event, string Detail);
public sealed class WorkflowRun
{
    public required string Id { get; init; }
    public required string Scenario { get; init; }
    public required string Requirement { get; init; }
    public string Status { get; set; } = "Running";
    public Dictionary<string, string> TaskStates { get; init; } = new();
    public List<AuditEvent> Audit { get; init; } = new();
    public List<string> Assumptions { get; init; } = new();
    public DateTime StartedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public string? PendingApproval { get; set; }
}

public sealed class WorkflowOrchestrator
{
    private readonly ConcurrentDictionary<string, WorkflowRun> _runs = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WorkflowTask> _graph = new()
    {
        ["normalize"] = new("normalize", "Normalize requirement", "RequirementAgent", Array.Empty<string>()),
        ["impact"] = new("impact", "Analyze codebase impact", "CodebaseAgent", new[] { "normalize" }),
        ["design"] = new("design", "Create architecture and API design", "DesignAgent", new[] { "normalize" }),
        ["plan"] = new("plan", "Build dependency-aware task plan", "PlannerAgent", new[] { "impact", "design" }),
        ["implement"] = new("implement", "Generate implementation proposal", "CodingAgent", new[] { "plan" }),
        ["test"] = new("test", "Run validation checks", "TestAgent", new[] { "implement" }),
        ["security"] = new("security", "Review security and policy", "SecurityAgent", new[] { "implement" }),
        ["approval"] = new("approval", "Human review before release", "HumanGate", new[] { "test", "security" }, true),
        ["release"] = new("release", "Produce release-readiness report", "ReleaseAgent", new[] { "approval" })
    };

    public async Task<WorkflowRun> StartAsync(string scenario, string requirement)
    {
        scenario = (scenario ?? "greenfield").Trim().ToLowerInvariant();
        if (!new[] { "greenfield", "brownfield", "ambiguous" }.Contains(scenario))
            scenario = "greenfield";

        var run = new WorkflowRun {
            Id = Guid.NewGuid().ToString("N"),
            Scenario = scenario,
            Requirement = requirement.Trim()
        };
        _runs[run.Id] = run;
        AddAudit(run, "workflow.started", $"scenario={scenario}");
        foreach (var task in _graph.Values) run.TaskStates[task.Id] = TaskStatus.Pending.ToString();

        if (scenario == "ambiguous")
        {
            run.Assumptions.Add("Unspecified expiry policy is not invented; product owner must decide.");
            run.Assumptions.Add("Analytics means total click count plus a bounded recent event sample.");
            AddAudit(run, "requirement.ambiguous", "Recorded assumptions; human review required.");
        }

        // Independent impact and design tasks are eligible together after normalization.
        await ExecuteTask(run, "normalize");
        await Task.WhenAll(ExecuteTask(run, "impact"), ExecuteTask(run, "design"));
        await ExecuteTask(run, "plan");
        await ExecuteTask(run, "implement");
        await Task.WhenAll(ExecuteTask(run, "test"), ExecuteTask(run, "security"));

        if (scenario == "ambiguous")
        {
            run.Status = "AwaitingApproval";
            run.PendingApproval = "Confirm expiry, custom-code policy, and analytics retention.";
            run.TaskStates["approval"] = TaskStatus.AwaitingApproval.ToString();
            AddAudit(run, "approval.required", run.PendingApproval);
            return run;
        }

        run.Status = "AwaitingApproval";
        run.PendingApproval = "Review test evidence, security findings, and release plan.";
        run.TaskStates["approval"] = TaskStatus.AwaitingApproval.ToString();
        AddAudit(run, "approval.required", run.PendingApproval);
        return run;
    }

    private async Task ExecuteTask(WorkflowRun run, string id)
    {
        await _gate.WaitAsync();
        try
        {
            var task = _graph[id];
            if (task.DependsOn.Any(dep => run.TaskStates[dep] != TaskStatus.Succeeded.ToString()))
            {
                run.TaskStates[id] = TaskStatus.Blocked.ToString();
                AddAudit(run, "task.blocked", $"{id}: dependency not satisfied");
                return;
            }
            run.TaskStates[id] = TaskStatus.Running.ToString();
            AddAudit(run, "task.started", $"{id} by {task.Agent}");
        }
        finally { _gate.Release(); }

        // Deterministic demonstration adapter. Replace with bounded LLM/tool execution.
        await Task.Yield();
        await _gate.WaitAsync();
        try
        {
            run.TaskStates[id] = TaskStatus.Succeeded.ToString();
            AddAudit(run, "task.succeeded", $"{id}: deterministic prototype output generated");
        }
        catch (Exception ex)
        {
            run.RetryCount++;
            run.TaskStates[id] = TaskStatus.Failed.ToString();
            AddAudit(run, "task.failed", $"{id}: {ex.GetType().Name}");
            run.Status = "SafeStopped";
        }
        finally { _gate.Release(); }
    }

    public WorkflowRun? Get(string id) => _runs.TryGetValue(id, out var run) ? run : null;

    public WorkflowRun? Approve(string id)
    {
        if (!_runs.TryGetValue(id, out var run)) return null;
        if (run.Status != "AwaitingApproval") return run;
        run.TaskStates["approval"] = TaskStatus.Succeeded.ToString();
        AddAudit(run, "approval.granted", "Approval endpoint invoked; production requires authenticated approver identity.");
        run.TaskStates["release"] = TaskStatus.Succeeded.ToString();
        AddAudit(run, "release.ready", "Release readiness summary generated; deployment is not executed.");
        run.Status = "Completed";
        run.CompletedAtUtc = DateTime.UtcNow;
        return run;
    }

    private static void AddAudit(WorkflowRun run, string evt, string detail) =>
        run.Audit.Add(new AuditEvent(DateTime.UtcNow, evt, detail));
}
