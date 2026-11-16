# Tariq supplier demo accounts

One-off additive provisioning and HTTP login verification for
`SupplierTariq30@Demo-orange.loacl` through `SupplierTariq36@Demo-orange.loacl`.
The `.loacl` spelling is intentional.

Supply the requested password through the process environment variable
`SUPPLIER_DEMO_PASSWORD`, then run this project with `dotnet run --project
Orange-Tendering-System/Tests/SupplierAccounts`. Remove the variable afterward.
No password is stored by this utility outside Identity's SQL Server password hashes.

Uses the application's Development connection configuration, including User Secrets,
with the normal launch-profile database `OrangeTenderingTeamDev`. Refuses a nonlocal
or non-team target. Existing accounts are skipped without repairs or password resets.
Creation of missing users, approved supplier profiles, and Supplier role assignments
is transactional. Verification reads committed records through fresh scopes, posts
to the actual login endpoint with antiforgery protection, and requires HTTP 200 from
the protected Supplier workspace. Startup demo provisioning and migrations are disabled
for this utility; no unrelated accounts are provisioned.

These accounts persist across normal application restarts. Use the `http` or `https`
launch profile; the explicitly named existing-database profile selects another database.
