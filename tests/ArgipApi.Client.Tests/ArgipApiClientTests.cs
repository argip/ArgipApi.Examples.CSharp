using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace ArgipApi.Client.Tests;

public class ArgipApiClientTests
{
    private const string CustomerJson = """{"CreditLimit":1000.0,"CreditAvailable":500.5,"CurrencyName":"PLN","Discount":19.0,"ExtraDiscount":3.0,"TestingPhase":true,"Language":"EN"}""";

    private readonly FakeHttpHandler _handler = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 8, 0, 0, TimeSpan.Zero));

    private ArgipApiClient CreateClient() => new(new HttpClient(_handler), new ArgipApiOptions
    {
        ClientId = "id-1",
        ClientSecret = "secret-1",
        ApiUrl = new Uri("https://api.test/"),
        TokenUrl = new Uri("https://auth.test/connect/token"),
    }, _time);

    [Fact]
    public async Task TokenIsRequestedOnceAndSentAsBearer()
    {
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        var client = CreateClient();

        var info = await client.GetCustomerInfoAsync();
        await client.GetCustomerInfoAsync();

        var token = Assert.Single(_handler.TokenRequests);
        Assert.Equal(HttpMethod.Post, token.Request.Method);
        Assert.Contains("grant_type=client_credentials", token.Body);
        Assert.Contains("client_id=id-1", token.Body);
        Assert.Contains("client_secret=secret-1", token.Body);
        Assert.Contains("scope=argipapi", token.Body);
        Assert.All(_handler.ApiRequests, x => Assert.Equal("Bearer tok-1", x.Request.Headers.Authorization!.ToString()));
        Assert.Equal("https://api.test/v1/Customer/Info", _handler.ApiRequests.First().Request.RequestUri!.ToString());
        Assert.True(info.TestingPhase);
        Assert.Equal(500.5m, info.CreditAvailable);
    }

    [Fact]
    public async Task TokenIsRefreshedFiveMinutesBeforeExpiry()
    {
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        var client = CreateClient();

        await client.GetCustomerInfoAsync();
        _time.Advance(TimeSpan.FromHours(8) - TimeSpan.FromMinutes(4));
        await client.GetCustomerInfoAsync();

        Assert.Equal(2, _handler.TokenRequests.Count());
        Assert.Equal("Bearer tok-2", _handler.ApiRequests.Last().Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ShortLivedTokenIsNotReused()
    {
        _handler.TokenExpiresIn = 200;
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        var client = CreateClient();

        await client.GetCustomerInfoAsync();
        await client.GetCustomerInfoAsync();

        Assert.Equal(2, _handler.TokenRequests.Count());
    }

    [Fact]
    public async Task UnauthorizedIsRetriedOnceWithFreshToken()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized);
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);

        var info = await CreateClient().GetCustomerInfoAsync();

        Assert.Equal("EN", info.Language);
        Assert.Equal(2, _handler.TokenRequests.Count());
        Assert.Equal("Bearer tok-2", _handler.ApiRequests.Last().Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task SecondUnauthorizedThrows()
    {
        _handler.Enqueue(HttpStatusCode.Unauthorized);
        _handler.Enqueue(HttpStatusCode.Unauthorized);

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
        Assert.Equal(2, _handler.ApiRequests.Count());
    }

    [Fact]
    public async Task ProductsPageReadsPaginationHeader()
    {
        _handler.Enqueue(HttpStatusCode.OK, """[{"ProductId":7,"Index":"DIN931-M8x80","NearestDilivery":"2026-11-20","IsActive":true,"Tags":[{"Id":1,"Value":"A2"}]}]""",
            x => x.Headers.Add("X-Pagination", """{"TotalCount":16817,"TotalPages":3364,"PrevPageLink":"","NextPageLink":"https://api.test/v1/Products/1/5/True"}"""));

        var page = await CreateClient().GetProductsAsync(0, 5, true, "DIN 931");

        Assert.Equal("https://api.test/v1/Products/0/5/true?quickfilter=DIN%20931", _handler.ApiRequests.Single().Request.RequestUri!.AbsoluteUri);
        Assert.Equal(7, Assert.Single(page.Items).ProductId);
        Assert.Equal("2026-11-20", page.Items[0].NearestDilivery);
        Assert.Equal("A2", page.Items[0].Tags![0].Value);
        Assert.Equal(new Pagination(16817, 3364, "", "https://api.test/v1/Products/1/5/True"), page.Pagination);
    }

    [Fact]
    public async Task AllProductsWalksEveryPage()
    {
        const string header = """{"TotalCount":2,"TotalPages":2,"PrevPageLink":"","NextPageLink":""}""";
        _handler.Enqueue(HttpStatusCode.OK, """[{"ProductId":1}]""", x => x.Headers.Add("X-Pagination", header));
        _handler.Enqueue(HttpStatusCode.OK, """[{"ProductId":2}]""", x => x.Headers.Add("X-Pagination", header));

        var ids = new List<int>();
        await foreach (var product in CreateClient().GetAllProductsAsync(1, activeOnly: false)) ids.Add(product.ProductId);

        Assert.Equal([1, 2], ids);
        Assert.Equal(["/v1/Products/0/1/false", "/v1/Products/1/1/false"], _handler.ApiRequests.Select(x => x.Request.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task ErrorWithMessageBody()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"message":"Bad execution date"}""");

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("Bad execution date", error.ApiMessage);
        Assert.Equal("""{"message":"Bad execution date"}""", error.Body);
    }

    [Fact]
    public async Task EmptyNotFound()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetProductAsync(1));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Null(error.ApiMessage);
        Assert.Equal("", error.Body);
    }

    [Fact]
    public async Task CalculateSendsPascalCaseJson()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"TotalNetValue":12.5,"CurrencyName":"PLN","Items":[],"WarningMessages":[{"Type":3,"ProductId":10,"Comment":"Rounded"}]}""");

        var result = await CreateClient().CalculateOrderAsync([new OrderItem { ProductId = 10, QuantityInPieces = 250 }]);

        var request = _handler.ApiRequests.Single();
        Assert.Equal(HttpMethod.Post, request.Request.Method);
        Assert.Equal("/v1/Order/Calculate", request.Request.RequestUri!.AbsolutePath);
        Assert.Equal("""[{"ProductId":10,"QuantityInPieces":250}]""", request.Body);
        Assert.Equal(3, result.WarningMessages![0].Type);
        Assert.Equal(12.5m, result.TotalNetValue);
    }

    [Fact]
    public async Task PathSegmentsAreEscaped()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ProductId":5}""");

        await CreateClient().GetProductByYourIndexAsync("A/B 1");

        Assert.Equal("/v1/Products/YourIndex/A%2FB%201", _handler.ApiRequests.Single().Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TokenErrorIsReported()
    {
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_client"}""") });

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("invalid_client", error.ApiMessage);
        Assert.Empty(_handler.ApiRequests);
    }

    [Fact]
    public async Task FiveConcurrentCallsShareOneTokenRequest()
    {
        for (var i = 0; i < 5; i++) _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        var client = CreateClient();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetCustomerInfoAsync()));

        Assert.Single(_handler.TokenRequests);
        Assert.Equal(5, _handler.ApiRequests.Count());
    }

    [Fact]
    public async Task ConcurrentUnauthorizedCallsShareOneRefresh()
    {
        // Both calls start with tok-1, which the server rejects: the second call must reuse the token the first one fetched.
        _handler.RejectedBearer = "tok-1";
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        var client = CreateClient();

        await Task.WhenAll(client.GetCustomerInfoAsync(), client.GetCustomerInfoAsync());

        Assert.Equal(2, _handler.TokenRequests.Count());
        Assert.Equal(4, _handler.ApiRequests.Count());
    }

    [Theory]
    [InlineData("""{"access_token":"tok-x","token_type":"Bearer"}""")]
    [InlineData("""{"access_token":"tok-x","expires_in":"abc","token_type":"Bearer"}""")]
    public async Task MissingOrInvalidExpiresInFallsBackToEightHours(string tokenJson)
    {
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tokenJson) });
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        _handler.Enqueue(HttpStatusCode.OK, CustomerJson);
        var client = CreateClient();

        await client.GetCustomerInfoAsync();
        await client.GetCustomerInfoAsync();

        Assert.Single(_handler.TokenRequests);
    }

    [Fact]
    public async Task TokenErrorMessageNamesTheTokenServer()
    {
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_client"}""") });

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Equal("Token server returned 400: invalid_client", error.Message);
        Assert.True(error.FromTokenServer);
    }

    [Fact]
    public async Task TokenResponseWithoutAccessTokenThrows()
    {
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"expires_in":28800}""") });

        var error = await Assert.ThrowsAsync<ArgipApiProtocolException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Contains("access_token", error.Message);
    }

    [Fact]
    public async Task EmptyNotFoundOnPagedProductsIsAnEmptyPage()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);

        var page = await CreateClient().GetProductsAsync(0, 10, true, "nothing like this");

        Assert.Empty(page.Items);
        Assert.Equal(new Pagination(0, 0, "", ""), page.Pagination);
    }

    [Fact]
    public async Task EmptyNotFoundFromTheTokenServerIsNeverAnEmptyPage()
    {
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient();

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => client.GetProductsAsync(0, 10, true));
        Assert.True(error.FromTokenServer);
        Assert.StartsWith("Token server returned 404", error.Message);

        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<ArgipApiException>(() => client.GetCustomerPartsAsync(0, 10));
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<ArgipApiException>(() => client.GetShippingAddressesAsync(0, 10));
        _handler.EnqueueToken(new HttpResponseMessage(HttpStatusCode.NotFound));
        await Assert.ThrowsAsync<ArgipApiException>(async () =>
        {
            await foreach (var _ in client.GetAllProductsAsync(10, activeOnly: true)) { }
        });
        Assert.Empty(_handler.ApiRequests);
    }

    [Fact]
    public async Task EmptyNotFoundFromTheApiIsNotMarkedAsATokenError()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetProductAsync(1));

        Assert.False(error.FromTokenServer);
        Assert.StartsWith("Argip API returned 404", error.Message);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 1001)]
    public async Task PagingOutOfRangeIsRejectedWithoutSending(int page, int pageSize)
    {
        var client = CreateClient();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetProductsAsync(page, pageSize, true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetCustomerPartsAsync(page, pageSize));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetShippingAddressesAsync(page, pageSize));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task PageSizeBoundsAreAccepted()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);
        _handler.Enqueue(HttpStatusCode.NotFound);

        await CreateClient().GetCustomerPartsAsync(0, 1);
        await CreateClient().GetCustomerPartsAsync(0, 1000);

        Assert.Equal(["/v1/CustomerParts/0/1", "/v1/CustomerParts/0/1000"], _handler.ApiRequests.Select(x => x.Request.RequestUri!.AbsolutePath));
    }

    [Fact]
    public async Task PageBeyondTheLastPageKeepsTheTotalCount()
    {
        _handler.Enqueue(HttpStatusCode.OK, "[]", x => x.Headers.Add("X-Pagination", """{"TotalCount":16817,"TotalPages":3364,"PrevPageLink":"","NextPageLink":""}"""));

        var page = await CreateClient().GetProductsAsync(5000, 5, true);

        Assert.Empty(page.Items);
        Assert.Equal(16817, page.Pagination.TotalCount);
        Assert.Equal(3364, page.Pagination.TotalPages);
    }

    [Fact]
    public async Task JsonNullFromTheApiIsAProtocolError()
    {
        _handler.Enqueue(HttpStatusCode.OK, "null");

        var error = await Assert.ThrowsAsync<ArgipApiProtocolException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Contains("null", error.Message);
    }

    [Fact]
    public async Task HtmlInsteadOfJsonFromTheApiThrowsJsonException()
    {
        _handler.Enqueue(HttpStatusCode.OK, "<html>proxy</html>");

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => CreateClient().GetCustomerInfoAsync());
    }

    [Fact]
    public async Task EmptyNotFoundOnPagedPartsAndAddressesIsAnEmptyPage()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);
        _handler.Enqueue(HttpStatusCode.NotFound);
        var client = CreateClient();

        Assert.Empty((await client.GetCustomerPartsAsync(0, 10)).Items);
        Assert.Empty((await client.GetShippingAddressesAsync(0, 10)).Items);
    }

    [Fact]
    public async Task AllProductsYieldsNothingWhenThereAreNone()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);

        var ids = new List<int>();
        await foreach (var product in CreateClient().GetAllProductsAsync(1000, activeOnly: true, "x")) ids.Add(product.ProductId);

        Assert.Empty(ids);
        Assert.Single(_handler.ApiRequests);
    }

    [Fact]
    public async Task NotFoundWithMessageOnPagedListStillThrows()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, """{"message":"Something else"}""");

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetProductsAsync(0, 10, true));

        Assert.Equal("Something else", error.ApiMessage);
    }

    [Fact]
    public async Task EmptyNotFoundOnSingleItemAndCategoryEndpointsStillThrows()
    {
        _handler.Enqueue(HttpStatusCode.NotFound);
        _handler.Enqueue(HttpStatusCode.NotFound, """{"message":"Products with categoryId = 5 not found"}""");
        var client = CreateClient();

        await Assert.ThrowsAsync<ArgipApiException>(() => client.GetShippingAddressAsync(1));
        var error = await Assert.ThrowsAsync<ArgipApiException>(() => client.GetProductsByCategoryAsync(5));
        Assert.Equal("Products with categoryId = 5 not found", error.ApiMessage);
    }

    [Fact]
    public async Task NonStringErrorMessageDoesNotBreakTheException()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"message":{"field":"x"},"other":1}""");

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Equal("""{"field":"x"}""", error.ApiMessage);
    }

    [Fact]
    public async Task NonJsonErrorBodyIsKeptRaw()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, "Unknown client");

        var error = await Assert.ThrowsAsync<ArgipApiException>(() => CreateClient().GetCustomerInfoAsync());

        Assert.Null(error.ApiMessage);
        Assert.Equal("Unknown client", error.Body);
    }

    [Fact]
    public async Task SaveOrderRejectsTooLongOrderNumberWithoutSending()
    {
        var order = new Order { CustomerOrderNumber = new string('X', 31), Items = [new OrderItem { ProductId = 1, QuantityInPieces = 1 }] };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => CreateClient().SaveOrderAsync(order));

        Assert.DoesNotContain("Parameter", error.Message);

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task SaveOrderSendsExecutionDateWithoutOffsetAndAcceptsThirtyCharacters()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"CustomerOrderNumber":"x","Items":[]}""");
        var order = new Order
        {
            ShippingAddressId = 3,
            CustomerOrderNumber = new string('X', 30),
            ExecutionDate = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Local),
            Items = [new OrderItem { ProductId = 1, QuantityInPieces = 100 }],
        };

        await CreateClient().SaveOrderAsync(order);

        var body = _handler.ApiRequests.Single().Body;
        Assert.Contains("\"ExecutionDate\":\"2026-11-12T00:00:00\"", body);
        Assert.DoesNotContain("+", body);
    }

    [Fact]
    public async Task CompanyNameIsSentAndOptionalPartDescriptionIsAnEmptyStringNotNull()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ShippingAddressId":1}""");
        _handler.Enqueue(HttpStatusCode.OK, """{"CustomerPartsId":1}""");
        var client = CreateClient();

        await client.PutShippingAddressAsync(new NewShippingAddress
        {
            FiendlyName = "n",
            PostalCode = "00-001",
            Street = "s",
            BuildingNumber = "1",
            City = "c",
            CountryCode = "PL",
            ContactPhone = "+48123456789",
            ContactPerson = "p",
            ReceivingCompanyName = "Acme",
            Email = "a@b.pl",
        });
        await client.PutCustomerPartAsync(new NewCustomerParts { ProductId = 1, Part = "P-1" });

        var bodies = _handler.ApiRequests.Select(x => x.Body).ToList();
        Assert.Contains("\"ReceivingCompanyName\":\"Acme\"", bodies[0]);
        Assert.Contains("\"Email\":\"a@b.pl\"", bodies[0]);
        Assert.Contains("\"Description\":\"\"", bodies[1]);
        Assert.DoesNotContain("null", bodies[0] + bodies[1]);
    }

    [Fact]
    public async Task TestingPhaseWriteResponseMessageIsParsedIntoTheModel()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ShippingAddressId":0,"Message":"Testing phase: the data is valid and would be saved in the production phase."}""");

        var saved = await CreateClient().PutShippingAddressAsync(new NewShippingAddress
        {
            FiendlyName = "",
            PostalCode = "00-001",
            Street = "s",
            BuildingNumber = "1",
            City = "c",
            CountryCode = "PL",
            ContactPhone = "+48123456789",
            ContactPerson = "p",
            ReceivingCompanyName = "Acme",
            Email = "a@b.pl",
        });

        Assert.Equal(0, saved.ShippingAddressId);
        Assert.Equal("Testing phase: the data is valid and would be saved in the production phase.", saved.Message);
    }

    [Fact]
    public async Task ResponseWithoutMessageGivesNull()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"CustomerPartsId":5}""");
        _handler.Enqueue(HttpStatusCode.OK, """{"ProductId":9}""");
        var client = CreateClient();

        Assert.Null((await client.DeleteCustomerPartAsync(9)).Message);
        Assert.Null((await client.MapCustomTagAsync(new MapCustomTag { ProductId = 9, TagType = 1, TagValue = "x" })).Message);
    }

    [Fact]
    public async Task MapProductSendsAllThreeFieldsEvenWhenOmitted()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"ProductId":9}""");

        await CreateClient().MapProductAsync(new MapProduct { ProductId = 9, Index = "A 1" });

        Assert.Equal("""{"ProductId":9,"EanBarcode":null,"ProductFullName":null,"Index":"A 1"}""", _handler.ApiRequests.Single().Body);
    }

    [Theory]
    [InlineData("ARGIP_API_URL", "argipapi.argip.com.pl")]
    [InlineData("ARGIP_API_URL", "localhost:5000")]
    [InlineData("ARGIP_TOKEN_URL", "not a url")]
    [InlineData("ARGIP_TOKEN_URL", "ftp://127.0.0.1/token")]
    [InlineData("ARGIP_TOKEN_URL", "http://[bad")]
    [InlineData("ARGIP_TOKEN_URL", "file:///c:/token")]
    public void InvalidUrlInEnvironmentIsAClearError(string variable, string value)
    {
        var names = new[] { variable, "ARGIP_CLIENT_ID", "ARGIP_CLIENT_SECRET" };
        var previous = names.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable("ARGIP_CLIENT_ID", "id");
            Environment.SetEnvironmentVariable("ARGIP_CLIENT_SECRET", "secret");
            Environment.SetEnvironmentVariable(variable, value);

            var error = Assert.Throws<ArgipApiConfigurationException>(ArgipApiOptions.FromEnvironment);

            Assert.Contains(variable, error.Message);
        }
        finally
        {
            for (var i = 0; i < names.Length; i++) Environment.SetEnvironmentVariable(names[i], previous[i]);
        }
    }
}
