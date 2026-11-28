# Offline transaction history and latest review

The previous batch regression failed because its wrong-allocation fixture generated a different UUID on each sweep, exercising drift detection rather than stable correlation conflict. Use one wrong allocation UUID throughout the test; production review logic is unchanged.

Add a dedicated read-only paginated history inspector, including unresolved-only filter (default), operation reference, kind, draft/attempt states, reason and timestamps. Require existing rehearsal, preparation and attempt grants. No capability token, raw JSON, credential or provider UUID is exposed. No state change or new permission, migration, transport, or posting authority. Hold does not erase history. Pagination is bounded and not a locked snapshot across pages.

Independent Defender runner review 45770490380 stopped before retrieving the archive: active antivirus protection unavailable. No scan result or clearance. Existing ClamAV results and executable distribution hold remain unchanged. No real business data or local machine scan reports committed. Source-only development continues, with local execution and real credentials outside scope.
