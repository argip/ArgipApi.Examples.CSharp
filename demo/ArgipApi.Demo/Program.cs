using System.Globalization;
using ArgipApi.Client;
using ArgipApi.Demo;

// Command line demo of the Argip Client API. Every command maps to exactly one client method (and one API call),
// so you can read this file next to the API documentation. Run without arguments to see the commands.

const string Help = """
    Usage: dotnet run --project demo/ArgipApi.Demo -- <command> [options] [--json]

    Commands:
      customer
      categories [--main]
      category <id>
      products list [--page N] [--size N] [--all-states] [--filter TEXT]
      products get <id> | ean <ean> | index <yourIndex>
      products category <id> | base <id>
      products map <productId> [--index X] [--name X] [--ean X]       (writes; overwrites all three, an omitted one is CLEARED)
      products tag <productId> <tagType> <value>                      (writes)
      parts list [--page N] [--size N]
      parts get <productId>
      parts put <productId> <part> [--description X]                  (writes)
      parts delete <productId>                                        (writes)
      addresses list [--page N] [--size N]
      addresses get <id>
      addresses put --name X --postal-code X --street X --building X --city X --country X --phone X --person X --email X --company X   (writes a real address)
      order calculate <productId:qty>...
      order save --address <id> --date <yyyy-MM-dd> [--number X (max 30 chars)] [--comments X] <productId:qty>...   (REAL order unless in testing phase)

    --json prints raw JSON instead of tables. Credentials: ARGIP_CLIENT_ID and ARGIP_CLIENT_SECRET (environment or .env).
    """;

DotEnv.Load();

// Product names contain non-ASCII letters; without this the Windows console shows them garbled.
Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(Help);
    return 0;
}

var json = args.Contains("--json");

try
{
    return await RunAsync(Arguments.Parse(args.Where(x => x != "--json").ToList()));
}
catch (UsageException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (ArgipApiConfigurationException e)
{
    // Missing ARGIP_CLIENT_ID / ARGIP_CLIENT_SECRET or an invalid ARGIP_API_URL / ARGIP_TOKEN_URL.
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (ArgumentException e)
{
    // The client refuses invalid input (for example a too long order number) before sending anything.
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (ArgipApiException e)
{
    // Business errors carry the API's message; an empty 404 can also hide an unexpected server error.
    Console.Error.WriteLine($"Error {(int)e.StatusCode}: {e.ApiMessage ?? (e.Body.Length > 0 ? e.Body : "(empty body)")}");
    if (e.FromTokenServer) Console.Error.WriteLine("The token server answered: check ARGIP_CLIENT_ID, ARGIP_CLIENT_SECRET and ARGIP_TOKEN_URL.");
    return 2;
}
catch (ArgipApiProtocolException e)
{
    Console.Error.WriteLine($"Error: unexpected response: {e.Message}");
    return 2;
}
catch (HttpRequestException e)
{
    // No answer at all (offline, DNS, firewall, wrong ARGIP_API_URL / ARGIP_TOKEN_URL).
    Console.Error.WriteLine($"Network error: {e.Message}");
    return 2;
}
catch (TaskCanceledException)
{
    // HttpClient reports its timeout (100 s by default) as a cancellation.
    Console.Error.WriteLine("Network error: the request timed out.");
    return 2;
}
catch (System.Text.Json.JsonException e)
{
    Console.Error.WriteLine($"Error: unexpected response (not valid JSON): {e.Message}");
    return 2;
}

async Task<int> RunAsync(Arguments a)
{
    if (a.Positional.Count == 0 || a.Positional[0] is "help")
    {
        Console.Error.WriteLine(Help);
        return 1;
    }

    // One HttpClient and one ArgipApiClient per process: the client caches the access token.
    using var http = new HttpClient();
    var apiOrNull = default(ArgipApiClient);
    ArgipApiClient Api() => apiOrNull ??= new ArgipApiClient(http, ArgipApiOptions.FromEnvironment());

    switch (a.At(0), a.At(1))
    {
        // GET v1/Customer/Info
        case ("customer", _):
            Output.Json(await Api().GetCustomerInfoAsync());
            return 0;

        // GET v1/Categories, or GET v1/MainCategories with --main
        case ("categories", _):
            var categories = a.Flag("main") ? await Api().GetMainCategoriesAsync() : await Api().GetCategoriesAsync();
            if (json) Output.Json(categories);
            else Output.Table(categories, ("CategoryId", x => x.CategoryId), ("ParentCategoryId", x => x.ParentCategoryId), ("Name", x => x.Name));
            return 0;

        // GET v1/Categories/{id}
        case ("category", _):
            {
                var id = a.RequiredInt(1, "id");
                Output.Json(await Api().GetCategoryAsync(id));
                return 0;
            }

        // GET v1/Products/{page}/{pagesize}/{activeonly}?quickfilter=  (pages start at 0)
        case ("products", "list"):
            {
                var (page, size) = a.Paging();
                var result = await Api().GetProductsAsync(page, size, activeOnly: !a.Flag("all-states"), a.Option("filter"));
                if (json)
                {
                    Output.Json(new { result.Items, result.Pagination });
                    return 0;
                }

                if (PrintIfEmpty(result.Items.Count, result.Pagination, page, "products")) return 0;
                PrintProducts(result.Items);
                Console.WriteLine($"Page {page + 1}/{result.Pagination.TotalPages}, {result.Pagination.TotalCount} products");
                return 0;
            }

        // GET v1/Products/{id}
        case ("products", "get"):
            {
                var id = a.RequiredInt(2, "id");
                Output.Json(await Api().GetProductAsync(id));
                return 0;
            }

        // GET v1/Products/Ean/{ean}
        case ("products", "ean"):
            {
                var ean = a.Required(2, "ean");
                Output.Json(await Api().GetProductByEanAsync(ean));
                return 0;
            }

        // GET v1/Products/YourIndex/{index}: finds a product by the index YOU mapped with "products map"
        case ("products", "index"):
            {
                var yourIndex = a.Required(2, "yourIndex");
                Output.Json(await Api().GetProductByYourIndexAsync(yourIndex));
                return 0;
            }

        // GET v1/Products/Category/{categoryid}
        case ("products", "category"):
            {
                var id = a.RequiredInt(2, "id");
                PrintProducts(await Api().GetProductsByCategoryAsync(id));
                return 0;
            }

        // GET v1/Products/BaseProduct/{baseproductid}
        case ("products", "base"):
            {
                var id = a.RequiredInt(2, "id");
                PrintProducts(await Api().GetProductsByBaseProductAsync(id));
                return 0;
            }

        // PUT v1/Products. WRITES: stores your own index, name and EAN for the product. The API overwrites all three.
        case ("products", "map"):
            {
                var map = new MapProduct
                {
                    ProductId = a.RequiredInt(2, "productId"),
                    Index = a.Option("index"),
                    ProductFullName = a.Option("name"),
                    EanBarcode = a.Option("ean"),
                };
                Console.Error.WriteLine("Note: --index, --name and --ean are all overwritten; an option you leave out clears the stored value.");
                var result = await Api().MapProductAsync(map);
                Output.WriteResult(result, result.Message, json);
                return 0;
            }

        // PUT v1/CustomTag. WRITES: stores a custom tag for the product.
        case ("products", "tag"):
            {
                var tag = new MapCustomTag
                {
                    ProductId = a.RequiredInt(2, "productId"),
                    TagType = a.RequiredInt(3, "tagType"),
                    TagValue = a.Required(4, "value"),
                };
                var result = await Api().MapCustomTagAsync(tag);
                Output.WriteResult(result, result.Message, json);
                return 0;
            }

        // GET v1/CustomerParts/{page}/{pagesize}
        case ("parts", "list"):
            {
                var (page, size) = a.Paging();
                var parts = await Api().GetCustomerPartsAsync(page, size);
                if (json) Output.Json(new { parts.Items, parts.Pagination });
                else if (!PrintIfEmpty(parts.Items.Count, parts.Pagination, page, "parts"))
                    Output.Table(parts.Items, ("CustomerPartsId", x => x.CustomerPartsId), ("ProductId", x => x.ProductId), ("Part", x => x.Part), ("Descrption", x => x.Descrption));
                return 0;
            }

        // GET v1/CustomerParts/{productId}
        case ("parts", "get"):
            {
                var productId = a.RequiredInt(2, "productId");
                Output.Json(await Api().GetCustomerPartAsync(productId));
                return 0;
            }

        // PUT v1/CustomerParts/. WRITES: adds or replaces your part number for the product.
        case ("parts", "put"):
            {
                var part = new NewCustomerParts
                {
                    ProductId = a.RequiredInt(2, "productId"),
                    Part = a.Required(3, "part"),
                    Description = a.Option("description") ?? "",
                };
                var result = await Api().PutCustomerPartAsync(part);
                Output.WriteResult(result, result.Message, json);
                return 0;
            }

        // DELETE v1/CustomerParts/{productId}. WRITES: removes your part number for the product.
        case ("parts", "delete"):
            {
                var productId = a.RequiredInt(2, "productId");
                var result = await Api().DeleteCustomerPartAsync(productId);
                Output.WriteResult(result, result.Message, json);
                return 0;
            }

        // GET v1/ShippingAddress/{page}/{pagesize}
        case ("addresses", "list"):
            {
                var (page, size) = a.Paging();
                var addresses = await Api().GetShippingAddressesAsync(page, size);
                if (json) Output.Json(new { addresses.Items, addresses.Pagination });
                else if (!PrintIfEmpty(addresses.Items.Count, addresses.Pagination, page, "addresses"))
                    Output.Table(addresses.Items, ("ShippingAddressId", x => x.ShippingAddressId), ("FiendlyName", x => x.FiendlyName), ("PostalCode", x => x.PostalCode),
                        ("City", x => x.City), ("Street", x => x.Street), ("BuildingNumber", x => x.BuildingNumber));
                return 0;
            }

        // GET v1/ShippingAddress/{id}
        case ("addresses", "get"):
            {
                var id = a.RequiredInt(2, "id");
                Output.Json(await Api().GetShippingAddressAsync(id));
                return 0;
            }

        // PUT v1/ShippingAddress/. WRITES: creates a NEW delivery address in Argip's system. It is real data (in the testing phase: validated, not saved).
        case ("addresses", "put"):
            {
                var address = new NewShippingAddress
                {
                    FiendlyName = a.RequiredOption("name"),
                    PostalCode = a.RequiredOption("postal-code"),
                    Street = a.RequiredOption("street"),
                    BuildingNumber = a.RequiredOption("building"),
                    City = a.RequiredOption("city"),
                    CountryCode = a.RequiredOption("country"),
                    ContactPhone = a.RequiredOption("phone"),
                    ContactPerson = a.RequiredOption("person"),
                    ReceivingCompanyName = a.RequiredOption("company"),
                    Email = a.RequiredOption("email"),
                };
                var result = await Api().PutShippingAddressAsync(address);
                Output.WriteResult(result, result.Message, json);
                return 0;
            }

        // POST v1/Order/Calculate: prices a basket, saves nothing. Items are <productId:quantityInPieces>.
        case ("order", "calculate"):
            {
                var basket = ParseItems(a.Positional.Skip(2));
                var calculation = await Api().CalculateOrderAsync(basket);
                if (json)
                {
                    Output.Json(calculation);
                    return 0;
                }

                PrintOrderLines(calculation.Items);
                PrintTotals(calculation.TotalNetValue, calculation.TaxValue, calculation.TotalGrossValue, calculation.TotalWeight, calculation.CurrencyName);
                PrintWarnings(calculation.WarningMessages);
                return 0;
            }

        // POST v1/Order/Save. WRITES: a REAL order unless your account is in the testing phase.
        case ("order", "save"):
            {
                var items = ParseItems(a.Positional.Skip(2));
                var number = a.Option("number");
                if (number is { Length: > Order.MaxCustomerOrderNumberLength })
                    throw new UsageException($"--number is {number.Length} characters long; the maximum is {Order.MaxCustomerOrderNumberLength}.");

                var order = new Order
                {
                    ShippingAddressId = a.RequiredIntOption("address"),
                    // The date is sent as typed. The server compares it with today's date in Poland, so a date earlier than that is rejected.
                    ExecutionDate = ParseDate(a.RequiredOption("date")),
                    CustomerOrderNumber = number,
                    Comments = a.Option("comments"),
                    Items = items,
                };

                // Check the testing phase first so the user sees whether the next call places a real order.
                var customer = await Api().GetCustomerInfoAsync();
                var note = $"TestingPhase: {customer.TestingPhase}";
                if (!customer.TestingPhase) note += Environment.NewLine + "WARNING: your account is NOT in the testing phase - this places a REAL order.";
                if (json) Console.Error.WriteLine(note);
                else Console.WriteLine(note);

                OrderModel saved;
                try
                {
                    saved = await Api().SaveOrderAsync(order);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgipApiException { StatusCode: System.Net.HttpStatusCode.NotFound, Body.Length: 0 })
                {
                    // The order may or may not have been saved. Resending automatically could create a duplicate.
                    Console.Error.WriteLine("The outcome of Order/Save is UNKNOWN. Do not resend it automatically: check with Argip whether the order exists first.");
                    throw;
                }

                if (json)
                {
                    Output.Json(saved);
                    return 0;
                }

                Console.WriteLine($"ExecutionDate: {saved.ExecutionDate:yyyy-MM-dd}");
                Console.WriteLine($"CustomerOrderNumber: {saved.CustomerOrderNumber}");
                PrintOrderLines(saved.Items);
                PrintTotals(saved.TotalNetValue, saved.TaxValue, saved.TotalGrossValue, saved.TotalWeight, saved.CurrencyName);
                PrintWarnings(saved.WarningMessages);
                return 0;
            }
        default:
            Console.Error.WriteLine($"Unknown command: {string.Join(' ', a.Positional.Take(2))}");
            Console.Error.WriteLine(Help);
            return 1;
    }

    // The API answers an empty list in two ways: an empty 404 (nothing at all, mapped to TotalCount 0 by the client)
    // and 200 [] for a page past the end (TotalCount > 0). Only the first means "nothing found".
    static bool PrintIfEmpty(int itemCount, Pagination pagination, int page, string noun)
    {
        if (itemCount > 0) return false;
        Console.WriteLine(pagination.TotalCount == 0
            ? $"No {noun} found."
            : $"No {noun} on page {page + 1} (there are {pagination.TotalPages} pages).");
        return true;
    }

    void PrintProducts(IEnumerable<ProductModel> products)
    {
        if (json) Output.Json(products);
        else
            Output.Table(products, ("ProductId", x => x.ProductId), ("Index", x => x.Index), ("ProductFullName", x => x.ProductFullName),
                ("PiecesInStock", x => x.PiecesInStock), ("YourMainPrice", x => x.YourMainPrice));
    }
}

// "123:250" means product 123, 250 pieces (pieces, not packages).
static List<OrderItem> ParseItems(IEnumerable<string> tokens)
{
    var items = new List<OrderItem>();
    foreach (var token in tokens)
    {
        var parts = token.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var productId)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
        {
            throw new UsageException($"Invalid item '{token}'. Use <productId>:<quantityInPieces>, e.g. 123:250.");
        }

        items.Add(new OrderItem { ProductId = productId, QuantityInPieces = quantity });
    }

    if (items.Count == 0) throw new UsageException("Give at least one item as <productId>:<quantityInPieces>.");
    return items;
}

static DateTime ParseDate(string text) =>
    DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        ? date
        : throw new UsageException($"Invalid date '{text}'. Use yyyy-MM-dd.");

static void PrintOrderLines(IReadOnlyList<OrderModelItem>? lines) =>
    Output.Table(lines ?? [], ("ProductId", x => x.Product?.ProductId), ("Index", x => x.Product?.Index), ("PiecesOrdered", x => x.PiecesOrdered),
        ("PiecesReceived", x => x.PiecesReceived), ("Price", x => x.Price), ("ItemNetValue", x => x.ItemNetValue));

static void PrintTotals(decimal net, decimal tax, decimal gross, decimal weight, string? currency)
{
    FormattableString line = $"Net {net} {currency}, tax {tax}, gross {gross}, weight {weight} kg";
    Console.WriteLine(line.ToString(CultureInfo.InvariantCulture));
}

// Warnings are part of a 200 response: the API skipped, reduced or rounded something. See WarningMessage for the types.
static void PrintWarnings(IReadOnlyList<WarningMessage>? warnings)
{
    foreach (var warning in warnings ?? [])
        Console.WriteLine($"Type {warning.Type} (product {warning.ProductId}): {warning.Comment}");
}
