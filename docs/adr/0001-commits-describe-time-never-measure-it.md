# Commits and pull requests describe agent time; they never measure or split it

Agent time is measured only from session timestamps (blocks). Commits and pull requests are attributed to blocks purely to describe the work, and a block keeps a single category no matter how many commits it carries. Orphans create no time; the user can turn them into manual entries explicitly.

## Considered Options

- **Split a block's time across the categories of its commits** (so one pull request could count as "Support" while the rest of the block is "Development"). Rejected for now: it changes how time is measured, needs a policy for how to divide minutes between commits (evenly, by count, by gaps between them), and deserves its own design.
- **Give orphan commits a fixed amount of time automatically.** Rejected: that would invent time no source observed. Converting an orphan to a manual entry is a one-click, deliberate act instead.
