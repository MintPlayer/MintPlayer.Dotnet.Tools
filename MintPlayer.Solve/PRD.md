# Product Requirements Document: Solve CLI Tool

**Project**: Solve CLI Tool
**Status**: Draft
**Author**: Claude
**Created**: 2026-01-27
**Last Updated**: 2026-01-27

---

## Overview

**Solve** is a CLI tool that streamlines the workflow of delegating GitHub issues to Claude Code. It automates the repetitive setup steps (branch management, PRD creation) and provides a structured interface for issue-driven development.

The tool wraps common git operations and Claude Code interactions into simple commands, essentially providing a CLI interface to the workflow defined in the DeCronosGroep claude commands (`create_for_issue`, `work_on`, `create_pull_request`, `resolve_pr_feedback`).

---

## Goals & Objectives

### Primary Goals
- Reduce manual steps when starting work on a GitHub issue
- Provide a consistent, repeatable workflow for issue-driven development
- Launch Claude Code with full context (issue details, PRD, development plan)
- Work seamlessly with any GitHub repository

### Success Metrics
- Single command to go from issue URL to ready-to-develop state
- Eliminates need to manually run 4+ git commands before starting work
- Consistent PRD structure across all issues
- Seamless handoff to Claude Code for implementation

---

## User Stories

### As a Developer
I want to run a single command with a GitHub issue URL
So that I can immediately start working on the issue without manual branch setup

### As a Team Lead
I want consistent PRDs generated for each issue
So that implementation plans are documented and reviewable

### As a Developer
I want to resume work on an issue I started earlier
So that I can pick up where I left off with full context

### As a Developer
I want to create a PR when my work is done
So that I can submit my changes for review with proper documentation

---

## Command Structure

```
solve <issue-url>              # Full workflow: init + prd + work
solve init <issue-url>         # Setup: switch branch, pull, create feature branch
solve prd [issue-url]          # Generate PRD and development plan
solve work [issue-url]         # Resume work on issue (loads PRD, shows status)
solve pr [issue-url]           # Create pull request
solve feedback [pr-url]        # Review and resolve PR feedback
solve status [issue-url]       # Show current status of issue work
```

---

## Detailed Command Specifications

### Root Command: `solve <issue-url>`

The default command runs the full workflow:

1. **Parse issue reference** - Extract owner, repo, and issue number
2. **Fetch issue details** - Get title, body, labels from GitHub API via `gh`
3. **Switch to default branch** - Detect and checkout main/master/development
4. **Pull latest changes** - `git pull`
5. **Create feature branch** - `issues/<number>-<slug>`
6. **Generate PRD** - Create `docs/issue_<number>_PRD.md`
7. **Generate development plan** - Create `.claude/plans/issue_<number>.md`
8. **Launch Claude Code** - Start implementation with full context

**Arguments:**
- `issue-url` (required): GitHub issue URL or reference

**Options:**
- `--branch-prefix` / `-p`: Custom branch prefix (default: `issues`)
- `--skip-prd`: Skip PRD generation
- `--skip-claude`: Don't launch Claude Code after setup
- `--dry-run`: Show what would be done without executing

---

### Subcommand: `solve init <issue-url>`

Setup-only mode (mirrors first part of `create_for_issue`):

1. Parse issue reference
2. Check if branch already exists for this issue
3. If exists: offer to switch to it
4. If not: switch to default branch, pull, create feature branch
5. Display next steps

**Arguments:**
- `issue-url` (required): GitHub issue URL or reference

**Options:**
- `--branch-prefix` / `-p`: Custom branch prefix (default: `issues`)
- `--no-pull`: Skip git pull
- `--force`: Create new branch even if one exists

**Output:**
```
Fetching issue #42...
  Title: Add user authentication
  Labels: enhancement, security
Switching to default branch (development)...
Pulling latest changes...
Creating branch: issues/42-add-user-authentication
Done! Run 'solve prd' to generate the PRD.
```

---

### Subcommand: `solve prd [issue-url]`

Generate PRD and development plan (mirrors `create_for_issue` steps 2-6):

1. Detect issue number from current branch if not provided
2. Fetch issue details via `gh issue view`
3. Analyze issue clarity (problem statement, outcomes, requirements)
4. If unclear: prompt user for clarification
5. Optionally update issue description on GitHub
6. Generate development plan at `.claude/plans/issue_<number>.md`
7. Generate PRD at `docs/issue_<number>_PRD.md`

**Arguments:**
- `issue-url` (optional): GitHub issue URL or reference. If omitted, detect from branch name.

**Options:**
- `--output` / `-o`: Custom output directory
- `--force` / `-f`: Overwrite existing PRD/plan
- `--update-issue`: Update GitHub issue with clarifications

**Branch Detection:**
Extracts issue number from patterns like:
- `issues/123`
- `issues/#123`
- `issues/123-description`
- `feature/issue-123-description`

---

### Subcommand: `solve work [issue-url]`

Start/continue implementation (mirrors `work_on`):

1. Detect issue number from branch or argument
2. If not on correct branch: offer to switch
3. Check for PRD and development plan files
4. If missing: prompt to run `solve prd` first
5. Read and analyze PRD/plan files
6. Show status summary (completed items, remaining work, blockers)
7. Launch Claude Code with context

**Arguments:**
- `issue-url` (optional): GitHub issue URL or reference. If omitted, detect from branch.

**Options:**
- `--status-only`: Only show status, don't launch Claude
- `--continue` / `-c`: Continue from last session

**Status Output:**
```
## Issue #42: Add user authentication

**PRD Status**: In Progress
**Implementation Status**: 40% complete

### Progress Summary
- Functional Requirements: 2 of 5 completed
- Milestones: M1 complete, M2 in progress

### Completed
- [x] FR-1: User model created
- [x] FR-2: Login endpoint implemented

### Remaining Work
- [ ] FR-3: JWT token generation
- [ ] FR-4: Password reset flow
- [ ] FR-5: Session management

### Open Questions
- None

What would you like to work on?
```

---

### Subcommand: `solve pr [issue-url]`

Create pull request (mirrors `create_pull_request`):

1. Detect issue from branch or argument
2. Run pre-PR checklist (configurable)
3. Analyze commits and diff against base branch
4. Draft PR description using template
5. Show preview to user
6. On confirmation: push branch, create PR via `gh pr create`

**Arguments:**
- `issue-url` (optional): Detect from branch if omitted

**Options:**
- `--draft` / `-d`: Create as draft PR
- `--base` / `-b`: Target branch (default: auto-detect development/main)
- `--title` / `-t`: Custom PR title (default: issue title)
- `--no-checklist`: Skip pre-PR checklist
- `--yes` / `-y`: Skip confirmation

**PR Template:**
```markdown
## What type of PR is this?

- [ ] 🍕 Feature
- [ ] 🐛 Bug Fix
- [ ] 📝 Documentation Update
- [ ] 🧑‍💻 Code Refactor
- [ ] ✅ Test
- [ ] 📦 Chore

## Description

[Auto-generated from commits and diff analysis]

## Related Tickets & Documents

Fixes #<issue-number>

## Checklist

- [ ] Tested locally
- [ ] Updated documentation
- [ ] No breaking changes
```

---

### Subcommand: `solve feedback [pr-url]`

Review and resolve PR feedback (mirrors `resolve_pr_feedback`):

1. Detect PR from current branch or argument
2. Fetch unresolved review threads via GitHub GraphQL API
3. Analyze each comment/change request
4. Assess validity (valid, partially valid, not valid)
5. Present summary with assessments
6. Create development plan for valid feedback
7. On confirmation: implement changes, update PRD if needed
8. Reply to reviewers and resolve threads

**Arguments:**
- `pr-url` (optional): PR URL or number. Detect from branch if omitted.

**Options:**
- `--assess-only`: Only show assessment, don't implement
- `--auto-reply`: Automatically reply to resolved items

---

### Subcommand: `solve status [issue-url]`

Show work status without launching Claude:

1. Detect issue from branch or argument
2. Read PRD and development plan
3. Analyze completion status
4. Check for uncommitted changes
5. Check for open PR

**Arguments:**
- `issue-url` (optional): Detect from branch if omitted

**Options:**
- `--json`: Output as JSON for scripting

---

## Functional Requirements

### Must Have (P0)

- [ ] **FR-1**: Parse GitHub issue references in multiple formats:
  - Full URL: `https://github.com/owner/repo/issues/123`
  - Short reference: `owner/repo#123`
  - Number only: `#123` or `123` (within git repo context)

- [ ] **FR-2**: Detect issue number from branch name patterns:
  - `issues/123`, `issues/#123`, `issues/123-description`
  - `feature/issue-123-description`

- [ ] **FR-3**: Detect default branch automatically (main, master, development, or repo default via `gh`)

- [ ] **FR-4**: Create consistently named feature branches (`issues/<number>-<slug>`)

- [ ] **FR-5**: Generate structured development plan at `.claude/plans/issue_<number>.md`

- [ ] **FR-6**: Generate structured PRD at `docs/issue_<number>_PRD.md`

- [ ] **FR-7**: Invoke Claude Code CLI with appropriate context

- [ ] **FR-8**: Support all subcommands (init, prd, work, pr, feedback, status)

### Should Have (P1)

- [ ] **FR-9**: Pre-PR checklist with configurable items

- [ ] **FR-10**: PR template customization via config file

- [ ] **FR-11**: Analyze PR feedback validity

- [ ] **FR-12**: Reply to PR comments via GitHub API

- [ ] **FR-13**: Update PRD/plan based on PR feedback

### Could Have (P2)

- [ ] **FR-14**: Configuration file for defaults (branch prefix, templates, checklist)

- [ ] **FR-15**: Support for multiple GitHub remotes

- [ ] **FR-16**: Interactive mode for issue clarification

---

## Non-Functional Requirements

### Performance
- **NFR-1**: Commands should complete within 5 seconds (excluding network operations)

### Compatibility
- **NFR-2**: Work on Windows, macOS, and Linux
- **NFR-3**: Require only `git` and `gh` CLI as external dependencies
- **NFR-4**: Optional: `claude` CLI for launching Claude Code

### Usability
- **NFR-5**: Provide clear error messages with suggested fixes
- **NFR-6**: Support `--help` for all commands
- **NFR-7**: Colorized output for better readability

### Security
- **NFR-8**: Never store GitHub credentials (rely on `gh` authentication)
- **NFR-9**: Validate URLs before making requests

---

## Technical Specifications

### Architecture

```
Solve/
├── Solve.csproj                    # Console app project
├── Program.cs                      # Entry point
├── Commands/
│   ├── SolveCommand.cs             # Root command [CliRootCommand]
│   ├── InitCommand.cs              # [CliCommand] init
│   ├── PrdCommand.cs               # [CliCommand] prd
│   ├── WorkCommand.cs              # [CliCommand] work
│   ├── PrCommand.cs                # [CliCommand] pr
│   ├── FeedbackCommand.cs          # [CliCommand] feedback
│   └── StatusCommand.cs            # [CliCommand] status
├── Services/
│   ├── IGitHubService.cs           # GitHub API interactions via gh CLI
│   ├── GitHubService.cs
│   ├── IGitService.cs              # Git operations
│   ├── GitService.cs
│   ├── IIssueParser.cs             # Issue URL/reference parsing
│   ├── IssueParser.cs
│   ├── IBranchService.cs           # Branch detection and management
│   ├── BranchService.cs
│   ├── IPrdGenerator.cs            # PRD document generation
│   ├── PrdGenerator.cs
│   ├── IClaudeService.cs           # Claude Code CLI integration
│   └── ClaudeService.cs
├── Models/
│   ├── GitHubIssue.cs              # Issue model
│   ├── IssueReference.cs           # Parsed issue reference
│   ├── PullRequest.cs              # PR model
│   ├── ReviewThread.cs             # PR review thread
│   └── WorkStatus.cs               # Work status model
└── Templates/
    ├── PrdTemplate.md              # Embedded PRD template
    ├── PlanTemplate.md             # Embedded development plan template
    └── PrTemplate.md               # Embedded PR template
```

### Command Implementation Pattern

Using MintPlayer.CliGenerator with nested commands:

```csharp
[CliRootCommand(Name = "solve", Description = "Delegate GitHub issues to Claude Code")]
public partial class SolveCommand : ICliCommand
{
    private readonly IIssueParser _issueParser;
    private readonly IGitService _gitService;
    private readonly IGitHubService _githubService;
    private readonly IPrdGenerator _prdGenerator;
    private readonly IClaudeService _claudeService;

    public SolveCommand(
        IIssueParser issueParser,
        IGitService gitService,
        IGitHubService githubService,
        IPrdGenerator prdGenerator,
        IClaudeService claudeService)
    {
        _issueParser = issueParser;
        _gitService = gitService;
        _githubService = githubService;
        _prdGenerator = prdGenerator;
        _claudeService = claudeService;
    }

    [CliArgument(0, Name = "issue-url", Required = false)]
    public string? IssueUrl { get; set; }

    [CliOption("--branch-prefix", "-p")]
    public string BranchPrefix { get; set; } = "issues";

    [CliOption("--skip-prd")]
    public bool SkipPrd { get; set; }

    [CliOption("--skip-claude")]
    public bool SkipClaude { get; set; }

    [CliOption("--dry-run")]
    public bool DryRun { get; set; }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        // Full workflow: init + prd + work
        // 1. Parse issue reference
        // 2. Init branch
        // 3. Generate PRD
        // 4. Launch Claude
    }

    [CliCommand("init", Description = "Initialize branch for issue")]
    public partial class InitCommand : ICliCommand
    {
        [CliArgument(0, Name = "issue-url")]
        public string IssueUrl { get; set; } = string.Empty;

        [CliOption("--branch-prefix", "-p")]
        public string BranchPrefix { get; set; } = "issues";

        [CliOption("--no-pull")]
        public bool NoPull { get; set; }

        [CliOption("--force")]
        public bool Force { get; set; }

        public Task<int> Execute(CancellationToken cancellationToken) { ... }
    }

    [CliCommand("prd", Description = "Generate PRD and development plan")]
    public partial class PrdCommand : ICliCommand
    {
        [CliArgument(0, Name = "issue-url", Required = false)]
        public string? IssueUrl { get; set; }

        [CliOption("--output", "-o")]
        public string? OutputDirectory { get; set; }

        [CliOption("--force", "-f")]
        public bool Force { get; set; }

        [CliOption("--update-issue")]
        public bool UpdateIssue { get; set; }

        public Task<int> Execute(CancellationToken cancellationToken) { ... }
    }

    [CliCommand("work", Description = "Start/continue working on issue")]
    public partial class WorkCommand : ICliCommand
    {
        [CliArgument(0, Name = "issue-url", Required = false)]
        public string? IssueUrl { get; set; }

        [CliOption("--status-only")]
        public bool StatusOnly { get; set; }

        [CliOption("--continue", "-c")]
        public bool Continue { get; set; }

        public Task<int> Execute(CancellationToken cancellationToken) { ... }
    }

    [CliCommand("pr", Description = "Create pull request")]
    public partial class PrCommand : ICliCommand
    {
        [CliArgument(0, Name = "issue-url", Required = false)]
        public string? IssueUrl { get; set; }

        [CliOption("--draft", "-d")]
        public bool Draft { get; set; }

        [CliOption("--base", "-b")]
        public string? BaseBranch { get; set; }

        [CliOption("--title", "-t")]
        public string? Title { get; set; }

        [CliOption("--no-checklist")]
        public bool NoChecklist { get; set; }

        [CliOption("--yes", "-y")]
        public bool SkipConfirmation { get; set; }

        public Task<int> Execute(CancellationToken cancellationToken) { ... }
    }

    [CliCommand("feedback", Description = "Review and resolve PR feedback")]
    public partial class FeedbackCommand : ICliCommand
    {
        [CliArgument(0, Name = "pr-url", Required = false)]
        public string? PrUrl { get; set; }

        [CliOption("--assess-only")]
        public bool AssessOnly { get; set; }

        [CliOption("--auto-reply")]
        public bool AutoReply { get; set; }

        public Task<int> Execute(CancellationToken cancellationToken) { ... }
    }

    [CliCommand("status", Description = "Show work status")]
    public partial class StatusCommand : ICliCommand
    {
        [CliArgument(0, Name = "issue-url", Required = false)]
        public string? IssueUrl { get; set; }

        [CliOption("--json")]
        public bool JsonOutput { get; set; }

        public Task<int> Execute(CancellationToken cancellationToken) { ... }
    }
}
```

### External Dependencies

| Dependency | Purpose | Required |
|------------|---------|----------|
| `gh` CLI | GitHub API access (authentication, issues, PRs) | Yes |
| `git` CLI | Branch management, pull, push operations | Yes |
| `claude` CLI | Claude Code invocation | Optional |

### Issue Reference Parsing

```csharp
public class IssueReference
{
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public int Number { get; set; }

    public static IssueReference? Parse(string input, string? currentRepoOwner = null, string? currentRepoName = null)
    {
        // Full URL: https://github.com/owner/repo/issues/123
        // Short: owner/repo#123
        // Number only: #123 or 123
    }
}
```

### Branch Naming

Format: `{prefix}/{number}-{slug}`

- `prefix`: Configurable (default: `issues`)
- `number`: Issue number
- `slug`: Sanitized issue title (lowercase, hyphens, max 30 chars)

Example: `issues/42-add-user-authentication`

### Claude Code Integration

```bash
# Launch Claude with context
claude --print "You are working on GitHub issue #<number>.

Issue: <title>
<issue body>

PRD: docs/issue_<number>_PRD.md
Development Plan: .claude/plans/issue_<number>.md

Please analyze the requirements and begin implementation following the development plan."
```

---

## Document Templates

### Development Plan Template (`.claude/plans/issue_<number>.md`)

```markdown
# Development Plan: Issue #<number>

**Issue**: #<number>
**Title**: <issue title>
**Type**: <Feature/Bug Fix/Refactor/etc.>
**Priority**: <High/Medium/Low>

## Executive Summary

<Brief description of what needs to be done and why>

---

## Problem Statement

### Current Behavior
<What currently happens>

### Expected Behavior
<What should happen after implementation>

### Impact
<Business/user impact>

---

## Technical Analysis

### Files to Modify
<List of files that need changes>

### Dependencies
<External services, packages, or other issues this depends on>

### Architecture Considerations
<Any architectural decisions or patterns to follow>

---

## Implementation Plan

### Phase 1: <Name>
1. <Step 1>
2. <Step 2>

### Phase 2: <Name> (if applicable)
...

---

## Test Scenarios

### Scenario 1: <Name>
- **Given**: <preconditions>
- **When**: <action>
- **Then**: <expected result>

---

## Acceptance Criteria

- [ ] <Criterion 1>
- [ ] <Criterion 2>

---

## Related Files

- <file1>
- <file2>
```

### PRD Template (`docs/issue_<number>_PRD.md`)

```markdown
# Product Requirements Document: <Feature Name>

**Issue**: #<number>
**Title**: <issue title>
**Status**: Draft
**Created**: <date>
**Last Updated**: <date>

---

## Overview

<High-level description of the feature/fix - keep brief>

---

## Goals & Objectives

### Primary Goals
- <Goal 1>
- <Goal 2>

### Success Metrics
- <Metric 1>
- <Metric 2>

---

## Functional Requirements

### Must Have (P0)
- [ ] **FR-1**: <requirement>
- [ ] **FR-2**: <requirement>

### Should Have (P1)
- [ ] **FR-3**: <requirement>

---

## Timeline & Milestones

### Milestone 1: <Name>
- [ ] Task 1
- [ ] Task 2

### Milestone 2: <Name>
- [ ] Task 3

---

## Open Questions

- [ ] <Question 1>
- [ ] <Question 2>

---

## Technical Notes (Issue-Specific)

<Only include notes specific to THIS issue>

---

## Related
- Issue #<number>
```

---

## Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| `gh` CLI not installed | High | Medium | Check for dependency at startup, provide installation instructions |
| No GitHub authentication | High | Medium | Prompt user to run `gh auth login` |
| Branch already exists | Medium | Medium | Offer to switch to existing branch or create new |
| PRD already exists | Low | Medium | Ask to overwrite or skip (--force flag) |
| Network failures | Medium | Low | Retry logic, clear error messages |
| Claude CLI not installed | Low | Medium | Graceful degradation, show manual instructions |

---

## Implementation Milestones

### Milestone 1: Core Infrastructure
- [ ] Project setup with CliGenerator
- [ ] Issue URL/reference parsing
- [ ] Git service (branch detection, default branch, branch creation)
- [ ] GitHub service (issue fetching via `gh`)

### Milestone 2: Init and PRD Commands
- [ ] `solve init` command
- [ ] `solve prd` command
- [ ] PRD and plan templates
- [ ] Branch detection from name

### Milestone 3: Work and Status Commands
- [ ] `solve work` command
- [ ] `solve status` command
- [ ] Status analysis from PRD/plan files

### Milestone 4: Full Workflow
- [ ] Root `solve` command (full workflow)
- [ ] Claude Code integration

### Milestone 5: PR and Feedback Commands
- [ ] `solve pr` command
- [ ] `solve feedback` command
- [ ] PR template
- [ ] GitHub GraphQL for review threads

---

## Open Questions

- [x] Should PRDs be stored in `.claude/plans/` or `docs/`? → **PRD in `docs/`, plan in `.claude/plans/`**
- [x] Branch naming convention? → **`issues/<number>-<slug>`**
- [ ] Should we support a config file for defaults (`.solverc` or similar)?
- [ ] How to handle repositories without write access (fork workflow)?
- [ ] Should `solve feedback` auto-commit changes or leave for user?

---

## Appendix

### Example Usage Session

```bash
# Full workflow - from issue URL to implementation
$ solve https://github.com/myorg/myrepo/issues/42
Fetching issue #42...
  Title: Add user authentication
  Labels: enhancement, security
Switching to default branch (development)...
Pulling latest changes...
Creating branch: issues/42-add-user-authentication
Generating PRD at docs/issue_42_PRD.md...
Generating plan at .claude/plans/issue_42.md...
Launching Claude Code...

# Or step by step:
$ solve init https://github.com/myorg/myrepo/issues/42
$ solve prd
$ solve work

# When done:
$ solve pr
Creating PR for issue #42...
PR created: https://github.com/myorg/myrepo/pull/43

# After review feedback:
$ solve feedback
Fetching unresolved review threads...
Found 3 items to address...
```

### References

- [DeCronosGroep Claude Commands](https://github.com/DeCronosGroep/claude/tree/development/plugins/dcg/commands)
- [MintPlayer.CliGenerator](./SourceGenerators/Cli)
- [GitHub CLI (`gh`) Documentation](https://cli.github.com/)
- [Claude Code Documentation](https://docs.anthropic.com/en/docs/claude-code)
