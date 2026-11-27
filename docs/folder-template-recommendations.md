# Retired project category recommendations

Retired in schema 53. See [simplified template selection](preferred-folder-template.md). The schema 52 migration and immutable historical rows are retained for compatibility; no category settings or recommendation workflow is active.

Historical design (superseded):

# Business-defined project category recommendations — schema 52

Businesses can define optional categories and map each to an exact current template revision, including the optional CRC preset. No service categories are seeded and no project type is inferred from proposal title, scope, billing method or client. The project selector offers a category dropdown as a recommendation, not an assignment. The administrator may select any available template and must confirm the exact project selection. Existing project selections and overrides remain pinned. Editing a template makes its earlier recommendation stale until explicitly re-reviewed; disabled and stale rules are excluded from recommendations. Rule changes are append-only, audited, revision-checked and role-restricted.

The category is currently an optional selection-dialog input, not a persisted engineering project classification. No automatic project-type rules are applied on project creation; a general project taxonomy would require a distinct reviewed data model. Schema 51 upgrades add only the empty rules table. No storage activation or physical directory work. Exact-revision regression pending.
