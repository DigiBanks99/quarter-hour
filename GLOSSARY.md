# Quarter Hour

Turns calendar events and AI agent coding sessions into a timesheet: measured time, mapped to timesheet categories, described in words a reviewer understands.

## Activities and time

**Activity**:
Something that took time and can appear on a timesheet: a calendar event, a block of agent work or a manual entry.
_Avoid_: Event (reserved for calendar events), item

**Agent session**:
One conversation with an AI coding agent, from its first message to its last.
_Avoid_: Chat, run, conversation

**Block**:
An unbroken stretch of one agent session's activity, ended by a pause longer than the idle gap. Each block is one agent activity.
_Avoid_: Chunk, segment, burst

**Manual entry**:
Time the user adds by hand because no source saw it.
_Avoid_: Custom entry, adjustment

## Mapping

**Group**:
Activities the mapping board treats as one: a meeting series, or all agent work in one project.
_Avoid_: Bucket, cluster

**Mapping**:
The link from a group (or a rule's match) to a timesheet category, or to "not relevant".
_Avoid_: Assignment (reserved for attribution), classification

**Project**:
The repository (or folder) an agent session worked in, as identified by its repository name.
_Avoid_: Repo (when meaning the timesheet concept), workspace

**Friendly name**:
The name the user gives a project for timesheets, such as "Concierge" for `concierge-api`. Several projects may share one friendly name.
_Avoid_: Alias, display name, nickname

## Work evidence

**Candidate**:
A commit or pull request the user authored in a project during the period, which might describe the work of some block.
_Avoid_: Match, hit

**Claim**:
Evidence in an agent session's transcript that the session itself made a particular commit or raised a particular pull request.
_Avoid_: Proof, link, tag

**Attribution**:
The decision that a candidate describes a particular block's work. Claimed candidates are attributed to the claiming block; others by time. The user may move a candidate to another block of its project, or mark it not relevant, and that choice wins.
_Avoid_: Assignment, mapping (reserved for categories)

**Orphan**:
A candidate that no block can be attributed. An orphan never creates time on its own; the user may turn it into a manual entry.
_Avoid_: Stray, unassigned commit
