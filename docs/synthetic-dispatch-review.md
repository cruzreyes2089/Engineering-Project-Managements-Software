# Synthetic dispatch outcome classification

Offline review classifier models the boundary after a possible network send. Tests exercise missing durable attempt, timeout after possible commit, truncated response, unexpected statuses including 4xx/409/5xx, missing readback, duplicate/conflicting or absent records, and contradictory send observations. Even a successful 201 plus complete independent recovery match can only reach manual confirmation review. None of the results marks an actual transaction posted, authorizes retry, clears a hold or performs a network call.

No transport or operational journal wiring; this is not an end-to-end transport test. Actual returned GUID matching must be incorporated into independent recovery evidence; a caller-supplied result is not trusted provider evidence. No response status is assumed to prove non-commit. No native API write implementation or executable distribution.
