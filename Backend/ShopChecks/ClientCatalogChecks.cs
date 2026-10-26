using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;

internal static class ClientCatalogChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var wire = new JsonSerializerOptions(JsonSerializerDefaults.Web) { IncludeFields = true };
        var graph = InventoryGraph.Create(starter: false);
        var profile = new { playerId = Guid.NewGuid().ToString("D"), displayName = "shop_fixture",
            inventoryJson = JsonSerializer.Serialize(graph, wire), dust = 0, alloy = 0, cells = 0 };
        string snapshot = "";
        var handler = new CatalogueHandler(path => path switch
        {
            "/auth/login" => JsonSerializer.Serialize(new { token = new string('a', 64), displayName = "shop_fixture", expiresAt = DateTime.UtcNow.AddHours(1) }, wire),
            "/auth/me" => JsonSerializer.Serialize(profile, wire),
            "/auth/shop" => snapshot,
            _ => throw new InvalidOperationException("Unexpected request " + path)
        });
        // Inject only transport: the real async client handles authentication,
        // deserialization, catalogue validation and profile application.
        var client = new HttpClient(handler);
        var httpField = typeof(AccountClient).GetField("s_Http", BindingFlags.Static | BindingFlags.NonPublic)!;
        httpField.SetValue(null, client);
        int updates = 0;
        void Changed() => updates++;
        AccountClient.InventoryChanged += Changed;
        try
        {
            await AccountClient.AuthenticateAsync("http://127.0.0.1/", "fixture", "fixture-password", false, default);
            snapshot = JsonSerializer.Serialize(new { offers = ShopCatalog.Offers, profile }, wire);
            int beforeCatalogue = updates;
            var offers = await AccountClient.GetShopOffersAsync(default);
            check(offers.Length == ShopCatalog.Offers.Length && offers.Length > 64,
                "actual client accepts the complete catalogue beyond the old 64-offer limit");
            check(offers.Select(o => (o.Code, o.BuyDust, o.SellDust, o.FreeSupplyRestricted)).SequenceEqual(
                ShopCatalog.Offers.Select(o => (o.Code, o.BuyDust, o.SellDust, o.FreeSupplyRestricted))), "client preserves all server offers and restrictions");
            check(updates == beforeCatalogue + 1 && AccountClient.Inventory?.Validate() == InventoryError.None,
                "valid catalogue applies its account profile");

            snapshot = JsonSerializer.Serialize(new { offers = ShopCatalog.Offers.Select(o => new { o.Code, o.Name, o.BuyDust, o.SellDust }), profile }, wire);
            var olderOffers = await AccountClient.GetShopOffersAsync(default);
            check(!olderOffers.Single(o => o.Code == "pistol").FreeSupplyRestricted,
                "an older server's omitted restriction does not falsely promise unsellable purchases");

            async Task Reject(ShopOffer[]? invalid, string reason, bool missingProfile = false)
            {
                var previous = AccountClient.Inventory;
                int before = updates;
                snapshot = JsonSerializer.Serialize(new { offers = invalid, profile = missingProfile ? null : (object)profile }, wire);
                bool rejected = false;
                try { await AccountClient.GetShopOffersAsync(default); }
                catch (InvalidOperationException error) when (error.Message.StartsWith("Invalid supplier catalogue.")) { rejected = true; }
                check(rejected && ReferenceEquals(previous, AccountClient.Inventory) && updates == before,
                    "client rejects " + reason + " before applying profile");
            }
            await Reject(null, "missing offers");
            await Reject(new ShopOffer[InventoryCatalog.Definitions.Count + 1], "oversized catalogue");
            await Reject(new ShopOffer[] { null! }, "null offer");
            await Reject(new[] { new ShopOffer { Code = null!, BuyDust = 0, SellDust = 0 } }, "null item code");
            await Reject(new[] { new ShopOffer { Code = "", BuyDust = 0, SellDust = 0 } }, "empty item code");
            await Reject(new[] { new ShopOffer { Code = "unknown", BuyDust = 0, SellDust = 0 } }, "unknown item");
            await Reject(new[] { new ShopOffer("pistol", -2, 7) }, "invalid purchase price");
            await Reject(new[] { new ShopOffer("pistol", 0, -1) }, "invalid sale price");
            await Reject(new[] { new ShopOffer("pistol", 0, 7), new ShopOffer("pistol", 0, 7) }, "duplicate item code");
            await Reject(ShopCatalog.Offers, "missing profile", missingProfile: true);
        }
        finally
        {
            AccountClient.InventoryChanged -= Changed;
            httpField.SetValue(null, null);
            client.Dispose();
        }
    }

    private sealed class CatalogueHandler(Func<string, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(response(request.RequestUri!.AbsolutePath), Encoding.UTF8, "application/json") });
    }
}
