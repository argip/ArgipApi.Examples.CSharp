using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ArgipApi.Client;

/// <summary>
/// Typed client for the Argip Client API (v1). One instance per application: it caches the access token.
/// Quantities are in pieces; prices are per 100 pieces.
/// </summary>
public sealed class ArgipApiClient(HttpClient http, ArgipApiOptions options, TimeProvider? time = null)
{
    // The API uses PascalCase property names (ProductId, QuantityInPieces), so no naming policy.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    private readonly TokenProvider _tokens = new(http, options, time ?? TimeProvider.System);

    // ---- Customer ----

    /// <summary>Your account: credit, discounts and whether you are in the testing phase.</summary>
    public Task<CustomerInfoModel> GetCustomerInfoAsync(CancellationToken ct = default) => GetAsync<CustomerInfoModel>("v1/Customer/Info", ct);

    // ---- Categories ----

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<CategoryDto>>("v1/Categories", ct);
    public Task<IReadOnlyList<CategoryDto>> GetMainCategoriesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<CategoryDto>>("v1/MainCategories", ct);
    public Task<CategoryInfoDto> GetCategoryAsync(int id, CancellationToken ct = default) => GetAsync<CategoryInfoDto>($"v1/Categories/{id}", ct);

    // ---- Products ----

    /// <summary>
    /// One page of products (page numbers start at 0, pageSize 1-1000; anything else throws <see cref="ArgumentOutOfRangeException"/> before sending). No matching products gives an empty page
    /// (the API answers an empty 404 for that, see <see cref="GetPageAsync{T}"/>).
    /// </summary>
    /// <param name="activeOnly">true: only active products; false: also inactive ones (always only products on offer).</param>
    /// <param name="quickFilter">Optional free-text filter, e.g. "DIN 931 M8x80".</param>
    public Task<Page<ProductModel>> GetProductsAsync(int page, int pageSize, bool activeOnly, string? quickFilter = null, CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        var path = $"v1/Products/{page}/{pageSize}/{(activeOnly ? "true" : "false")}";
        if (!string.IsNullOrEmpty(quickFilter)) path += "?quickfilter=" + Uri.EscapeDataString(quickFilter);
        return GetPageAsync<ProductModel>(path, ct);
    }

    /// <summary>Every product, page by page (yields nothing when there are none). Use pageSize 1000 for a full catalogue sync.</summary>
    public async IAsyncEnumerable<ProductModel> GetAllProductsAsync(int pageSize, bool activeOnly, string? quickFilter = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var page = 0; ; page++)
        {
            var result = await GetProductsAsync(page, pageSize, activeOnly, quickFilter, ct);
            foreach (var product in result.Items) yield return product;
            if (page >= result.Pagination.TotalPages - 1) yield break;
        }
    }

    public Task<ProductModel> GetProductAsync(int id, CancellationToken ct = default) => GetAsync<ProductModel>($"v1/Products/{id}", ct);
    public Task<IReadOnlyList<ProductModel>> GetProductsByCategoryAsync(int categoryId, CancellationToken ct = default) => GetAsync<IReadOnlyList<ProductModel>>($"v1/Products/Category/{categoryId}", ct);
    public Task<IReadOnlyList<ProductModel>> GetProductsByBaseProductAsync(int baseProductId, CancellationToken ct = default) => GetAsync<IReadOnlyList<ProductModel>>($"v1/Products/BaseProduct/{baseProductId}", ct);
    public Task<ProductModel> GetProductByEanAsync(string ean, CancellationToken ct = default) => GetAsync<ProductModel>($"v1/Products/Ean/{Uri.EscapeDataString(ean)}", ct);

    /// <summary>
    /// Finds a product by YOUR own index (set with <see cref="MapProductAsync"/>). The segment is URL-encoded, but an index
    /// containing '/' cannot be found this way: ASP.NET Core does not decode %2F in route values, so the API answers 404.
    /// </summary>
    public Task<ProductModel> GetProductByYourIndexAsync(string index, CancellationToken ct = default) => GetAsync<ProductModel>($"v1/Products/YourIndex/{Uri.EscapeDataString(index)}", ct);

    /// <summary>
    /// WRITES: stores your own index, name and EAN for an Argip product. All three fields are overwritten with the
    /// values you send: a field you leave out (null) CLEARS the stored value. In the testing phase the data is validated but
    /// not saved, and the response has <c>Message</c> set.
    /// </summary>
    public Task<ProductModel> MapProductAsync(MapProduct body, CancellationToken ct = default) => SendAsync<ProductModel>(HttpMethod.Put, "v1/Products", body, ct);

    /// <summary>WRITES: stores a custom tag for an Argip product. In the testing phase the data is validated but not saved, and the response has <c>Message</c> set.</summary>
    public Task<ProductModel> MapCustomTagAsync(MapCustomTag body, CancellationToken ct = default) => SendAsync<ProductModel>(HttpMethod.Put, "v1/CustomTag", body, ct);

    // ---- Customer parts ----

    /// <summary>One page of your part numbers; no parts gives an empty page (empty 404, see <see cref="GetPageAsync{T}"/>).</summary>
    public Task<Page<CustomerPartsModel>> GetCustomerPartsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        return GetPageAsync<CustomerPartsModel>($"v1/CustomerParts/{page}/{pageSize}", ct);
    }

    public Task<CustomerPartsModel> GetCustomerPartAsync(int productId, CancellationToken ct = default) => GetAsync<CustomerPartsModel>($"v1/CustomerParts/{productId}", ct);

    /// <summary>
    /// WRITES: adds or replaces your part number for a product. In the testing phase the data is validated but not saved,
    /// <c>Message</c> is set and a new part has <c>CustomerPartsId</c> 0. Too long strings are silently truncated.
    /// </summary>
    public Task<CustomerPartsModel> PutCustomerPartAsync(NewCustomerParts body, CancellationToken ct = default) => SendAsync<CustomerPartsModel>(HttpMethod.Put, "v1/CustomerParts/", body, ct);

    /// <summary>WRITES: deletes your part number for a product. In the testing phase nothing is deleted and <c>Message</c> is set.</summary>
    public Task<CustomerPartsModel> DeleteCustomerPartAsync(int productId, CancellationToken ct = default) => SendAsync<CustomerPartsModel>(HttpMethod.Delete, $"v1/CustomerParts/{productId}", null, ct);

    // ---- Shipping addresses ----

    /// <summary>One page of your delivery addresses; no addresses gives an empty page (empty 404, see <see cref="GetPageAsync{T}"/>).</summary>
    public Task<Page<ShippingAddressModel>> GetShippingAddressesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePaging(page, pageSize);
        return GetPageAsync<ShippingAddressModel>($"v1/ShippingAddress/{page}/{pageSize}", ct);
    }

    public Task<ShippingAddressModel> GetShippingAddressAsync(int id, CancellationToken ct = default) => GetAsync<ShippingAddressModel>($"v1/ShippingAddress/{id}", ct);

    /// <summary>
    /// WRITES: creates a new delivery address in Argip's system. It is real data, not a draft. In the testing phase the data is
    /// validated but not saved, <c>Message</c> is set and <c>ShippingAddressId</c> is 0.
    /// </summary>
    public Task<ShippingAddressModel> PutShippingAddressAsync(NewShippingAddress body, CancellationToken ct = default) => SendAsync<ShippingAddressModel>(HttpMethod.Put, "v1/ShippingAddress/", body, ct);

    // ---- Orders ----

    /// <summary>Prices and checks a basket without saving anything. Always read <c>WarningMessages</c>.</summary>
    public Task<CalcOrderModel> CalculateOrderAsync(IEnumerable<OrderItem> items, CancellationToken ct = default) => SendAsync<CalcOrderModel>(HttpMethod.Post, "v1/Order/Calculate", items.ToList(), ct);

    /// <summary>
    /// WRITES: places an order. It is a REAL order unless Customer/Info says TestingPhase = true.
    /// A 200 response does not guarantee anything was saved: check <c>Items</c> and <c>WarningMessages</c>.
    /// After a timeout, a network error or an empty 404 the outcome is UNKNOWN: do not resend automatically, check with Argip first
    /// or the order may be duplicated.
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="Order.CustomerOrderNumber"/> is longer than 30 characters (nothing is sent).</exception>
    public Task<OrderModel> SaveOrderAsync(Order order, CancellationToken ct = default)
    {
        if (order.CustomerOrderNumber is { Length: > Order.MaxCustomerOrderNumberLength })
            throw new ArgumentException($"CustomerOrderNumber is {order.CustomerOrderNumber.Length} characters long; the maximum is {Order.MaxCustomerOrderNumberLength}.");
        return SendAsync<OrderModel>(HttpMethod.Post, "v1/Order/Save", order, ct);
    }

    // ---- Plumbing ----

    // The API fails a negative page or a page size outside 1-1000 with an empty 404, which would look like "no items".
    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page), page, "Pages start at 0.");
        if (pageSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "The page size must be between 1 and 1000.");
    }

    private Task<T> GetAsync<T>(string path, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, path, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct);
        return await ReadJsonAsync<T>(response, ct);
    }

    /// <summary>
    /// Paged lists answer an EMPTY 404 when there is nothing to list, so an empty-body 404 is turned into an empty page here.
    /// The API cannot tell this apart from an unexpected server error (which is also an empty 404): if you expect data, check it another way.
    /// </summary>
    private async Task<Page<T>> GetPageAsync<T>(string path, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendRawAsync(HttpMethod.Get, path, null, ct);
        }
        catch (ArgipApiException e) when (e.StatusCode == HttpStatusCode.NotFound && e.Body.Length == 0 && !e.FromTokenServer)
        {
            // Only the list endpoint's own answer counts. An empty 404 from the token server (mistyped token URL, proxy)
            // is a configuration error and must never look like an empty catalogue.
            return new Page<T>([], new Pagination(0, 0, "", ""));
        }

        using var _ = response;
        var items = await ReadJsonAsync<IReadOnlyList<T>>(response, ct);
        // Paged lists describe the paging in the X-Pagination header (JSON), not in the body.
        var header = response.Headers.TryGetValues("X-Pagination", out var values) ? values.First() : null;
        var pagination = header is null ? new Pagination(items.Count, 1, null, null) : JsonSerializer.Deserialize<Pagination>(header, Json)!;
        return new Page<T>(items, pagination);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        string? rejectedToken = null;
        for (var attempt = 0; ; attempt++)
        {
            // On the second attempt tell the provider which token was rejected: it fetches a new one only if
            // no other caller has replaced it already (revoked token, server restart).
            var token = await _tokens.GetTokenAsync(rejectedToken, ct);
            using var request = new HttpRequestMessage(method, new Uri(options.ApiUrl, path));
            request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);

            var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                rejectedToken = token;
                continue;
            }

            if (response.IsSuccessStatusCode) return response;

            using (response)
            {
                throw ArgipApiException.From(response.StatusCode, await response.Content.ReadAsStringAsync(ct));
            }
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new ArgipApiProtocolException("The API returned null.");
}
