using System.Text.Json.Serialization;

namespace ArgipApi.Client;

// Property names match the JSON of the API one to one (PascalCase), including the API's own typos
// (Descrption, FiendlyName, NearestDilivery). Do not "fix" them: the JSON would no longer map.
// Type names match the schema names in the API's OpenAPI document (/swagger/v1/swagger.json).

// ---- Customer ----

/// <summary>Customer account returned by <c>GET v1/Customer/Info</c>.</summary>
public sealed record CustomerInfoModel
{
    public decimal CreditLimit { get; init; }
    public decimal CreditAvailable { get; init; }
    public string? CurrencyName { get; init; }

    /// <summary>Base discount in percent.</summary>
    public decimal Discount { get; init; }

    /// <summary>Additional discount in percent.</summary>
    public decimal ExtraDiscount { get; init; }

    /// <summary>
    /// True while your account is in the testing phase: orders saved with <c>Order/Save</c> are stored as test
    /// orders and never processed. When false, every saved order is a real order.
    /// </summary>
    public bool TestingPhase { get; init; }

    public string? Language { get; init; }
}

// ---- Categories ----

/// <summary>One category of the product tree. <see cref="ParentCategoryId"/> links it to its parent.</summary>
public sealed record CategoryDto
{
    public int CategoryId { get; init; }
    public int ParentCategoryId { get; init; }
    public string? Name { get; init; }
}

/// <summary>A category together with the path from the root of the tree.</summary>
public sealed record CategoryInfoDto
{
    public int CategoryId { get; init; }
    public string? BreadcrumbsTrail { get; init; }
    public IReadOnlyList<CategoryDto>? PathElements { get; init; }
}

// ---- Products ----

/// <summary>A tag assigned by Argip to a product (for example a material or a standard).</summary>
public sealed record TagModel
{
    public int Id { get; init; }
    public string? Value { get; init; }
}

/// <summary>A tag you assigned yourself with <c>PUT v1/CustomTag</c>.</summary>
public sealed record CustomTagModel
{
    public int Type { get; init; }
    public string? Value { get; init; }
}

/// <summary>
/// A product with stock and the prices valid for YOUR account. Quantities are in pieces; every price is per 100 pieces.
/// </summary>
public sealed record ProductModel
{
    public int ProductId { get; init; }

    /// <summary>Groups variants (for example sizes) of one article; use <c>GetProductsByBaseProductAsync</c> to list them.</summary>
    public int BaseProductId { get; init; }

    /// <summary>Argip's product index.</summary>
    public string? Index { get; init; }

    public string? ProductFullName { get; init; }
    public string? EanBarcode { get; init; }
    public string? MultiPackEanBarcode { get; init; }

    /// <summary>Pieces in the smallest package. Orders are rounded to full packages.</summary>
    public int SinglePackQuantityInPieces { get; init; }

    /// <summary>
    /// Number of SINGLE PACKS in one multipack, despite the name: NOT pieces. (<see cref="SinglePackQuantityInPieces"/> is in pieces.)
    /// </summary>
    public int MultiPackQuantityInPieces { get; init; }

    /// <summary>Quantity from which <see cref="YourPriceLevel1"/> applies.</summary>
    public int QuantityLimitLevel1 { get; init; }

    /// <summary>Quantity from which <see cref="YourPriceLevel2"/> applies.</summary>
    public int QuantityLimitLevel2 { get; init; }

    public string? PictureUrl { get; init; }

    /// <summary>List price, per 100 pieces.</summary>
    public decimal MainPrice { get; init; }

    /// <summary>Your price for quantities below the first limit, per 100 pieces.</summary>
    public decimal YourMainPrice { get; init; }

    /// <summary>Your price from <see cref="QuantityLimitLevel1"/>, per 100 pieces.</summary>
    public decimal YourPriceLevel1 { get; init; }

    /// <summary>Your price from <see cref="QuantityLimitLevel2"/>, per 100 pieces.</summary>
    public decimal YourPriceLevel2 { get; init; }

    public int CurrencyId { get; init; }
    public string? CurrencyName { get; init; }

    /// <summary>Pieces available right now.</summary>
    public int PiecesInStock { get; init; }

    public int OtherPossibleQuantity { get; init; }

    /// <summary>Expected next delivery (sic) - spelled this way by the API.</summary>
    public string? NearestDilivery { get; init; }

    /// <summary>Your own index, set with <c>MapProductAsync</c>.</summary>
    public string? YourIndex { get; init; }

    public string? YourProductFullName { get; init; }
    public string? YourEanBarcode { get; init; }
    public bool IsActive { get; init; }
    public decimal BoxWeight { get; init; }
    public decimal TaxRate { get; init; }
    public string? CountryOfOrigin { get; init; }
    public bool IsDeviation { get; init; }
    public string? Deviation { get; init; }
    public IReadOnlyList<TagModel>? Tags { get; init; }
    public IReadOnlyList<CustomTagModel>? CustomTags { get; init; }

    /// <summary>Ids of the categories this product belongs to.</summary>
    public IReadOnlyList<int>? CategoryMapping { get; init; }

    /// <summary>Set only for an account in the testing phase: the data was validated but not saved.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Request body of <c>PUT v1/Products</c>: your own index, name and EAN for an Argip product.
/// All three fields are overwritten, so a field left null CLEARS the value stored earlier.
/// </summary>
public sealed record MapProduct
{
    public int ProductId { get; init; }
    public string? EanBarcode { get; init; }
    public string? ProductFullName { get; init; }
    public string? Index { get; init; }
}

/// <summary>Request body of <c>PUT v1/CustomTag</c>.</summary>
public sealed record MapCustomTag
{
    public int ProductId { get; init; }
    public int TagType { get; init; }
    public required string TagValue { get; init; }
}

// ---- Customer parts ----

/// <summary>Your own part number assigned to an Argip product.</summary>
public sealed record CustomerPartsModel
{
    public int CustomerPartsId { get; init; }
    public int ProductId { get; init; }
    public string? Part { get; init; }

    /// <summary>Description (sic) - spelled this way by the API.</summary>
    public string? Descrption { get; init; }

    /// <summary>Set only for an account in the testing phase: the data was validated but not saved.</summary>
    public string? Message { get; init; }
}

/// <summary>Request body of <c>PUT v1/CustomerParts/</c>. Note: here the field is spelled correctly.</summary>
public sealed record NewCustomerParts
{
    public required string Part { get; init; }

    /// <summary>Sent as an empty string when not set, never null.</summary>
    public string Description { get; init; } = "";
    public int ProductId { get; init; }
}

// ---- Shipping addresses ----

/// <summary>A delivery address registered for your account.</summary>
public sealed record ShippingAddressModel
{
    public int ShippingAddressId { get; init; }

    /// <summary>Short name of the address (sic) - spelled this way by the API.</summary>
    public string? FiendlyName { get; init; }

    public string? PostalCode { get; init; }
    public string? Street { get; init; }
    public string? BuildingNumber { get; init; }
    public string? City { get; init; }
    public string? CountryCode { get; init; }
    public string? ContactPhone { get; init; }
    public string? ContactPerson { get; init; }
    public string? ReceivingCompanyName { get; init; }

    /// <summary>Set only for an account in the testing phase: the data was validated but not saved.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Request body of <c>PUT v1/ShippingAddress/</c>. Creates a real address in Argip's system. <see cref="FiendlyName"/> may be
/// empty, but <see cref="ReceivingCompanyName"/>, <see cref="PostalCode"/>, <see cref="Street"/> and <see cref="BuildingNumber"/>
/// must not be empty or blank. Strings longer than the documented maximum are silently truncated by the API.
/// </summary>
public sealed record NewShippingAddress
{
    /// <summary>Short name of the address (sic) - spelled this way by the API.</summary>
    public required string FiendlyName { get; init; }

    public required string PostalCode { get; init; }
    public required string Street { get; init; }
    public required string BuildingNumber { get; init; }
    public required string City { get; init; }
    public required string CountryCode { get; init; }
    public required string ContactPhone { get; init; }
    public required string ContactPerson { get; init; }

    /// <summary>Required: an empty or blank value makes the API answer 404 "friendly and company name couldn't be empty".</summary>
    public required string ReceivingCompanyName { get; init; }

    /// <summary>Required: without it the API answers an empty 404 and creates nothing.</summary>
    public required string Email { get; init; }
}

// ---- Orders ----

/// <summary>
/// One line of an order: product and quantity in pieces (not packages). A quantity of 0 or less orders one full package
/// (without any warning), so never send it by accident.
/// </summary>
public sealed record OrderItem
{
    public int ProductId { get; init; }
    public int QuantityInPieces { get; init; }
}

/// <summary>Request body of <c>POST v1/Order/Save</c>.</summary>
public sealed record Order
{
    public const int MaxCustomerOrderNumberLength = 30;

    public int ShippingAddressId { get; init; }

    /// <summary>
    /// Your own order number, echoed back in the response. At most 30 characters: a longer value makes Save fail with an
    /// empty 404 and nothing is saved, so the client rejects it before sending.
    /// </summary>
    public string? CustomerOrderNumber { get; init; }

    /// <summary>
    /// Requested execution date, compared with TODAY IN POLAND (the server's local date). It is always sent as
    /// <c>yyyy-MM-ddTHH:mm:ss</c> without an offset, whatever the DateTimeKind.
    /// </summary>
    [JsonConverter(typeof(LocalDateTimeConverter))]
    public DateTime ExecutionDate { get; init; }

    public string? Comments { get; init; }
    public required IReadOnlyList<OrderItem> Items { get; init; }
}

/// <summary>
/// Something the API changed or skipped while processing an order. Always inspect these, and branch on
/// <see cref="Type"/>, not on the text. Types:
/// 1 unknown product (item skipped);
/// 2 stock problem: in Calculate "Quantity unreachable" (the item stays in the totals with the rounded quantity),
///   in Save the quantity is reduced to the stock level;
/// 3 quantity larger than one package rounded up to full packages (only when the rounded quantity is within stock);
/// 4 Save only: out of stock, item removed from the order;
/// 5 inactive product, not on offer, or without a valid package size (item skipped).
/// </summary>
public sealed record WarningMessage
{
    public int Type { get; init; }
    public int? ProductId { get; init; }
    public string? Comment { get; init; }
}

/// <summary>One priced line of an order.</summary>
public sealed record OrderModelItem
{
    public ProductModel? Product { get; init; }

    /// <summary>Unit price used for this line, per 100 pieces.</summary>
    public decimal Price { get; init; }

    public decimal TaxRate { get; init; }

    /// <summary>Pieces you asked for.</summary>
    public int PiecesOrdered { get; init; }

    /// <summary>Pieces the API accepted (after rounding and stock checks). May differ from <see cref="PiecesOrdered"/>.</summary>
    public int PiecesReceived { get; init; }

    public decimal ItemNetValue { get; init; }
    public decimal ItemWeight { get; init; }
}

/// <summary>Result of <c>POST v1/Order/Calculate</c>: prices and checks, nothing is saved.</summary>
public sealed record CalcOrderModel
{
    public decimal TotalWeight { get; init; }
    public decimal TotalNetValue { get; init; }
    public decimal TotalGrossValue { get; init; }
    public decimal TaxValue { get; init; }
    public string? CurrencyName { get; init; }
    public IReadOnlyList<OrderModelItem>? Items { get; init; }
    public IReadOnlyList<WarningMessage>? WarningMessages { get; init; }
}

/// <summary>Result of <c>POST v1/Order/Save</c>: the order as it was stored.</summary>
public sealed record OrderModel
{
    public ShippingAddressModel? ShippingAddress { get; init; }
    public string? CustomerOrderNumber { get; init; }
    public DateTime ExecutionDate { get; init; }
    public string? Comments { get; init; }
    public decimal TotalWeight { get; init; }
    public decimal TotalNetValue { get; init; }
    public decimal TotalGrossValue { get; init; }
    public decimal TaxValue { get; init; }
    public string? CurrencyName { get; init; }
    public IReadOnlyList<OrderModelItem>? Items { get; init; }
    public IReadOnlyList<WarningMessage>? WarningMessages { get; init; }
}
