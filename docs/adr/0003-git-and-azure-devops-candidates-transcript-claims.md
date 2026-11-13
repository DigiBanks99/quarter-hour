# Candidates come from git and Azure DevOps; transcripts only claim them

Commits are read from each project's local git history and pull requests from Azure DevOps through the Azure CLI (a prerequisite). Agent transcripts are searched only for claims (a commit SHA or pull request id in tool output), which decide attribution when more than one block could own a candidate. Unclaimed candidates are attributed by time.

## Considered Options

- **Transcripts only.** Rejected: every agent stores tool calls differently, heredoc and file-based commit messages are hard to parse, amended or failed commits are hard to tell apart, and commits or pull requests made by hand would be missed.
- **Git and Azure DevOps only.** Rejected: cannot tell apart two sessions working in the same repository at the same time.
- **GitHub.** Out of scope: the target environment uses only Azure DevOps.

## Consequences

- The reflog is used to find commits on branches deleted after a squash merge; it exists only on the machine where the work happened.
- Pull requests from forks live in the upstream repository, so every Azure DevOps remote is queried, and per-project extra remotes can be configured.
- AgentsView claims use its content search endpoint; older AgentsView versions fall back to attribution by time.
