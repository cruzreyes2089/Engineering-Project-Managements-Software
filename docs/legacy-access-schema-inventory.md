# Legacy Access metadata inventory (source-specific mapping prerequisite)

The normalized CRM importer does not assume legacy table or column names. This optional Windows PowerShell utility inspects a trusted, closed, local copy using installed Access DAO, opened read-only. It records only local table/field/index/relationship metadata and source SHA-256, skips system/hidden and linked tables, never launches the Access application, opens recordsets or queries, reads link connection strings, exports row data, or uploads anything. The source file hash must remain unchanged before and after. A lock sidecar may be created by the engine; the original database should not be opened by this procedure. The report can contain sensitive schema names, so review before sharing and never commit actual reports to the public repo. Password-protected and linked databases need separate reviewed handling; no credentials are requested or captured.

Reference: Microsoft DAO OpenDatabase (ReadOnly argument), TableDefAttributeEnum and Field.ForeignName documentation:
https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/dbengine-opendatabase-method-dao
https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/tabledefattributeenum-enumeration-dao
https://learn.microsoft.com/en-us/office/client-developer/access/desktop-database-reference/field-foreignname-property-dao

The CI workflow checks PowerShell syntax only. Actual DAO behavior on installed Access 2016 (including 32/64-bit compatibility), real schema mapping, source row counts and extraction require acceptance on a trusted machine. No actual migration or user database inspection has occurred. Do not run against the original database or a database from an untrusted computer. No execution-policy bypass or provider installation required by this procedure.
