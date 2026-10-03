---
name: dotnet-validation
description: "Build and validate XtreamForge changes using the repository's .NET, EditorConfig, analyzer, SonarAnalyzer, and test requirements. Use whenever C# or .NET code is created, modified, refactored, or reviewed."
---

# Purpose

Use this skill whenever C# or .NET code is created, modified, refactored, or reviewed.

The purpose is to ensure a change is actually valid before considering the task complete.

# Validation source of truth

Respect:

# Repository configuration facts

Read from the repository, never guess:

- Solution file: `XtreamForge.slnx` (`.slnx` format — use `dotnet build XtreamForge.slnx`, not a `.sln` file)
- Target framework: `net10.0` across all projects
- C# language version: C# 14 (inferred from `net10.0`); extension member syntax is in active use:
  ```csharp
  public static class FooExtensions
  {
      extension(IServiceCollection services)
      {
          public IServiceCollection AddSomething() { ... }
      }
  }
  ```
  Recognize and follow this pattern; do not convert it to classic static methods.
- `Directory.Build.props` sets globally:
  - `<Nullable>enable</Nullable>`
  - `<ImplicitUsings>enable</ImplicitUsings>`
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  - `<AnalysisLevel>latest</AnalysisLevel>`
  - `SonarAnalyzer.CSharp` applied to every `.csproj`
- `Directory.Packages.props` manages all NuGet versions centrally — never add a `Version` attribute to a `<PackageReference>` in a project file
- `.editorconfig` at root: 4-space indent, LF line endings, UTF-8, no trailing whitespace

# Validation source of truth

Respect:

- `.editorconfig`
- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`
- individual project files
- the current solution structure

Do not guess build configuration that can be read from these files.

# Validation workflow

Start with the narrowest useful validation.

For changes isolated to one project:

1. build the affected project;
2. inspect compiler and analyzer output;
3. fix diagnostics caused by the change;
4. run relevant tests.

For broader or cross-project changes, validate the entire solution when practical:

```
dotnet build XtreamForge.slnx
dotnet test XtreamForge.slnx
```

Do not repeatedly run the same failing command without investigating the failure.

# Warnings and analyzers

Warnings are treated as errors.

SonarAnalyzer.CSharp is part of the repository's quality gate.

A change must not introduce new:

- compiler warnings;
- .NET analyzer diagnostics;
- SonarAnalyzer.CSharp diagnostics.

Fix the underlying implementation rather than suppressing the diagnostic.

Do not add `#pragma warning disable` or `SuppressMessage` merely to pass validation.

Do not lower analyzer severity or modify repository analyzer configuration unless explicitly requested.

# EditorConfig

Before modifying a source file, respect every `.editorconfig` that applies to its directory.

Repository formatting and naming conventions take precedence over generic preferences.

Do not reformat unrelated code.

# Failures

When validation fails:

1. read the exact diagnostic;
2. identify the file and rule;
3. determine whether the current change caused it;
4. understand the reason for the rule;
5. correct the implementation;
6. rerun the smallest relevant validation.

Distinguish newly introduced failures from unrelated pre-existing problems.

# Completion

Do not state that a build or test passes unless it was actually executed successfully.

When validation could not be executed, report that explicitly.
