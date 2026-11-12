# Argip API examples for C# (.NET 10)

## What this is

A small, readable client for the **Argip Client API** (products, stock, prices, orders; OAuth 2.0 client credentials), a console demo and a one-file quickstart. The code is meant to be read and copied: every method says which endpoint it calls and whether it writes data.

The API contract (routes, fields, error behaviour, quirks) is described in the agent guide: <https://github.com/argip/ArgipApi.AgentGuide>. Humans and AI coding agents can use it as the reference for everything this repository does.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- API credentials (`client_id` and `client_secret`) from the Argip sales department: export@argip.com.pl

## Setup

```bash
cp .env.example .env
# edit .env: set ARGIP_CLIENT_ID and ARGIP_CLIENT_SECRET
```

`.env` is read from the **current directory**, so run every command from the repository root. It is ignored by git. Real environment variables take precedence over `.env`. Optional `ARGIP_API_URL` and `ARGIP_TOKEN_URL` default to the production servers.

## Run the quickstart

```bash
dotnet run --project quickstart/ArgipApi.Quickstart
```

One file, [`quickstart/ArgipApi.Quickstart/Program.cs`](quickstart/ArgipApi.Quickstart/Program.cs): reads your account, lists products, prices a basket and saves an order. Errors are printed as one line (exit code 1 for missing credentials or an invalid URL setting, 2 for an API, token server or network error).

> **WARNING:** the last step **places a real order** unless your account is in the testing phase. The program prints `testing phase: True/False` first. Read the file before you run it.

## Run the demo

```bash
dotnet run --project demo/ArgipApi.Demo -- <command> [options] [--json]
```

Add `--json` to any command to get raw JSON instead of a table. Run without arguments to list the commands. Exit codes: 0 success (an empty list prints `No products found.` and similar; a page past the end prints `No products on page N (there are M pages).`), 1 usage error, invalid URL setting (`ARGIP_API_URL` and `ARGIP_TOKEN_URL` must be absolute `http(s)` URLs) or missing credentials, 2 API, token server or network error (including a timeout and an unexpected, non-JSON answer). Every bad input is checked before anything is sent and gives a one-line message, never a stack trace. `--page` is 0 or more, `--size` is 1-1000.

| Command | What it does | Writes? |
|---|---|---|
| `customer` | Your account: credit, discounts, testing phase | no |
| `categories [--main]` | Category tree (or main categories only) | no |
| `category <id>` | One category with its path | no |
| `products list [--page N] [--size N] [--all-states] [--filter TEXT]` | One page of products (pages start at 0) | no |
| `products get <id>` / `ean <ean>` / `index <yourIndex>` | One product | no |
| `products category <id>` / `base <id>` | Products of a category / of a base product | no |
| `products map <productId> [--index X] [--name X] [--ean X]` | Store your own index, name, EAN. **All three are overwritten: an option you leave out clears the stored value** | **yes** |
| `products tag <productId> <tagType> <value>` | Store a custom tag | **yes** |
| `parts list [--page N] [--size N]` / `parts get <productId>` | Your part numbers | no |
| `parts put <productId> <part> [--description X]` | Add or replace your part number | **yes** |
| `parts delete <productId>` | Delete your part number | **yes** |
| `addresses list [--page N] [--size N]` / `addresses get <id>` | Your delivery addresses | no |
| `addresses put --name X --postal-code X --street X --building X --city X --country X --phone X --person X --email X --company X` | Create a delivery address in Argip's system | **yes, real data** |
| `order calculate <productId:qty>...` | Price a basket, nothing is saved | no |
| `order save --address <id> --date <yyyy-MM-dd> [--number X] [--comments X] <productId:qty>...` | Place an order. `--number` is at most 30 characters; `--date` must not be earlier than today in Poland | **yes, real order unless in testing phase** |

Examples:

```bash
dotnet run --project demo/ArgipApi.Demo -- customer
dotnet run --project demo/ArgipApi.Demo -- products list --size 10 --filter "DIN 931"
dotnet run --project demo/ArgipApi.Demo -- order calculate 12345:250 67890:1000
dotnet run --project demo/ArgipApi.Demo -- order save --address 42 --date <today or later, yyyy-MM-dd> --number PO-1 12345:250
```

Quantities are in **pieces** (not packages) and prices are **per 100 pieces**.

## Use the client in your project

Copy `src/ArgipApi.Client` into your solution (or add a project reference to it). The project file is self-contained (`net10.0`, nullable and implicit usings enabled), and the code uses C# 14, so it needs the .NET 10 SDK; with an older SDK, copy the `.cs` files into your own project only if it already uses C# 14. Keep **one** `ArgipApiClient` for the whole application: it caches the access token (valid for 8 hours) and renews it five minutes before it expires. A new client per request would fetch a new token every time.

```csharp
using ArgipApi.Client;

var http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }); // one per application
var api = new ArgipApiClient(http, ArgipApiOptions.FromEnvironment());

var page = await api.GetProductsAsync(page: 0, pageSize: 100, activeOnly: true);
await foreach (var product in api.GetAllProductsAsync(pageSize: 1000, activeOnly: true))
    Console.WriteLine(product.Index);
```

With dependency injection register it as a singleton: `services.AddSingleton(ArgipApiOptions.FromEnvironment())`, `services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(15) }))`, `services.AddSingleton<ArgipApiClient>()`. The connection lifetime lets a long-running service notice DNS changes. Do not use `AddHttpClient<ArgipApiClient>()`: typed clients are transient, so each instance would have its own token cache.

Failed calls throw `ArgipApiException` with `StatusCode`, `ApiMessage` (the API's `{"message": ...}`) and the raw `Body`; `FromTokenServer` tells whether the token server answered (wrong credentials, mistyped token URL). Other exceptions: `ArgipApiConfigurationException` (missing or invalid setting, nothing sent), `ArgipApiProtocolException` (a 200 answer that is `null` or a token response without `access_token`), `System.Text.Json.JsonException` (an answer that is not JSON, e.g. HTML from a proxy), `ArgumentException` / `ArgumentOutOfRangeException` (invalid local input, nothing sent), `HttpRequestException` / `TaskCanceledException` (network failure, timeout). Never retry `SaveOrderAsync` (or any other write) automatically after a timeout or an empty 404: check first, or you may place the order twice.

Behaviour worth knowing:

- Paged list methods (`GetProductsAsync`, `GetCustomerPartsAsync`, `GetShippingAddressesAsync`, `GetAllProductsAsync`) turn the API's empty `404` ("no items") into an empty page. The API cannot tell that apart from an unexpected server error, which is also an empty `404`. A token server error, even an empty `404`, always throws and is never turned into an empty page. A page past the end is not an error: the API answers `200` with an empty list and the real `Pagination`. Single-item methods and the category/base-product lists keep throwing. Paged methods reject `page < 0` and a page size outside 1-1000 before sending, because the API would answer an empty `404`.
- `MapProductAsync` overwrites `Index`, `ProductFullName` and `EanBarcode`: a field you leave out is cleared.
- `Order.CustomerOrderNumber` is at most 30 characters (the client throws `ArgumentException` before sending). `Order.ExecutionDate` is compared with today's date in Poland and is sent without a time zone offset.
- A quantity of 0 or less in an order item orders one full package, without any warning.
- `GetProductByYourIndexAsync` cannot find an index that contains `/` (ASP.NET Core does not decode `%2F` in route values).
- `NewShippingAddress.Email` is required; `NewShippingAddress.ReceivingCompanyName` is required too (empty or blank gives a 404); `NewCustomerParts.Description` is sent as `""`, never `null`. Too long strings are silently truncated by the API.
- In the testing phase (`TestingPhase = true`) `MapCustomTagAsync`, `MapProductAsync`, `PutShippingAddressAsync`, `PutCustomerPartAsync` and `DeleteCustomerPartAsync` validate the data but save nothing. The response is a normal model with `Message` set (new ids are 0), and reading the data back shows the old state. The demo prints `Message` on its own line.

## Things that surprise people

Details are in the [agent guide](https://github.com/argip/ArgipApi.AgentGuide).

1. **Quantities are in pieces; prices are per 100 pieces** ([quantities and prices](https://github.com/argip/ArgipApi.AgentGuide/blob/master/docs/quantities-and-prices.md)).
2. **`Order/Save` places a REAL order** unless `Customer/Info` says `TestingPhase = true`. A 200 response does not mean it was saved: check `Items` (empty = nothing saved) and `WarningMessages`. `Order/Calculate` does not reserve stock. In the testing phase the other writes (custom tags, product mapping, customer parts, shipping addresses) are validated but not saved, and their response has a `Message` field ([orders](https://github.com/argip/ArgipApi.AgentGuide/blob/master/docs/orders.md)).
3. **An unexpected server error comes back as an EMPTY 404, not 500**: for a read, log it and retry later. For `Order/Save` and the other writes an empty 404 or a timeout means the outcome is UNKNOWN: never resend automatically, check with Argip first, or the order may be duplicated ([errors](https://github.com/argip/ArgipApi.AgentGuide/blob/master/docs/errors.md)).
4. **Tokens**: request them with form fields (not JSON), cache them for their lifetime (8 h), refresh shortly before expiry, and retry once after 401 ([authentication](https://github.com/argip/ArgipApi.AgentGuide/blob/master/docs/authentication.md)).
5. **Limits and naming**: pages start at 0 and `pagesize` is 1-1000; max 500 items per order; some field names are misspelled on purpose and must be used as-is (`NearestDilivery`, `Descrption`, `FiendlyName`) ([endpoints](https://github.com/argip/ArgipApi.AgentGuide/blob/master/docs/endpoints.md)).

## Tests

```bash
dotnet test
```

The unit tests use a fake HTTP handler: no network and no credentials needed.

## License

MIT, see [LICENSE](LICENSE).
