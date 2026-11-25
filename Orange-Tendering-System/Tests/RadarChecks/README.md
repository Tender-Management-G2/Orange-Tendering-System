# Orange Radar checks

Pure analytics checks on an in-memory synthetic market (no database): market metrics, sealing rule, behavior patterns,
insufficient-data states, graph and network integrity, offer comparison and tender lifecycle.

Run from the web project folder:

```powershell
dotnet run --project Tests/RadarChecks                 # analytics, network, lifecycle
dotnet run --project Tests/RadarChecks -- --exports    # + Excel / Word / PDF exports against the local team DB (read-only)
```

If the web app is running from `bin/Debug`, build with `-o <folder>` and run the produced `RadarChecks.dll`.
