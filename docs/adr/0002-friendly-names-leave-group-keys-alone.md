# Friendly project names change labels, not group keys

A friendly name replaces a project's label wherever it is shown, in descriptions and in the `project` rule field and `{project}` template, but the agent group key stays derived from the raw repository name (`agent:<repo>`). Direct mappings on the board are rules on the group key, so deriving the key from the friendly name would silently orphan every existing mapping on rename. `{repo}` exposes the raw name for rules that must not move.

## Consequences

- Rules written against the `project` field follow the friendly name, so renaming a project can stop such a rule matching; the Projects page warns when a rename affects rules.
- Projects that share a friendly name merge in descriptions but remain separate groups on the mapping board.
