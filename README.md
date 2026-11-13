# MiniCI — a tiny CI runner

MiniCI runs CI pipelines from a YAML file, on your own machine, with no server
and no dependencies. Define jobs and steps, declare dependencies between jobs,
and MiniCI schedules them, runs them in parallel where it can, captures logs,
and collects artifacts.

```bash
minici init          # write a sample minici.yml
minici run           # run minici.yml in this directory
minici runs          # list past runs
minici status <id>   # summary of one run
minici logs <id>     # full log of one run
```

## A pipeline

```yaml
name: my-app

env:
  DOTNET_CLI_TELEMETRY_OPTOUT: "1"

jobs:
  build:
    steps:
      - name: restore
        run: dotnet restore
      - name: build
        run: dotnet build -c Release --no-restore
    artifacts:
      - "src/**/bin/Release/*.dll"

  test:
    needs: [build]
    steps:
      - name: test
        run: dotnet test --no-build -c Release
```

Jobs run level by level: every job in a level only depends on jobs from
earlier levels, so a whole level runs in parallel (up to `--jobs N`, default
is the CPU count). A job whose dependency failed is skipped, never run.

## Step options

| Key                 | Meaning |
| ------------------- | ------- |
| `run`               | Shell command to execute (required). |
| `name`              | Display name (default: `step`). |
| `working-directory` | Directory the command runs in, relative to the pipeline file. |
| `timeout`           | Seconds before the step is killed (default: 300). |
| `continue-on-error` | Keep running later steps even if this one fails (default: `false`). |
| `env`               | Extra environment variables for this step. |

Environment variables merge in order pipeline → job → step, with later levels
winning. Artifacts use glob patterns (`*`, `?`, `**`) and are copied to
`.minici/artifacts/<run-id>/<job>/`. Every run is recorded as JSON plus a
plain-text log under `.minici/runs/`.

## Design decisions

- **Zero external dependencies.** The YAML subset parser is hand-written
  (about 200 lines) because a pipeline file only needs maps, lists, scalars,
  and comments — pulling in a YAML library would be the biggest dependency
  in the project for the smallest job.
- **DAG levels, not a single topological order.** Grouping jobs into levels
  makes parallelism obvious and keeps the scheduler to one Kahn's-algorithm
  pass; a dependency cycle is reported with the cycle itself.
- **Processes, not threads, for steps.** Each step is a real shell process,
  so any command works, output is captured faithfully, and a timeout kills
  the whole process tree.
- **The run store is just files.** JSON records and text logs under
  `.minici/` are inspectable with ordinary tools and need no database.

## Run it

Requires .NET 8 SDK.

```bash
dotnet build -c Release
dotnet src/MiniCI/bin/Release/net8.0/MiniCI.dll run
```

## Tests

```bash
dotnet test
```

28 xUnit tests cover the YAML parser, pipeline validation, DAG scheduling
(including cycle detection), the step executor (exit codes, env, timeouts),
glob matching, artifact collection, and full end-to-end runs.

> Note: `dotnet test` relies on vstest, which cannot connect to its test host
> in some sandboxed environments. The tests themselves are ordinary xUnit
> `[Fact]`s and can also be run with a small reflection runner that invokes
> each fact directly.
