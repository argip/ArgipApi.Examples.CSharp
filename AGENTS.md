# Agents Guidelines: ArgipApi.Examples.CSharp

Public example client for the Argip Client API (v1) in C# / .NET 10: a typed client, a console demo and a one-file quickstart. The code is documentation for customers: keep it simple and keep the educational comments.

## Structure

```
src/ArgipApi.Client/            client library: ArgipApiClient, TokenProvider, Models, ArgipApiOptions, ArgipApiException, Pagination, LocalDateTimeConverter
tests/ArgipApi.Client.Tests/    xUnit tests with a fake HTTP handler (no network): client tests and the demo's argument parsing tests
demo/ArgipApi.Demo/             console demo: one command per client method (Program.cs, Arguments.cs, Output.cs, DotEnv.cs)
quickstart/ArgipApi.Quickstart/ the whole flow in one file (links demo's DotEnv.cs)
```

## Commands

```
dotnet build -c Release
dotnet test
dotnet format --verify-no-changes
```

`.gitattributes` forces CRLF in the working copy on every platform (`eol=crlf`), so the format check also passes on Linux and macOS checkouts.

## Rules

- The API contract comes from the Argip API (source of truth) and is described in ArgipApi.AgentGuide (https://github.com/argip/ArgipApi.AgentGuide). Never invent endpoints or fields; when the API changes, update `src/ArgipApi.Client/Models.cs`, the client, demo, quickstart and README together.
- Property names are the API's JSON names, typos included (`Descrption`, `FiendlyName`, `NearestDilivery`). Do not fix them.
- Never commit secrets. Credentials only in environment variables or the git-ignored `.env`; `.env.example` has empty values.
- Tests must not touch the network: use `FakeHttpHandler`. Do not call the real API from tests or from automated runs.
- Methods that write (`Map*`, `Put*`, `Delete*`, `SaveOrderAsync`) are documented as such. `SaveOrderAsync` places a real order unless the account is in the testing phase; `PutShippingAddressAsync` creates a real address.
- One `ArgipApiClient` per application (token cache). Do not request a token per call.
- Style: file-scoped namespaces, primary constructors, UTF-8 without BOM, CRLF line endings (`.editorconfig`), lambda parameters `x`/`y`/`z`. Comments are in English and explain why or what the API does (this is example code, so comments teach).
