using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

/// <summary>
/// A golden-file snapshot of the printer-feed wire shape (issue #238).
/// </summary>
/// <remarks>
/// <para>
/// PR #237 changed the MEANING of <c>OrderDto.Items</c> — root-only, with components nested under
/// <c>SideItems</c> — without adding, renaming or removing a single field. Every schema check on
/// both sides therefore passed, the printer-app kept scanning top-level items, and a live
/// restaurant's back kitchen silently got no ticket. The consumer only found out because a human
/// went looking.
/// </para>
/// <para>
/// This test exists to make that class of change VISIBLE at authoring time. It drives the real HTTP
/// endpoint — not the mapper, and not a hand-built <c>JsonSerializerOptions</c> — so the snapshot is
/// the bytes a printer actually receives, including the API's own naming policy and enum-as-string
/// converter. A semantic change then shows up as a diff a reviewer must consciously accept, instead
/// of as silence.
/// </para>
/// <para>
/// <b>Coverage:</b> two orders, chosen to reach the sub-DTOs a one-order snapshot leaves as `null`
/// or `[]` and therefore silently unpinned. Order A is the bundle: mixed kitchens, three levels
/// deep. Order B is a DELIVERY order carrying an address, a payment and a status-history row —
/// `deliveryAddress` above all, because a string-vs-object mismatch on exactly that field is what
/// once threw on the printer's deserializer and froze `_lastPollTime`, re-throwing every 5s forever.
/// Still NOT covered: the menu-backed item path (`menuID` set, `productId` null), which
/// <c>PrinterFeedIncludeTests</c> owns, and the TYPES of the ids, which are scrubbed as volatile.
/// </para>
/// <para>
/// <b>When this test fails:</b> read the diff. If the new shape is intended, update
/// <c>printer-feed.golden.json</c> in the same PR and say in the PR body which consumers were
/// checked — the printer-app (<c>OrderPrintService</c>) and the frontend
/// (<c>utils/orderItemTree.ts</c>, the receipt templates) both walk this tree. If it is not
/// intended, you have just been saved a silent production incident.
/// </para>
/// </remarks>
[Collection("Database Lane 1")]
public class PrinterFeedContractSnapshotTests : IntegrationTestBase
{
    public PrinterFeedContractSnapshotTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    private static readonly string GoldenPath = Path.Combine(
        AppContext.BaseDirectory, "Features", "Orders", "printer-feed.golden.json");

    private const string BundleOrderNumber = "PF238-A-BUNDLE";
    private const string DeliveryOrderNumber = "PF238-B-DELIVERY";

    private static bool IsSnapshotOrder(JsonNode? order) =>
        order?["orderNumber"]?.GetValue<string>() is BundleOrderNumber or DeliveryOrderNumber;

    /// <summary>
    /// Rewrite the golden file instead of asserting. Set <c>UPDATE_CONTRACT_SNAPSHOTS=1</c> locally
    /// when a shape change is intended; never set in CI, where an auto-updating snapshot would
    /// assert nothing at all.
    /// </summary>
    private static bool UpdateMode =>
        Environment.GetEnvironmentVariable("UPDATE_CONTRACT_SNAPSHOTS") == "1";

    /// <summary>
    /// The golden file in the SOURCE tree, for update mode only. Reads go through the copied file in
    /// the output directory, so a stale build cannot make the assertion pass against source.
    /// </summary>
    private static string SourceGoldenPath([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "printer-feed.golden.json");

    [Fact]
    public async Task PrinterFeed_WireShape_MatchesTheCommittedGoldenFile()
    {
        var actual = Normalize(await FetchRawFeedAsync());

        if (UpdateMode)
        {
            // Written WITH a trailing newline: pre-commit's `end-of-file-fixer` would otherwise add
            // one on the very commit that lands the file, and the comparison below would then fail
            // forever on a byte no human put there. A gate that is permanently red is a gate
            // everyone learns to ignore.
            await File.WriteAllTextAsync(SourceGoldenPath(), actual + Environment.NewLine);
            return;
        }

        File.Exists(GoldenPath).Should().BeTrue(
            $"the golden file must be committed and copied to the output directory; expected it at {GoldenPath}");
        var expected = await File.ReadAllTextAsync(GoldenPath);

        // TrimEnd on both sides for the same reason: the committed file carries the hook's newline,
        // `JsonNode.ToJsonString` never emits one.
        actual.TrimEnd().Should().Be(
            expected.ReplaceLineEndings().TrimEnd(),
            "the printer-feed wire shape changed. If that is intended, update printer-feed.golden.json in "
            + "the SAME PR and name the consumers you checked (printer-app OrderPrintService; frontend "
            + "orderItemTree + receipt templates). A field's MEANING can change without its name changing — "
            + "that is exactly what #237 did, and why this file exists.");
    }

    /// <summary>
    /// The two invariants the golden file is really guarding, asserted separately so a failure says
    /// WHICH one broke rather than just "the bytes differ".
    /// </summary>
    [Fact]
    public async Task PrinterFeed_KeepsItemsRootOnly_WithComponentsNestedToFullDepth()
    {
        var feed = JsonNode.Parse(await FetchRawFeedAsync())!;
        var order = feed["data"]!["items"]!.AsArray()
            .Single(o => o!["orderNumber"]!.GetValue<string>() == BundleOrderNumber)!;

        var items = order["items"]!.AsArray();
        items.Should().ContainSingle("`items` is ROOT-ONLY: a component must never also appear at top level");

        var combo = items.Single()!;
        combo["productName"]!.GetValue<string>().Should().Be("Menu Deal");

        var children = combo["sideItems"]!.AsArray();
        children.Should().HaveCount(2, "both components hang off their parent");
        children.Select(c => c!["productName"]!.GetValue<string>())
            .Should().BeEquivalentTo(["Beef Burger", "Fries"]);

        // Mixed kitchens on ONE combo — the case that produced no back-kitchen ticket at all.
        children.Select(c => c!["kitchenType"]!.GetValue<string>())
            .Should().BeEquivalentTo([nameof(KitchenType.FrontKitchen), nameof(KitchenType.BackKitchen)]);

        // Depth is arbitrary, not one level: a consumer that stops at the first level drops this row.
        var grandchild = children.Single(c => c!["productName"]!.GetValue<string>() == "Beef Burger")!["sideItems"]!
            .AsArray().Single()!;
        grandchild["productName"]!.GetValue<string>().Should().Be("Extra Patty");
    }

    private async Task<string> FetchRawFeedAsync()
    {
        // No X-Api-Key header: `ApiKeyAuthFilter` is open when `PrinterSettings:ApiKey` is unset,
        // which is the test configuration.
        var response = await Client.GetAsync("/api/orders/printer-feed");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Strip everything that legitimately differs between runs — ids, timestamps, and any order but
    /// the one this class seeds — then re-serialize indented so the golden file is diff-readable and
    /// a failure points at the field that moved.
    /// </summary>
    private static string Normalize(string rawJson)
    {
        var root = JsonNode.Parse(rawJson)!.AsObject();
        // The endpoint answers HTTP 200 even on failure (legacy printer contract), so a server-side
        // error arrives here as `success: false` and a `data` with no paging — an NRE three lines
        // down would hide the actual message.
        root["success"]!.GetValue<bool>().Should().BeTrue(
            "the feed reported an error instead of a payload: {0}", root["message"]?.ToJsonString() ?? "(none)");
        var data = root["data"]!.AsObject();
        var orders = data["items"]!.AsArray()
            .Where(IsSnapshotOrder)
            .Select(o => JsonNode.Parse(o!.ToJsonString())!)
            .ToArray();

        // The ENVELOPE is part of the contract too — the printer-app branches on `success` and reads
        // `data.items`, and this endpoint answers HTTP 200 even on failure precisely because of that
        // (see PrinterFeedController). So it is snapshotted, with only the counts scrubbed: those
        // move with whatever else the suite has seeded.
        var normalized = new JsonObject
        {
            ["success"] = root["success"]!.GetValue<bool>(),
            ["data"] = new JsonObject
            {
                ["items"] = new JsonArray(orders.Select(Scrub).ToArray()),
                ["totalCount"] = JsonValue.Create(VolatileMarker),
                ["page"] = data["page"]!.GetValue<int>(),
                ["pageSize"] = data["pageSize"]!.GetValue<int>(),
            },
        };

        return normalized.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings();
    }

    /// The first key every element of an array carries, in preference order, used to sort it.
    /// `null` means "leave the order alone" (a scalar array, or a mixed one).
    ///
    /// Sorting matters because the feed guarantees the order of NOTHING it returns: items are
    /// grouped from a flat list by parent id, and payments / status history / customizations have no
    /// `OrderBy` in the query at all. An unsorted snapshot flakes, and a flaky gate is an ignored
    /// gate. This asserts the SHAPE of the tree; if row order ever becomes contractual it needs its
    /// own test and a deterministic `OrderBy` in the mapper to back it.
    private static string? SortKeyFor(List<JsonNode> nodes)
    {
        foreach (var key in new[] { "orderNumber", "productName", "ingredientName", "paymentMethod", "toStatus" })
        {
            if (nodes.Count > 0 && nodes.All(n => n is JsonObject o && o.ContainsKey(key)))
            {
                return key;
            }
        }
        return null;
    }

    /// Plain ASCII on purpose: angle brackets would be \u003C-escaped by System.Text.Json and the
    /// golden file has to stay quick for a human to scan.
    private const string VolatileMarker = "__volatile__";

    private static readonly HashSet<string> VolatileKeys =
        ["id", "orderId", "productId", "menuID", "menuId", "parentOrderItemId", "orderDate", "createdAt", "updatedAt", "changedAt", "paymentDate"];

    /// <summary>
    /// Replace volatile VALUES while keeping their KEYS: a field disappearing is exactly the kind of
    /// contract change this file has to catch, so it must not be scrubbed away.
    /// </summary>
    private static JsonNode Scrub(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var result = new JsonObject();
                foreach (var (key, value) in obj.ToArray())
                {
                    result[key] = VolatileKeys.Contains(key) && value is not null
                        ? JsonValue.Create(VolatileMarker)
                        : Scrub(value);
                }
                return result;
            case JsonArray arr:
                // Sort item arrays by name. The feed does NOT guarantee child order — it groups a
                // flat list by parent id — so an unsorted snapshot would flake between runs and
                // teach everyone to ignore it. This asserts the SHAPE of the tree, which is the
                // contract; if row order ever becomes part of the contract it needs its own test
                // and a deterministic `OrderBy` in the mapper to back it.
                var scrubbed = arr.Select(Scrub).ToList();
                var sortKey = SortKeyFor(scrubbed);
                if (sortKey is not null)
                {
                    scrubbed = scrubbed
                        .OrderBy(n => n![sortKey]?.ToJsonString() ?? string.Empty, StringComparer.Ordinal)
                        .ToList();
                }
                return new JsonArray(scrubbed.ToArray());
            default:
                return node is null ? JsonValue.Create((string?)null)! : JsonNode.Parse(node.ToJsonString())!;
        }
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // A FrontKitchen combo containing a FrontKitchen burger (which itself carries a component)
        // and BackKitchen fries: mixed kitchens AND depth > 1, the two properties #237 changed the
        // handling of. Deliberately the shape the printer-app E2E needs (issue #238 ask 1).
        var combo = NewProduct("Menu Deal", ProductType.Menu, KitchenType.FrontKitchen);
        var burger = NewProduct("Beef Burger", ProductType.MainItem, KitchenType.FrontKitchen);
        var fries = NewProduct("Fries", ProductType.AddOn, KitchenType.BackKitchen);
        var patty = NewProduct("Extra Patty", ProductType.AddOn, KitchenType.FrontKitchen);
        context.AddRange(combo, burger, fries, patty);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = BundleOrderNumber,
            Type = OrderType.DineIn,
            TableNumber = 7,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 24.00m,
            Total = 24.00m,
            OrderDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };

        var comboItemId = Guid.NewGuid();
        var burgerItemId = Guid.NewGuid();
        order.Items.Add(NewItem(comboItemId, order.Id, combo.Id, "Menu Deal", 1, 24.00m, 24.00m, null));
        order.Items.Add(NewItem(burgerItemId, order.Id, burger.Id, "Beef Burger", 1, 12.00m, 0m, comboItemId));
        order.Items.Add(NewItem(Guid.NewGuid(), order.Id, fries.Id, "Fries", 2, 3.50m, 0m, comboItemId));
        order.Items.Add(NewItem(Guid.NewGuid(), order.Id, patty.Id, "Extra Patty", 1, 2.00m, 0m, burgerItemId));

        var deliveredDish = NewProduct("E2E Delivered Dish", ProductType.MainItem, KitchenType.FrontKitchen);
        context.Add(deliveredDish);

        context.Add(order);
        context.Add(BuildDeliveryOrder(deliveredDish.Id));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// A delivery order with an address, a payment and a status transition — the three collections
    /// the bundle order leaves empty, and with them the DTOs that could otherwise be restructured
    /// without moving a single byte of the golden file.
    /// </summary>
    private static Order BuildDeliveryOrder(Guid dishProductId)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = DeliveryOrderNumber,
            Type = OrderType.Delivery,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Completed,
            SubTotal = 15.00m,
            DeliveryFee = 5.00m,
            Total = 20.00m,
            TotalPaid = 20.00m,
            // Fixed and distinct from the bundle order's: the feed orders by OrderDate and ties are
            // broken by a random Guid, so two orders stamped `UtcNow` would swap places between runs.
            OrderDate = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            CreatedBy = "test",
            CustomerName = "E2E Customer",
            DeliveryAddress = new OrderAddress
            {
                Id = Guid.NewGuid(),
                Label = "Home",
                AddressLine1 = "Rue du Grand-Pre 45",
                City = "Geneve",
                PostalCode = "1202",
                Country = "CH",
                Phone = "+41000000000",
                DeliveryInstructions = "Ring twice",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "test",
            },
        };

        order.Items.Add(NewItem(Guid.NewGuid(), order.Id, dishProductId, "E2E Delivered Dish", 1, 15.00m, 15.00m, null));
        order.Payments.Add(new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            PaymentMethod = PaymentMethod.CreditCard,
            Amount = 20.00m,
            Status = PaymentStatus.Completed,
            PaymentDate = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        });
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FromStatus = OrderStatus.Pending,
            ToStatus = OrderStatus.Confirmed,
            Notes = "confirmed by test",
            ChangedAt = new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc),
            ChangedBy = "test",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        });

        return order;
    }

    private static Product NewProduct(string name, ProductType type, KitchenType kitchenType) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        BasePrice = 10m,
        IsActive = true,
        IsAvailable = true,
        Type = type,
        KitchenType = kitchenType,
        CreatedBy = "test",
    };

    private static OrderItem NewItem(
        Guid id, Guid orderId, Guid productId, string name, int quantity, decimal unitPrice, decimal itemTotal, Guid? parentId) => new()
        {
            Id = id,
            OrderId = orderId,
            ProductId = productId,
            ProductName = name,
            Quantity = quantity,
            UnitPrice = unitPrice,
            // Child rows carry UnitPrice for display but ItemTotal = 0 — the parent's total already
            // rolls up the combo price (OrderItemFactory convention, issue #54).
            ItemTotal = itemTotal,
            ParentOrderItemId = parentId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
        };
}
