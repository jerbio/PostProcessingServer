# PostProcessingServer tests

This suite owns PPS queues, host adapters and analytics behavior. It references the production projects; it does not compile linked copies of production sources or reference `TilerFront.Tests`. Front retains its API/core processor tests, and shared dispatcher tests continue to target `TilerCrossServerResources`.

From a Visual Studio Developer PowerShell, in the PostProcessingServer directory:

```powershell
MSBuild.exe .\PostProcessingServer.Tests\PostProcessingServer.Tests.csproj /restore /p:Configuration=Debug
dotnet vstest .\PostProcessingServer.Tests\bin\Debug\net48\PostProcessingServer.Tests.dll '/TestCaseFilter:Category!=Integration' /Logger:trx
```

Use Visual Studio MSBuild because the legacy web projects need its WebApplication targets. The default project-reference build compiles the production dependencies before the tests; do not substitute a PPS DLL HintPath or source links. If the existing TilerElements XML-serializer step is unavailable/locked during local verification, the established optional `/p:TilerElementsSGenPath=__skip_locked_serializer_for_verification__` override skips that step only. It was used for the 2026-10-16 verification; it does not establish deployment/serializer validation.

The three `Category=Integration` cases exercise PPS dispatcher recovery and the authoritative scheduler. Their `ScheduleUpdateDatabase` fixture creates and deletes a randomly named `PpsScheduleTests_*` LocalDB database using the Front application model required by the planner. It passes the connection explicitly and does not load host Web.config files. Offline tests never instantiate that fixture.

Database integration tests require a separately authorized validation run. When authorized and LocalDB is available:

```powershell
dotnet vstest .\PostProcessingServer.Tests\bin\Debug\net48\PostProcessingServer.Tests.dll '/TestCaseFilter:Category=Integration' /Logger:trx
```

The 2026-10-16 split preserved all existing discovery cases: 67 offline cases and three integration cases moved from Front; two new PPS boundary tests bring this suite to 72 cases. All 69 PPS offline cases passed. Integration cases compiled and were discovered, but were not executed. Front's two new boundary regressions and six existing shared-dispatch tests also passed.
