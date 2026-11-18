using System.Text.Json;
using NetCafeManager.Server.Authentication;
using NetCafeManager.Server.Inventory;
using NetCafeManager.Shared.Inventory;
using NetCafeManager.Shared.Networking;

namespace NetCafeManager.Server.Networking;

public sealed class ProductPurchaseTcpHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ProductPurchaseService _purchases;
    private readonly ProductInventoryService _inventory;
    private readonly AuthenticationService _authentication;

    public ProductPurchaseTcpHandler(ProductPurchaseService purchases, ProductInventoryService inventory, AuthenticationService authentication)
    {
        _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
    }

    public async Task<NetworkMessage?> HandleAsync(
        NetworkMessage request, TcpConnectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (request.Type is not (NetworkMessageTypes.ProductPurchase or NetworkMessageTypes.ProductList)) return null;
        if (context.User is null || string.IsNullOrWhiteSpace(context.SessionToken) ||
            !_authentication.TryValidateSession(context.SessionToken, out UserSession? session) ||
            session?.UserId != context.User.Id)
            return NetworkResponseFactory.Error(request, "PURCHASE_AUTH_REQUIRED", "Vui lòng đăng nhập lại.");
        if (request.Type == NetworkMessageTypes.ProductList)
        {
            var products = await _inventory.ListAsync(context.User, cancellationToken);
            return NetworkResponseFactory.Result(request, products.Where(x => x.Type == ServiceItemTypes.Product).ToList());
        }
        if (context.ActiveUsageSession is null)
            return NetworkResponseFactory.Error(request, "PURCHASE_SESSION_REQUIRED", "Cần có phiên sử dụng đang hoạt động.");

        PurchaseProductRequest? payload;
        try { payload = request.Data.Deserialize<PurchaseProductRequest>(JsonOptions); }
        catch (JsonException) { payload = null; }
        if (payload is null)
            return NetworkResponseFactory.Error(request, "PURCHASE_INVALID_REQUEST", "Dữ liệu mua hàng không hợp lệ.");

        InventoryResult<ProductPurchaseReceipt> result = await _purchases.PurchaseAsync(
            context.User, context.ActiveUsageSession.SessionId, payload, cancellationToken);
        return NetworkResponseFactory.Result(request, result);
    }
}
