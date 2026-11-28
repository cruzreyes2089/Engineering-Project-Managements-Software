# Build output isolation

The build workflow now refuses to publish into a nonempty existing candidate directory or a redirected output directory. It does not delete prior artifacts. This prevents ordinary accidental mixing of stale files in a trusted checkout; it is not a race-free boundary against hostile concurrent filesystem changes. Clean CI checkout expected. The existing inventory and static checks remain mandatory. No installer or executable distribution. Exact regression pending.
