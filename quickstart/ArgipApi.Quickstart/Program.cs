// Argip Client API quickstart: the whole flow in one file.
//
// It authenticates, reads your account and the catalogue, prices a basket and then SAVES an order.
// WARNING: the last step PLACES A REAL ORDER unless your account is in the testing phase
// (the program prints "testing phase: True" in step 1 when it is). Read it before you run it.
//
// Run:  dotnet run --project quickstart/ArgipApi.Quickstart (from the repository root: .env is read from the current directory)
using System.Globalization;
using ArgipApi.Client;
using ArgipApi.Demo;

// Product names contain non-ASCII letters; without this the Windows console shows them garbled.
Console.OutputEncoding = System.Text.Encoding.UTF8;

// Same output on every machine, whatever its regional settings (decimal comma or point).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

try
{
    return await RunAsync();
}
catch (ArgipApiConfigurationException e)
{
    // Missing credentials or an invalid ARGIP_API_URL / ARGIP_TOKEN_URL.
    Console.Error.WriteLine(e.Message);
    return 1;
}
catch (ArgipApiException e)
{
    Console.Error.WriteLine($"Error {(int)e.StatusCode}: {e.ApiMessage ?? (e.Body.Length > 0 ? e.Body : "(empty body)")}");
    if (e.FromTokenServer) Console.Error.WriteLine("The token server answered: check ARGIP_CLIENT_ID, ARGIP_CLIENT_SECRET and ARGIP_TOKEN_URL.");
    return 2;
}
catch (ArgipApiProtocolException e)
{
    Console.Error.WriteLine($"Error: unexpected response: {e.Message}");
    return 2;
}
catch (System.Text.Json.JsonException e)
{
    Console.Error.WriteLine($"Error: unexpected response (not valid JSON): {e.Message}");
    return 2;
}
catch (HttpRequestException e)
{
    Console.Error.WriteLine($"Network error: {e.Message}");
    return 2;
}
catch (TaskCanceledException)
{
    Console.Error.WriteLine("Network error: the request timed out.");
    return 2;
}

static async Task<int> RunAsync()
{
    // Step 0: configuration. Put ARGIP_CLIENT_ID and ARGIP_CLIENT_SECRET in .env (see .env.example).
    DotEnv.Load();
    var options = ArgipApiOptions.FromEnvironment();
    using var http = new HttpClient();
    var api = new ArgipApiClient(http, options);

    // Step 1: who am I? The client fetches and caches the access token automatically on the first call.
    var customer = await api.GetCustomerInfoAsync();
    Console.WriteLine($"Currency {customer.CurrencyName}, discount {customer.Discount}% + {customer.ExtraDiscount}%, testing phase: {customer.TestingPhase}");

    // Step 2: browse the catalogue. Pages start at 0; the X-Pagination header tells how many pages exist.
    var page = await api.GetProductsAsync(page: 0, pageSize: 5, activeOnly: true);
    Console.WriteLine($"{page.Pagination.TotalCount} active products, showing {page.Items.Count}:");
    foreach (var product in page.Items)
        Console.WriteLine($"  {product.ProductId,8}  {product.Index,-25} stock {product.PiecesInStock,8} pcs  your price {product.YourMainPrice} {product.CurrencyName} / 100 pcs");

    // Step 3: price a basket WITHOUT saving it. Quantities are in pieces; the API rounds up to full packages.
    // Only products that are in stock and have a known package size make a meaningful order: the quantity is one package (in pieces).
    var basket = page.Items
        .Where(x => x.SinglePackQuantityInPieces > 0 && x.PiecesInStock >= x.SinglePackQuantityInPieces)
        .Take(2)
        .Select(x => new OrderItem { ProductId = x.ProductId, QuantityInPieces = x.SinglePackQuantityInPieces })
        .ToList();
    if (basket.Count == 0)
    {
        Console.WriteLine("No orderable products in stock on the first page - nothing to calculate.");
        return 0;
    }

    var calculation = await api.CalculateOrderAsync(basket);
    Console.WriteLine($"Net {calculation.TotalNetValue} {calculation.CurrencyName}, gross {calculation.TotalGrossValue}, weight {calculation.TotalWeight} kg");
    foreach (var warning in calculation.WarningMessages ?? []) Console.WriteLine($"  warning type {warning.Type} (product {warning.ProductId}): {warning.Comment}");

    // Step 4: save the order to your first delivery address, to be executed today.
    // An account without addresses gets an empty page.
    var addresses = await api.GetShippingAddressesAsync(page: 0, pageSize: 1);
    if (addresses.Items.Count == 0)
    {
        Console.WriteLine("No delivery address on your account - ask Argip to add one, then run again.");
        return 0;
    }

    if (!customer.TestingPhase) Console.WriteLine("WARNING: your account is NOT in the testing phase - the next step places a REAL order.");

    // The server compares the execution date with today's date in Poland, so "today" is taken in that time zone,
    // not on this machine. The client sends it as yyyy-MM-ddT00:00:00 without an offset.
    var polandNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"));

    var saved = await api.SaveOrderAsync(new Order
    {
        ShippingAddressId = addresses.Items[0].ShippingAddressId,
        ExecutionDate = polandNow.Date,
        // At most 30 characters.
        CustomerOrderNumber = $"QUICKSTART-{polandNow:yyyyMMddHHmm}",
        Comments = "Placed by the Argip API C# quickstart",
        Items = basket,
    });

    // A 200 response does not mean everything was saved: items can be reduced or removed (warnings type 2 and 4).
    // If this step ends in a timeout or an empty 404, the order may exist: do not run it again before checking with Argip.
    Console.WriteLine($"Saved {saved.Items?.Count ?? 0} item(s), net {saved.TotalNetValue} {saved.CurrencyName}");
    foreach (var warning in saved.WarningMessages ?? []) Console.WriteLine($"  warning type {warning.Type} (product {warning.ProductId}): {warning.Comment}");
    return 0;
}
